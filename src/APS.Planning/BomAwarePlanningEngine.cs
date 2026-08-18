using APS.Application;
using APS.Domain;

namespace APS.Planning;

/// <summary>
/// Production-kernel decorator that makes recursive BOM requirements part of the Plan Version result without
/// duplicating CampaignPlanningService's intermediate-material decision. Campaign allocations are replayed as
/// coverage evidence first; only raw-material snapshot inventory is independently netted below that boundary.
///
/// #14 will replace the run-scoped coverage session with the unified time-phased material ledger. The recursive
/// BOM engine and its requirement lineage remain unchanged.
/// </summary>
public sealed class BomAwarePlanningEngine(
    PlanningEngine inner,
    IRecursiveMaterialRequirementEngine recursiveMaterialRequirements) : IPlanningEngine
{
    private const decimal QuantityToleranceMt = 0.0001m;

    public PlanningRunResult Run(PlanningRunRequest request)
    {
        var result = inner.Run(request);
        if (request.BillsOfMaterial is not { Count: > 0 } || request.ProductionOrders.Count == 0)
            return result;

        var materialSpecifications = request.MaterialSpecifications ?? Array.Empty<MaterialSpecification>();
        var seeds = BuildDemandSeeds(request.ProductionOrders, materialSpecifications);
        if (seeds.Count == 0) return result;

        var coverage = new CampaignAwareMaterialCoverageSession(
            result.CampaignPlan.InventoryAllocations,
            request.Inventory);
        var recursive = recursiveMaterialRequirements.Explode(new RecursiveMaterialRequirementRequest(
            seeds,
            request.BillsOfMaterial,
            materialSpecifications,
            coverage));

        foreach (var requirement in recursive.Requirements)
            requirement.PlanVersionId = result.PlanVersionId;

        var reconciliationIssues = ReconcileSteelFeedQuantities(request, result.CampaignPlan, recursive);
        var materialPlan = MergeMaterialPlan(result, request, recursive, reconciliationIssues);
        var materialErrors = materialPlan.Issues.Any(x => x.Severity == PlanningIssueSeverity.Error);

        var schedule = result.Schedule with
        {
            Issues = result.Schedule.Issues
                .Concat(recursive.Issues)
                .Concat(reconciliationIssues)
                .DistinctBy(x => (x.Code, x.Message, x.SourceId))
                .ToArray()
        };

        return result with
        {
            MaterialPlan = materialPlan,
            Schedule = schedule,
            IsFeasible = result.IsFeasible && !materialErrors
        };
    }

    private static IReadOnlyCollection<MaterialDemandSeed> BuildDemandSeeds(
        IReadOnlyCollection<ProductionOrder> productionOrders,
        IReadOnlyCollection<MaterialSpecification> materialSpecifications)
    {
        var specs = new Dictionary<string, MaterialSpecification>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in materialSpecifications.Where(x => x.IsActive))
        {
            specs[spec.MaterialSpecificationCode] = spec;
            if (!string.IsNullOrWhiteSpace(spec.SapMaterialCode)) specs[spec.SapMaterialCode] = spec;
        }

        return productionOrders
            .Where(x => x.Status is ProductionOrderStatus.Planned or ProductionOrderStatus.Firmed or ProductionOrderStatus.Released)
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

    private static IReadOnlyCollection<PlanningIssue> ReconcileSteelFeedQuantities(
        PlanningRunRequest request,
        CampaignPlanningResult campaignPlan,
        RecursiveMaterialRequirementResult recursive)
    {
        var issues = new List<PlanningIssue>();
        var specs = new Dictionary<string, MaterialSpecification>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in request.MaterialSpecifications ?? Array.Empty<MaterialSpecification>())
        {
            specs[spec.MaterialSpecificationCode] = spec;
            if (!string.IsNullOrWhiteSpace(spec.SapMaterialCode)) specs[spec.SapMaterialCode] = spec;
        }

        foreach (var po in request.ProductionOrders)
        {
            var feedNodes = recursive.Requirements
                .Where(x => x.ProductionOrderId == po.Id && x.FlowType == BomFlowType.Input && x.ParentRequirementId.HasValue)
                .Where(x => IsSteelFeed(x, po, specs))
                .ToArray();
            if (feedNodes.Length == 0) continue;

            var minimumDepth = feedNodes.Min(Depth);
            var selectedFeed = feedNodes.Where(x => Depth(x) == minimumDepth).ToArray();
            var bomInternal = selectedFeed.Sum(x => x.InternalProductionQuantity);
            var bomCovered = selectedFeed.Sum(x => IsMt(x.MaterialUom) ? x.CoveredQuantityMt : 0m);
            campaignPlan.FreshSteelRequirementsMt.TryGetValue(po.Id, out var campaignFresh);

            var campaignIntermediate = campaignPlan.InventoryAllocations
                .Where(x => x.ProductionOrderId == po.Id &&
                            x.Use is PlanningInventoryUse.IntermediateFeed or
                                PlanningInventoryUse.ExternalIntermediateFeed or
                                PlanningInventoryUse.CommittedInternalProductionFeed or
                                PlanningInventoryUse.PlannedPurchaseFeed or
                                PlanningInventoryUse.PlannedTransferFeed or
                                PlanningInventoryUse.ManualPlannedFeed)
                .Sum(x => x.QuantityMt);

            if (Math.Abs(bomInternal - campaignFresh) > QuantityToleranceMt ||
                Math.Abs(bomCovered - campaignIntermediate) > QuantityToleranceMt)
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Warning,
                    "BOM_CAMPAIGN_STEEL_REQUIREMENT_MISMATCH",
                    $"PO {po.ProductionOrderNumber}: recursive BOM steel-feed basis is internal={bomInternal:0.####} MT, covered={bomCovered:0.####} MT while current Campaign planning is fresh={campaignFresh:0.####} MT, covered={campaignIntermediate:0.####} MT. The BOM tree is persisted explicitly; #14/#33 integration must resolve this before Campaign material arithmetic can be retired.",
                    po.Id));
            }
        }

        return issues;
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

    private static MaterialPlanningResult MergeMaterialPlan(
        PlanningRunResult result,
        PlanningRunRequest request,
        RecursiveMaterialRequirementResult recursive,
        IReadOnlyCollection<PlanningIssue> reconciliationIssues)
    {
        var existing = result.MaterialPlan ?? new MaterialPlanningResult(
            Array.Empty<MaterialSupplyReservation>(),
            Array.Empty<ScheduledMaterialEvent>(),
            Array.Empty<MaterialBalanceEvent>(),
            Array.Empty<PlanningIssue>());

        var recursiveRawReservations = recursive.CoverageAllocations
            .Where(x => IsMt(x.Uom) && x.InventoryStage == InventoryStage.RawMaterial && x.Quantity > QuantityToleranceMt)
            .Select(x => new MaterialSupplyReservation
            {
                PlanVersionId = result.PlanVersionId,
                ProductionOrderId = recursive.Requirements.First(r => r.Id == x.RequirementId).ProductionOrderId ?? Guid.Empty,
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

        var requirements = (existing.Requirements ?? Array.Empty<MaterialRequirement>())
            .Concat(recursive.Requirements)
            .DistinctBy(x => x.Id)
            .ToArray();
        var issues = existing.Issues
            .Concat(recursive.Issues)
            .Concat(reconciliationIssues)
            .DistinctBy(x => (x.Code, x.Message, x.SourceId))
            .ToArray();
        var reservations = existing.Reservations
            .Concat(recursiveRawReservations)
            .DistinctBy(x => (x.ProductionOrderId, x.MaterialSpecificationCode, x.LocationCode, x.AvailableFromUtc, x.QuantityMt))
            .ToArray();

        return existing with
        {
            Requirements = requirements,
            Reservations = reservations,
            Issues = issues
        };
    }

    private static int Depth(MaterialRequirement requirement) =>
        string.IsNullOrWhiteSpace(requirement.RequirementPath)
            ? 0
            : requirement.RequirementPath.Split(" -> ", StringSplitOptions.RemoveEmptyEntries).Length;

    private static bool IsMt(string? uom) =>
        string.Equals(uom?.Trim(), "MT", StringComparison.OrdinalIgnoreCase);

    private sealed class CampaignAwareMaterialCoverageSession : IMaterialCoverageSession
    {
        private readonly List<CampaignPool> _campaignPools;
        private readonly List<InventoryPool> _rawPools;

        public CampaignAwareMaterialCoverageSession(
            IReadOnlyCollection<PlanningInventoryAllocation> campaignAllocations,
            IReadOnlyCollection<InventoryPosition> inventory)
        {
            _campaignPools = campaignAllocations
                .Where(x => x.Use != PlanningInventoryUse.FinishedGoodsFulfilment && x.QuantityMt > QuantityToleranceMt)
                .Select(x => new CampaignPool(x, x.QuantityMt))
                .ToList();
            _rawPools = inventory
                .Where(x => x.Stage == InventoryStage.RawMaterial)
                .Where(x => x.QualityStatus is MaterialQualityStatus.Available or MaterialQualityStatus.Released)
                .Where(x => x.ProjectedAvailableQuantityMt > QuantityToleranceMt)
                .Select(x => new InventoryPool(x, x.ProjectedAvailableQuantityMt))
                .ToList();
        }

        public MaterialCoverageResult Cover(MaterialCoverageRequest request)
        {
            if (!IsMt(request.Uom) || request.RequiredQuantity <= QuantityToleranceMt)
                return MaterialCoverageResult.None;

            // Existing campaign allocations are PO-specific decisions and are replayed rather than re-netted.
            // If the recursive node carries a qualification that campaign allocations cannot prove, do not assume
            // the source material is qualified; expose the resulting mismatch explicitly instead.
            var allocations = new List<MaterialCoverageAllocation>();
            var remaining = request.RequiredQuantity;
            if (string.IsNullOrWhiteSpace(request.QualificationCode))
            {
                foreach (var pool in _campaignPools
                             .Where(x => x.RemainingQuantityMt > QuantityToleranceMt &&
                                         x.Allocation.ProductionOrderId == request.ProductionOrderId &&
                                         Same(x.Allocation.MaterialCode, request.MaterialCode) &&
                                         OptionalSame(request.GradeCode, x.Allocation.GradeCode) &&
                                         OptionalSame(request.CrossSectionCode, x.Allocation.CrossSectionCode) &&
                                         (!x.Allocation.AvailableFromUtc.HasValue || x.Allocation.AvailableFromUtc.Value <= request.RequiredAtUtc))
                             .OrderBy(x => x.Allocation.AvailableFromUtc ?? DateTime.MinValue)
                             .ThenBy(x => x.Allocation.LocationCode, StringComparer.OrdinalIgnoreCase))
                {
                    if (remaining <= QuantityToleranceMt) break;
                    var quantity = Math.Min(remaining, pool.RemainingQuantityMt);
                    pool.RemainingQuantityMt -= quantity;
                    remaining -= quantity;
                    allocations.Add(new MaterialCoverageAllocation(
                        request.RequirementId,
                        SourceType(pool.Allocation.Use),
                        pool.Allocation.SourceReference,
                        pool.Allocation.MaterialCode,
                        null,
                        pool.Allocation.GradeCode,
                        pool.Allocation.CrossSectionCode,
                        "MT",
                        pool.Allocation.LocationCode,
                        quantity,
                        pool.Allocation.AvailableFromUtc,
                        MaterialQualityStatus.Available,
                        pool.Allocation.Stage));
                }
            }

            // Campaign planning does not consume raw-material pools. Raw inventory is therefore safe to net here.
            if (string.IsNullOrWhiteSpace(request.QualificationCode))
            {
                foreach (var pool in _rawPools
                             .Where(x => x.RemainingQuantityMt > QuantityToleranceMt && MatchesRaw(x.Position, request))
                             .OrderBy(x => x.Position.AvailableFromUtc ?? DateTime.MinValue)
                             .ThenBy(x => x.Position.LocationCode, StringComparer.OrdinalIgnoreCase))
                {
                    if (remaining <= QuantityToleranceMt) break;
                    var quantity = Math.Min(remaining, pool.RemainingQuantityMt);
                    pool.RemainingQuantityMt -= quantity;
                    remaining -= quantity;
                    allocations.Add(new MaterialCoverageAllocation(
                        request.RequirementId,
                        MaterialCoverageSourceType.OpeningInventory,
                        null,
                        pool.Position.MaterialCode,
                        null,
                        pool.Position.GradeCode,
                        pool.Position.CrossSectionCode,
                        "MT",
                        pool.Position.LocationCode,
                        quantity,
                        pool.Position.AvailableFromUtc,
                        pool.Position.QualityStatus,
                        pool.Position.Stage));
                }
            }

            return new MaterialCoverageResult(allocations.Sum(x => x.Quantity), allocations);
        }

        private static MaterialCoverageSourceType SourceType(PlanningInventoryUse use) => use switch
        {
            PlanningInventoryUse.ExternalIntermediateFeed => MaterialCoverageSourceType.KnownIncoming,
            PlanningInventoryUse.PlannedPurchaseFeed => MaterialCoverageSourceType.KnownIncoming,
            PlanningInventoryUse.PlannedTransferFeed => MaterialCoverageSourceType.KnownIncoming,
            PlanningInventoryUse.ManualPlannedFeed => MaterialCoverageSourceType.KnownIncoming,
            PlanningInventoryUse.CommittedInternalProductionFeed => MaterialCoverageSourceType.CommittedInternalProduction,
            _ => MaterialCoverageSourceType.OpeningInventory
        };

        private static bool MatchesRaw(InventoryPosition position, MaterialCoverageRequest request) =>
            Same(position.MaterialCode, request.MaterialCode) &&
            OptionalSame(request.GradeCode, position.GradeCode) &&
            OptionalSame(request.CrossSectionCode, position.CrossSectionCode) &&
            (string.IsNullOrWhiteSpace(request.LocationCode) || Same(position.LocationCode, request.LocationCode)) &&
            (!position.AvailableFromUtc.HasValue || position.AvailableFromUtc.Value <= request.RequiredAtUtc);

        private static bool OptionalSame(string? expected, string? actual) =>
            string.IsNullOrWhiteSpace(expected) || Same(expected, actual);

        private static bool Same(string? left, string? right) =>
            string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

        private sealed class CampaignPool(PlanningInventoryAllocation allocation, decimal remainingQuantityMt)
        {
            public PlanningInventoryAllocation Allocation { get; } = allocation;
            public decimal RemainingQuantityMt { get; set; } = remainingQuantityMt;
        }

        private sealed class InventoryPool(InventoryPosition position, decimal remainingQuantityMt)
        {
            public InventoryPosition Position { get; } = position;
            public decimal RemainingQuantityMt { get; set; } = remainingQuantityMt;
        }
    }
}
