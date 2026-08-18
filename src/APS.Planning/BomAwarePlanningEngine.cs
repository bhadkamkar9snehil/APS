using APS.Application;
using APS.Domain;

namespace APS.Planning;

/// <summary>
/// Canonical material-orchestration decorator.
///
/// Order of authority for BOM-enabled production runs:
/// PO manufacturing demand -> recursive BOM -> single-use time-phased supply coverage -> precomputed Campaign
/// material demand -> Campaign/heat/route/schedule -> persisted material tree/ledger.
///
/// Campaign therefore no longer reserves the same intermediate stock independently when BOM masters are active.
/// </summary>
public sealed class BomAwarePlanningEngine(
    PlanningEngine inner,
    IRecursiveMaterialRequirementEngine recursiveMaterialRequirements) : IPlanningEngine
{
    private const decimal QuantityToleranceMt = 0.0001m;

    public PlanningRunResult Run(PlanningRunRequest request)
    {
        if (request.BillsOfMaterial is not { Count: > 0 } || request.ProductionOrders.Count == 0)
            return inner.Run(request);

        var materialSpecifications = request.MaterialSpecifications ?? Array.Empty<MaterialSpecification>();
        var seeds = BuildDemandSeeds(request.ProductionOrders, materialSpecifications);
        if (seeds.Count == 0) return inner.Run(request);

        var coverage = new UnifiedTimePhasedMaterialCoverageSession(
            request.HorizonStartUtc,
            request.Inventory,
            materialSpecifications,
            request.ExternalMaterialSupplies,
            request.CommittedMaterialSupplies);
        var recursive = recursiveMaterialRequirements.Explode(new RecursiveMaterialRequirementRequest(
            seeds,
            request.BillsOfMaterial,
            materialSpecifications,
            coverage));

        var precomputed = BuildCampaignMaterialDemand(request, recursive, materialSpecifications, out var handoffIssues);
        var effectiveRequest = request with { PrecomputedCampaignMaterialDemand = precomputed };
        var result = inner.Run(effectiveRequest);

        foreach (var requirement in recursive.Requirements)
            requirement.PlanVersionId = result.PlanVersionId;

        var materialPlan = MergeMaterialPlan(result, request, recursive, handoffIssues);
        var allMaterialIssues = recursive.Issues
            .Concat(handoffIssues)
            .DistinctBy(x => (x.Code, x.Message, x.SourceId))
            .ToArray();
        var schedule = result.Schedule with
        {
            Issues = result.Schedule.Issues
                .Concat(allMaterialIssues)
                .DistinctBy(x => (x.Code, x.Message, x.SourceId))
                .ToArray()
        };

        return result with
        {
            MaterialPlan = materialPlan,
            Schedule = schedule,
            IsFeasible = result.IsFeasible && !materialPlan.Issues.Any(x => x.Severity == PlanningIssueSeverity.Error)
        };
    }

    private static IReadOnlyCollection<MaterialDemandSeed> BuildDemandSeeds(
        IReadOnlyCollection<ProductionOrder> productionOrders,
        IReadOnlyCollection<MaterialSpecification> materialSpecifications)
    {
        var specs = BuildSpecificationLookup(materialSpecifications);
        return productionOrders
            .Where(x => x.Status is ProductionOrderStatus.Planned or ProductionOrderStatus.Firmed)
            .Where(x => x.RemainingQuantityMt > QuantityToleranceMt)
            .OrderBy(x => x.DemandSource == DemandSourceType.MakeToOrder ? 0 : 1)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.RequiredDate)
            .ThenBy(x => x.ProductionOrderNumber, StringComparer.OrdinalIgnoreCase)
            .Select(po =>
            {
                specs.TryGetValue(po.MaterialCode, out var spec);
                return new MaterialDemandSeed(
                    po.Id,
                    po.MaterialCode,
                    spec?.MaterialSpecificationCode,
                    po.GradeCode,
                    po.FinalCrossSectionCode,
                    po.RemainingQuantityMt,
                    "MT",
                    po.RequiredDate,
                    po.Priority,
                    RouteCode: po.RouteCode,
                    GradeFamilyCode: po.GradeFamilyCode,
                    ProductFamilyCode: po.ProductFamilyCode,
                    QualificationCode: RequiresQualifiedSupply(po) ? $"PO-QUAL:{po.Id:N}" : null);
            })
            .ToArray();
    }

    private static bool RequiresQualifiedSupply(ProductionOrder po)
    {
        var requirement = po.Requirement;
        if (requirement is null) return false;
        return requirement.SegregationPolicy != SegregationPolicy.None ||
               !string.IsNullOrWhiteSpace(requirement.QualityClassCode) ||
               requirement.RequireVd.HasValue || requirement.ForbidVd.HasValue ||
               requirement.RequireReheating.HasValue || requirement.ForbidHotCharge.HasValue ||
               requirement.RequireTmt.HasValue || requirement.RequiredResourceId.HasValue ||
               !string.IsNullOrWhiteSpace(requirement.RequiredResourceGroupCode) ||
               requirement.ChemistryOverrides.Count > 0 || requirement.ProcessOverrides.Count > 0;
    }

    private static IReadOnlyCollection<PrecomputedCampaignMaterialDemand> BuildCampaignMaterialDemand(
        PlanningRunRequest request,
        RecursiveMaterialRequirementResult recursive,
        IReadOnlyCollection<MaterialSpecification> materialSpecifications,
        out IReadOnlyCollection<PlanningIssue> issues)
    {
        var result = new List<PrecomputedCampaignMaterialDemand>();
        var handoffIssues = new List<PlanningIssue>();
        var specs = BuildSpecificationLookup(materialSpecifications);
        var coverageByRequirement = recursive.CoverageAllocations
            .GroupBy(x => x.RequirementId)
            .ToDictionary(x => x.Key, x => x.ToArray());

        foreach (var po in request.ProductionOrders
                     .Where(x => x.Status is ProductionOrderStatus.Planned or ProductionOrderStatus.Firmed)
                     .Where(x => x.RemainingQuantityMt > QuantityToleranceMt))
        {
            var candidates = recursive.Requirements
                .Where(x => x.ProductionOrderId == po.Id && x.FlowType == BomFlowType.Input)
                .Where(x => IsSteelFeed(x, po, specs))
                .ToArray();

            MaterialRequirement[] feed;
            if (candidates.Length == 0)
            {
                handoffIssues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "BOM_STEEL_FEED_UNRESOLVED",
                    $"PO {po.ProductionOrderNumber} has no recursively-derived billet/bloom/slab feed node matching caster section {po.CasterSectionCode}. The rolling requirement remains visible as uncovered material; Campaign must not invent steel supply.",
                    po.Id));
                result.Add(new PrecomputedCampaignMaterialDemand(
                    po.Id,
                    po.RemainingQuantityMt,
                    po.RemainingQuantityMt,
                    0m,
                    0m,
                    po.RemainingQuantityMt,
                    Array.Empty<PlanningInventoryAllocation>()));
                continue;
            }

            var minimumDepth = candidates.Min(Depth);
            feed = candidates.Where(x => Depth(x) == minimumDepth).ToArray();
            if (feed.Any(x => !IsMt(x.MaterialUom)))
            {
                handoffIssues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "BOM_STEEL_FEED_UOM_INVALID",
                    $"PO {po.ProductionOrderNumber} resolved a steel-feed node whose UOM is not MT; Campaign steel quantities cannot silently convert UOM.",
                    po.Id));
            }

            var feedRequired = feed.Sum(x => x.GrossQuantity);
            var covered = feed.Sum(x => x.CoveredQuantity);
            var fresh = feed.Sum(x => x.InternalProductionQuantity);
            var shortfall = feed.Sum(x => x.ShortfallQuantity);
            var balance = covered + fresh + shortfall;
            if (Math.Abs(feedRequired - balance) > QuantityToleranceMt)
            {
                handoffIssues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "BOM_STEEL_FEED_BALANCE_INVALID",
                    $"PO {po.ProductionOrderNumber} steel-feed tree does not conserve quantity: required={feedRequired:0.####}, covered={covered:0.####}, internal={fresh:0.####}, shortfall={shortfall:0.####} MT.",
                    po.Id));
            }

            var feedIds = feed.Select(x => x.Id).ToHashSet();
            var allocations = feed
                .SelectMany(node => coverageByRequirement.GetValueOrDefault(node.Id) ?? Array.Empty<MaterialCoverageAllocation>())
                .Where(x => x.Quantity > QuantityToleranceMt)
                .Select(x => ToCampaignAllocation(po, x))
                .ToArray();

            result.Add(new PrecomputedCampaignMaterialDemand(
                po.Id,
                po.RemainingQuantityMt,
                feedRequired,
                covered,
                fresh,
                shortfall,
                allocations,
                feedIds.ToArray()));
        }

        issues = handoffIssues;
        return result;
    }

    private static PlanningInventoryAllocation ToCampaignAllocation(
        ProductionOrder po,
        MaterialCoverageAllocation allocation)
    {
        var use = allocation.SourceType switch
        {
            MaterialCoverageSourceType.KnownIncoming => PlanningInventoryUse.ExternalIntermediateFeed,
            MaterialCoverageSourceType.CommittedInternalProduction => PlanningInventoryUse.CommittedInternalProductionFeed,
            MaterialCoverageSourceType.PlannedInternalProduction => PlanningInventoryUse.CommittedInternalProductionFeed,
            _ => PlanningInventoryUse.IntermediateFeed
        };
        return new PlanningInventoryAllocation(
            po.Id,
            allocation.InventoryStage ?? InventoryStage.CastIntermediate,
            allocation.MaterialCode,
            allocation.GradeCode,
            allocation.CrossSectionCode,
            allocation.LocationCode,
            allocation.Quantity,
            use,
            allocation.SourceReference,
            allocation.AvailableFromUtc);
    }

    private static bool IsSteelFeed(
        MaterialRequirement requirement,
        ProductionOrder po,
        IReadOnlyDictionary<string, MaterialSpecification> specs)
    {
        MaterialSpecification? spec = null;
        if (!string.IsNullOrWhiteSpace(requirement.MaterialSpecificationCode))
            specs.TryGetValue(requirement.MaterialSpecificationCode, out spec);
        spec ??= specs.TryGetValue(requirement.MaterialCode, out var byMaterial) ? byMaterial : null;

        if (spec?.ProductForm is SteelProductForm.Billet or SteelProductForm.Bloom or SteelProductForm.Slab)
            return true;
        return !string.IsNullOrWhiteSpace(po.CasterSectionCode) &&
               string.Equals(requirement.CrossSectionCode?.Trim(), po.CasterSectionCode.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, MaterialSpecification> BuildSpecificationLookup(
        IReadOnlyCollection<MaterialSpecification> materialSpecifications)
    {
        var specs = new Dictionary<string, MaterialSpecification>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in materialSpecifications.Where(x => x.IsActive))
        {
            specs[spec.MaterialSpecificationCode] = spec;
            if (!string.IsNullOrWhiteSpace(spec.SapMaterialCode)) specs[spec.SapMaterialCode] = spec;
        }
        return specs;
    }

    private static MaterialPlanningResult MergeMaterialPlan(
        PlanningRunResult result,
        PlanningRunRequest request,
        RecursiveMaterialRequirementResult recursive,
        IReadOnlyCollection<PlanningIssue> handoffIssues)
    {
        var existing = result.MaterialPlan ?? new MaterialPlanningResult(
            Array.Empty<MaterialSupplyReservation>(),
            Array.Empty<ScheduledMaterialEvent>(),
            Array.Empty<MaterialBalanceEvent>(),
            Array.Empty<PlanningIssue>());
        var requirementById = recursive.Requirements.ToDictionary(x => x.Id);

        var recursiveRawReservations = recursive.CoverageAllocations
            .Where(x => IsMt(x.Uom) && x.InventoryStage == InventoryStage.RawMaterial && x.Quantity > QuantityToleranceMt)
            .Where(x => requirementById.ContainsKey(x.RequirementId))
            .Select(x => new MaterialSupplyReservation
            {
                PlanVersionId = result.PlanVersionId,
                ProductionOrderId = requirementById[x.RequirementId].ProductionOrderId ?? Guid.Empty,
                MaterialSpecificationCode = x.MaterialSpecificationCode ?? x.MaterialCode,
                GradeCode = x.GradeCode,
                CrossSectionCode = x.CrossSectionCode,
                InventoryStage = InventoryStage.RawMaterial,
                SupplyReference = x.SourceReference,
                LocationCode = x.LocationCode,
                QuantityMt = x.Quantity,
                AvailableFromUtc = x.AvailableFromUtc ?? request.HorizonStartUtc,
                Status = MaterialReservationStatus.Planned
            })
            .Where(x => x.ProductionOrderId != Guid.Empty)
            .ToArray();

        return existing with
        {
            Requirements = (existing.Requirements ?? Array.Empty<MaterialRequirement>())
                .Concat(recursive.Requirements)
                .DistinctBy(x => x.Id)
                .ToArray(),
            Reservations = existing.Reservations
                .Concat(recursiveRawReservations)
                .DistinctBy(x => (x.ProductionOrderId, x.MaterialSpecificationCode, x.LocationCode, x.AvailableFromUtc, x.QuantityMt))
                .ToArray(),
            Issues = existing.Issues
                .Concat(recursive.Issues)
                .Concat(handoffIssues)
                .DistinctBy(x => (x.Code, x.Message, x.SourceId))
                .ToArray()
        };
    }

    private static int Depth(MaterialRequirement requirement) =>
        string.IsNullOrWhiteSpace(requirement.RequirementPath)
            ? 0
            : requirement.RequirementPath.Split(" -> ", StringSplitOptions.RemoveEmptyEntries).Length;

    private static bool IsMt(string? uom) =>
        string.Equals(uom?.Trim(), "MT", StringComparison.OrdinalIgnoreCase);
}
