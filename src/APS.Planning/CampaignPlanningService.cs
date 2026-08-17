using APS.Application;
using APS.Domain;

namespace APS.Planning;

public sealed class MtsProductionOrderService : IMtsProductionOrderService
{
    public MtsProductionOrderProposal Propose(StockPolicy policy, InventoryPosition inventory, decimal alreadyFirmedSupplyMt = 0m)
    {
        var projected = inventory.ProjectedAvailableQuantityMt + alreadyFirmedSupplyMt;
        var raw = Math.Max(0m, policy.TargetStockMt - projected);

        if (raw <= 0m)
        {
            return new(null, projected, 0m, "Projected stock already meets or exceeds target stock.");
        }

        var proposed = Math.Max(raw, policy.MinimumReplenishmentMt);
        if (policy.MaximumReplenishmentMt > 0m)
        {
            proposed = Math.Min(proposed, policy.MaximumReplenishmentMt);
        }

        var po = new ProductionOrder
        {
            ProductionOrderNumber = $"MTS-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
            DemandSource = DemandSourceType.MakeToStock,
            MaterialCode = policy.MaterialCode,
            GradeCode = policy.GradeCode,
            GradeSequenceClassCode = policy.GradeSequenceClassCode,
            FinalCrossSectionCode = policy.FinalCrossSectionCode,
            CasterSectionCode = policy.CasterSectionCode,
            RouteCode = policy.RouteCode,
            PlannedQuantityMt = proposed,
            RemainingQuantityMt = proposed,
            RequiredDate = policy.RequiredDate,
            Priority = policy.Priority,
            TargetStockMt = policy.TargetStockMt,
            ProjectedAvailableStockMt = projected,
            StockPolicyCode = policy.PolicyCode
        };

        return new(po, projected, proposed, "APS-generated MTS Production Order required to restore target stock.");
    }
}

public sealed class CampaignPlanningService : ICampaignPlanningService
{
    public CampaignPlanningResult FormCampaigns(CampaignPlanningRequest request)
    {
        ValidatePolicy(request.Policy);
        ResolveGradeMasters(request);

        var coveredByFinishedGoods = new List<ProductionOrder>();
        var rollingRequirements = new Dictionary<Guid, decimal>();
        var freshSteelRequirements = new Dictionary<Guid, decimal>();
        var intermediateAllocated = new Dictionary<Guid, decimal>();
        var externalAllocated = new Dictionary<Guid, decimal>();
        var inventoryAllocations = new List<PlanningInventoryAllocation>();

        var finishedGoodsPools = request.Inventory
            .Where(i => i.Stage == InventoryStage.FinishedGoods &&
                        i.QualityStatus is MaterialQualityStatus.Available or MaterialQualityStatus.Released &&
                        i.ProjectedAvailableQuantityMt > 0m)
            .Select(i => new InventoryPool(i, i.ProjectedAvailableQuantityMt))
            .ToList();

        var intermediatePools = request.Inventory
            .Where(i => i.Stage is InventoryStage.CastIntermediate or InventoryStage.OtherIntermediate)
            .Where(i => i.QualityStatus is MaterialQualityStatus.Available or MaterialQualityStatus.Released)
            .Where(i => i.ProjectedAvailableQuantityMt > 0m)
            .Select(i => new InventoryPool(i, i.ProjectedAvailableQuantityMt))
            .ToList();

        var externalPools = (request.ExternalMaterialSupplies ?? Array.Empty<ExternalMaterialSupply>())
            .Where(x => x.IsFirm &&
                        x.QualityStatus is MaterialQualityStatus.Available or MaterialQualityStatus.Released &&
                        x.QuantityMt - x.ReservedQuantityMt > 0m)
            .Select(x => new ExternalSupplyPool(x, x.QuantityMt - x.ReservedQuantityMt))
            .ToList();

        // MTO is protected before MTS. Within each class, higher priority and earlier requirement date win supply.
        var ordered = request.ProductionOrders
            .Where(x => x.Status is ProductionOrderStatus.Planned or ProductionOrderStatus.Firmed)
            .OrderBy(x => x.DemandSource == DemandSourceType.MakeToOrder ? 0 : 1)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.RequiredDate)
            .ThenBy(x => x.ProductionOrderNumber)
            .ToArray();

        foreach (var po in ordered)
        {
            ValidateOrderRequirementAgainstGrade(po);
            var remaining = Math.Max(0m, po.RemainingQuantityMt);

            var fgUsed = AllocateInventory(
                po,
                remaining,
                finishedGoodsPools,
                position =>
                    Same(position.MaterialCode, po.MaterialCode) &&
                    Same(position.GradeCode, po.GradeCode) &&
                    Same(position.CrossSectionCode, po.FinalCrossSectionCode),
                PlanningInventoryUse.FinishedGoodsFulfilment,
                inventoryAllocations);

            var rollingRequirement = remaining - fgUsed;
            rollingRequirements[po.Id] = rollingRequirement;

            if (rollingRequirement <= 0m)
            {
                coveredByFinishedGoods.Add(po);
                freshSteelRequirements[po.Id] = 0m;
                intermediateAllocated[po.Id] = 0m;
                externalAllocated[po.Id] = 0m;
                continue;
            }

            var intermediateUsed = AllocateInventory(
                po,
                rollingRequirement,
                intermediatePools,
                position =>
                    Same(position.GradeCode, po.GradeCode) &&
                    Same(position.CrossSectionCode, po.CasterSectionCode) &&
                    (!position.AvailableFromUtc.HasValue || position.AvailableFromUtc.Value <= po.RequiredDate),
                PlanningInventoryUse.IntermediateFeed,
                inventoryAllocations);

            var afterInternal = rollingRequirement - intermediateUsed;
            var externalUsed = AllocateExternalSupply(po, afterInternal, externalPools, inventoryAllocations);

            intermediateAllocated[po.Id] = intermediateUsed;
            externalAllocated[po.Id] = externalUsed;
            freshSteelRequirements[po.Id] = Math.Max(0m, afterInternal - externalUsed);
        }

        var campaignInputs = ordered
            .Where(po => rollingRequirements[po.Id] > 0m)
            .GroupBy(po => CampaignKey(po, request.Policy))
            .OrderBy(g => g.Min(x => x.RequiredDate));

        var campaigns = new List<Campaign>();
        var sequence = 1;

        foreach (var group in campaignInputs)
        {
            var groupOrders = group
                .OrderBy(x => x.DemandSource == DemandSourceType.MakeToOrder ? 0 : 1)
                .ThenByDescending(x => x.Priority)
                .ThenBy(x => x.RequiredDate)
                .ThenBy(x => x.GradeCode)
                .ThenBy(x => x.ProductionOrderNumber)
                .ToArray();

            var current = NewCampaign(request.CampaignNumberPrefix, sequence++, group.Key, groupOrders.Min(x => x.RequiredDate));

            foreach (var po in groupOrders)
            {
                var remainingRolling = rollingRequirements[po.Id];
                var remainingIntermediate = intermediateAllocated[po.Id] + externalAllocated[po.Id];
                var remainingFresh = freshSteelRequirements[po.Id];

                while (remainingRolling > 0m)
                {
                    var capacity = request.Policy.MaximumCampaignQuantityMt - current.PlannedQuantityMt;
                    if (capacity <= 0m)
                    {
                        BuildGradeSequenceAndHeats(current, request);
                        campaigns.Add(current);
                        current = NewCampaign(request.CampaignNumberPrefix, sequence++, group.Key, po.RequiredDate);
                        capacity = request.Policy.MaximumCampaignQuantityMt;
                    }

                    var allocationQty = Math.Min(remainingRolling, capacity);
                    var intermediateQty = Math.Min(remainingIntermediate, allocationQty);
                    var freshQty = allocationQty - intermediateQty;

                    freshQty = Math.Min(freshQty, remainingFresh);
                    var accounted = intermediateQty + freshQty;
                    if (accounted < allocationQty)
                    {
                        freshQty += allocationQty - accounted;
                    }

                    current.Allocations.Add(new CampaignAllocation
                    {
                        CampaignId = current.Id,
                        Campaign = current,
                        ProductionOrderId = po.Id,
                        ProductionOrder = po,
                        PlannedQuantityMt = allocationQty,
                        ExistingIntermediateInventoryMt = intermediateQty,
                        FreshSteelQuantityMt = freshQty
                    });

                    current.PlannedQuantityMt += allocationQty;
                    current.ExistingIntermediateInventoryMt += intermediateQty;
                    current.FreshSteelRequirementMt += freshQty;
                    current.RequiredDate = current.RequiredDate <= po.RequiredDate ? current.RequiredDate : po.RequiredDate;

                    remainingRolling -= allocationQty;
                    remainingIntermediate = Math.Max(0m, remainingIntermediate - intermediateQty);
                    remainingFresh = Math.Max(0m, remainingFresh - freshQty);
                }
            }

            if (current.PlannedQuantityMt > 0m)
            {
                BuildGradeSequenceAndHeats(current, request);
                campaigns.Add(current);
            }
        }

        return new CampaignPlanningResult(
            campaigns,
            coveredByFinishedGoods,
            rollingRequirements,
            freshSteelRequirements,
            intermediateAllocated,
            inventoryAllocations,
            externalAllocated);
    }

    private static void ResolveGradeMasters(CampaignPlanningRequest request)
    {
        var gradeByCode = (request.SteelGrades ?? Array.Empty<SteelGrade>())
            .ToDictionary(x => x.GradeCode, StringComparer.OrdinalIgnoreCase);

        foreach (var po in request.ProductionOrders)
        {
            if (po.SteelGrade is null && gradeByCode.TryGetValue(po.GradeCode, out var grade))
            {
                po.SteelGrade = grade;
                po.SteelGradeId = grade.Id;
            }

            if (po.SteelGrade is null) continue;
            po.GradeFamilyCode ??= po.SteelGrade.GradeFamilyCode;
            po.GradeSequenceClassCode ??= po.SteelGrade.SequenceClassCode;
        }
    }

    private static void ValidateOrderRequirementAgainstGrade(ProductionOrder po)
    {
        if (po.SteelGrade is null || po.Requirement is null) return;

        var gradeVd = po.SteelGrade.ProcessRequirements
            .FirstOrDefault(x => x.ProcessOperationType == ProcessOperationType.Vd)?.Requirement;

        if (gradeVd == RequirementDisposition.Forbidden && po.Requirement.RequireVd == true)
        {
            throw new InvalidOperationException($"Production Order {po.ProductionOrderNumber} requires VD but grade {po.GradeCode} forbids VD.");
        }

        if (gradeVd == RequirementDisposition.Required && po.Requirement.ForbidVd == true)
        {
            throw new InvalidOperationException($"Production Order {po.ProductionOrderNumber} forbids VD but grade {po.GradeCode} requires VD.");
        }

        if (!string.IsNullOrWhiteSpace(po.Requirement.RequiredRouteCode) &&
            !Same(po.Requirement.RequiredRouteCode, po.RouteCode))
        {
            throw new InvalidOperationException($"Production Order {po.ProductionOrderNumber} requires route {po.Requirement.RequiredRouteCode} but is assigned route {po.RouteCode}.");
        }
    }

    private static decimal AllocateInventory(
        ProductionOrder productionOrder,
        decimal requiredQuantityMt,
        IEnumerable<InventoryPool> pools,
        Func<InventoryPosition, bool> matches,
        PlanningInventoryUse use,
        ICollection<PlanningInventoryAllocation> allocations)
    {
        var allocated = 0m;
        foreach (var pool in pools
                     .Where(pool => pool.RemainingQuantityMt > 0m && matches(pool.Position))
                     .OrderBy(pool => pool.Position.AvailableFromUtc)
                     .ThenBy(pool => pool.Position.LocationCode)
                     .ThenBy(pool => pool.Position.MaterialCode))
        {
            var stillRequired = requiredQuantityMt - allocated;
            if (stillRequired <= 0m) break;

            var quantity = Math.Min(stillRequired, pool.RemainingQuantityMt);
            if (quantity <= 0m) continue;

            pool.RemainingQuantityMt -= quantity;
            allocated += quantity;
            allocations.Add(new PlanningInventoryAllocation(
                productionOrder.Id,
                pool.Position.Stage,
                pool.Position.MaterialCode,
                pool.Position.GradeCode,
                pool.Position.CrossSectionCode,
                pool.Position.LocationCode,
                quantity,
                use,
                null,
                pool.Position.AvailableFromUtc));
        }

        return allocated;
    }

    private static decimal AllocateExternalSupply(
        ProductionOrder po,
        decimal requiredQuantityMt,
        IEnumerable<ExternalSupplyPool> pools,
        ICollection<PlanningInventoryAllocation> allocations)
    {
        var allocated = 0m;
        foreach (var pool in pools
                     .Where(x => x.RemainingQuantityMt > 0m &&
                                 x.Supply.AvailableFromUtc <= po.RequiredDate &&
                                 Same(x.Supply.GradeCode, po.GradeCode) &&
                                 Same(x.Supply.CrossSectionCode, po.CasterSectionCode))
                     .OrderBy(x => x.Supply.UsagePenalty)
                     .ThenBy(x => x.Supply.AvailableFromUtc)
                     .ThenBy(x => x.Supply.SupplyReference))
        {
            var remaining = requiredQuantityMt - allocated;
            if (remaining <= 0m) break;
            var quantity = Math.Min(remaining, pool.RemainingQuantityMt);
            if (quantity <= 0m) continue;

            pool.RemainingQuantityMt -= quantity;
            allocated += quantity;
            allocations.Add(new PlanningInventoryAllocation(
                po.Id,
                InventoryStage.InTransit,
                pool.Supply.MaterialSpecificationCode ?? "EXTERNAL-BILLET",
                pool.Supply.GradeCode,
                pool.Supply.CrossSectionCode,
                pool.Supply.LocationCode,
                quantity,
                PlanningInventoryUse.ExternalIntermediateFeed,
                pool.Supply.SupplyReference,
                pool.Supply.AvailableFromUtc));
        }

        return allocated;
    }

    private static CampaignCompatibilityKey CampaignKey(ProductionOrder po, CampaignPlanningPolicy policy)
    {
        var sequenceClass = SequenceClass(po);
        var gradePartition = policy.AllowMixedGradesWithinSequenceClass ? "*" : po.GradeCode;
        var demandPartition = policy.AllowMtoMtsMixing ? "*" : po.DemandSource.ToString();
        return new(
            sequenceClass,
            po.CasterSectionCode,
            po.RouteCode,
            gradePartition,
            demandPartition,
            CampaignSegregationPartition(po));
    }

    private static string CampaignSegregationPartition(ProductionOrder po)
    {
        var requirement = po.Requirement;
        if (requirement is null) return "*";

        return requirement.SegregationPolicy switch
        {
            SegregationPolicy.DedicatedCampaign => $"PO:{po.Id:N}",
            SegregationPolicy.SameSalesOrderOnly => $"SO:{po.SalesOrderId?.ToString("N") ?? po.Id.ToString("N")}",
            SegregationPolicy.SameCustomerOnly => $"CUSTOMER:{requirement.CustomerCode ?? po.SalesOrder?.CustomerCode ?? "UNKNOWN"}",
            _ => "*"
        };
    }

    private static Campaign NewCampaign(string prefix, int sequence, CampaignCompatibilityKey key, DateTime requiredDate) =>
        new()
        {
            CampaignNumber = $"{prefix}-{sequence:00000}",
            GradeSequenceClassCode = key.GradeSequenceClassCode,
            CasterSectionCode = key.CasterSectionCode,
            RouteCode = key.RouteCode,
            PlannedQuantityMt = 0m,
            FreshSteelRequirementMt = 0m,
            ExistingIntermediateInventoryMt = 0m,
            RequiredDate = requiredDate,
            Status = CampaignStatus.Draft
        };

    private static void BuildGradeSequenceAndHeats(Campaign campaign, CampaignPlanningRequest request)
    {
        campaign.GradeSequence.Clear();
        campaign.Heats.Clear();

        var allocations = campaign.Allocations.ToList();
        var heatGroups = allocations
            .Where(a => a.ProductionOrder is not null && a.FreshSteelQuantityMt > 0m)
            .GroupBy(a => new HeatCompatibilityKey(
                a.ProductionOrder!.GradeCode,
                HeatRequirementSignature(a.ProductionOrder)))
            .Select(g => new
            {
                Key = g.Key,
                RequiredOutputQuantityMt = g.Sum(x => x.FreshSteelQuantityMt),
                ProductionOrders = g.Select(x => x.ProductionOrder!).DistinctBy(x => x.Id).ToArray(),
                FirstIndex = allocations.FindIndex(x => ReferenceEquals(x, g.First()))
            })
            .OrderBy(x => x.FirstIndex)
            .ToArray();

        var gradeSequenceNo = 1;
        var heatSequenceNo = 1;

        foreach (var group in heatGroups)
        {
            var grade = group.ProductionOrders.Select(x => x.SteelGrade).FirstOrDefault(x => x is not null);
            var yieldPct = grade?.ProcessRequirements
                               .FirstOrDefault(x => x.ProcessOperationType == ProcessOperationType.Ccm)
                               ?.ExpectedYieldPct
                           ?? request.Policy.ExpectedCastingYieldPct;
            if (yieldPct <= 0m || yieldPct > 100m)
            {
                throw new InvalidOperationException($"Grade {group.Key.GradeCode} has invalid casting yield {yieldPct}.");
            }

            var plannedInputQuantity = decimal.Round(
                group.RequiredOutputQuantityMt / (yieldPct / 100m),
                4,
                MidpointRounding.AwayFromZero);

            var gradeSequence = new CampaignGradeSequence
            {
                CampaignId = campaign.Id,
                Campaign = campaign,
                SequenceNumber = gradeSequenceNo++,
                GradeCode = group.Key.GradeCode,
                PlannedQuantityMt = plannedInputQuantity
            };
            campaign.GradeSequence.Add(gradeSequence);

            var heatPlans = BuildFurnaceFeasibleHeatPlan(
                plannedInputQuantity,
                group.ProductionOrders,
                request);

            foreach (var heatPlan in heatPlans)
            {
                campaign.Heats.Add(new CampaignHeat
                {
                    CampaignId = campaign.Id,
                    Campaign = campaign,
                    CampaignGradeSequenceId = gradeSequence.Id,
                    CampaignGradeSequence = gradeSequence,
                    SequenceNumber = heatSequenceNo++,
                    GradeCode = group.Key.GradeCode,
                    PlannedQuantityMt = heatPlan.QuantityMt,
                    MinimumFeasibleQuantityMt = heatPlan.MinimumMt,
                    TargetQuantityMt = heatPlan.TargetMt,
                    MaximumFeasibleQuantityMt = heatPlan.MaximumMt
                });
            }
        }
    }

    private static IReadOnlyList<HeatQuantityPlan> BuildFurnaceFeasibleHeatPlan(
        decimal totalQuantityMt,
        IReadOnlyCollection<ProductionOrder> productionOrders,
        CampaignPlanningRequest request)
    {
        if (totalQuantityMt <= 0m) return Array.Empty<HeatQuantityPlan>();

        var envelopes = BuildFurnaceEnvelopes(productionOrders, request);
        if (envelopes.Count == 0)
        {
            // Standalone/legacy calls may not supply plant masters. Integrated planning must.
            if (request.Resources is { Count: > 0 })
            {
                throw new InvalidOperationException($"No eligible EAF heat-capacity envelope exists for {productionOrders.First().GradeCode} on route {productionOrders.First().RouteCode}.");
            }

            return DistributeLegacyHeatQuantities(totalQuantityMt, request.Policy)
                .Select(x => new HeatQuantityPlan(x, request.Policy.MinimumHeatSizeMt, request.Policy.NominalHeatSizeMt, request.Policy.MaximumHeatSizeMt))
                .ToArray();
        }

        var globalMin = envelopes.Min(x => x.MinimumMt);
        var globalMax = envelopes.Max(x => x.MaximumMt);
        var minimumCount = Math.Max(1, (int)Math.Ceiling(totalQuantityMt / globalMax));
        var maximumCount = Math.Max(minimumCount, (int)Math.Floor(totalQuantityMt / globalMin));
        HeatPlanCandidate? best = null;

        for (var heatCount = minimumCount; heatCount <= maximumCount; heatCount++)
        {
            foreach (var counts in EnumerateEnvelopeCounts(envelopes.Count, heatCount))
            {
                var minimumTotal = counts.Select((count, index) => count * envelopes[index].MinimumMt).Sum();
                var maximumTotal = counts.Select((count, index) => count * envelopes[index].MaximumMt).Sum();
                if (totalQuantityMt < minimumTotal || totalQuantityMt > maximumTotal) continue;

                var items = new List<MutableHeatPlan>();
                for (var envelopeIndex = 0; envelopeIndex < envelopes.Count; envelopeIndex++)
                {
                    for (var i = 0; i < counts[envelopeIndex]; i++)
                    {
                        var envelope = envelopes[envelopeIndex];
                        items.Add(new MutableHeatPlan(envelope, Math.Clamp(envelope.TargetMt, envelope.MinimumMt, envelope.MaximumMt)));
                    }
                }

                var delta = totalQuantityMt - items.Sum(x => x.QuantityMt);
                if (delta > 0m)
                {
                    foreach (var item in items.OrderByDescending(x => x.Envelope.MaximumMt - x.QuantityMt))
                    {
                        if (delta <= 0m) break;
                        var add = Math.Min(delta, item.Envelope.MaximumMt - item.QuantityMt);
                        item.QuantityMt += add;
                        delta -= add;
                    }
                }
                else if (delta < 0m)
                {
                    var reduce = -delta;
                    foreach (var item in items.OrderByDescending(x => x.QuantityMt - x.Envelope.MinimumMt))
                    {
                        if (reduce <= 0m) break;
                        var take = Math.Min(reduce, item.QuantityMt - item.Envelope.MinimumMt);
                        item.QuantityMt -= take;
                        reduce -= take;
                    }
                    delta = -reduce;
                }

                if (Math.Abs(delta) > 0.0001m) continue;
                var score = items.Sum(x => Math.Abs(x.QuantityMt - x.Envelope.TargetMt));
                var candidate = new HeatPlanCandidate(
                    items.Select(x => new HeatQuantityPlan(
                            decimal.Round(x.QuantityMt, 4, MidpointRounding.AwayFromZero),
                            x.Envelope.MinimumMt,
                            x.Envelope.TargetMt,
                            x.Envelope.MaximumMt))
                        .ToArray(),
                    score);
                if (best is null || candidate.Score < best.Score) best = candidate;
            }
        }

        return best?.Heats
               ?? throw new InvalidOperationException(
                   $"Fresh steel requirement {totalQuantityMt:0.####} MT for grade {productionOrders.First().GradeCode} cannot be split into furnace-feasible heats with the configured EAF capacities.");
    }

    private static IReadOnlyList<FurnaceEnvelope> BuildFurnaceEnvelopes(
        IReadOnlyCollection<ProductionOrder> productionOrders,
        CampaignPlanningRequest request)
    {
        if (request.Resources is null || request.Resources.Count == 0) return Array.Empty<FurnaceEnvelope>();

        var explicitEafExists = request.Resources.Any(x => x.ProcessUnitType == ProcessUnitType.Eaf);
        var resources = request.Resources.Where(x =>
            x.IsActive &&
            x.OperatingState is not ResourceOperatingState.Breakdown and not ResourceOperatingState.Disabled &&
            (x.ProcessUnitType == ProcessUnitType.Eaf || (!explicitEafExists && x.ResourceType == ResourceType.Furnace)));
        var capabilities = request.ResourceCapabilities ?? Array.Empty<ResourceCapability>();
        var representative = productionOrders.First();
        var grade = representative.SteelGrade;
        var gradeRequirement = grade?.ProcessRequirements.FirstOrDefault(x => x.ProcessOperationType == ProcessOperationType.Eaf);
        var requiredResourceIds = productionOrders
            .Select(x => x.Requirement?.RequiredResourceId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        if (requiredResourceIds.Length > 1)
        {
            throw new InvalidOperationException($"Orders grouped into one heat require different physical resources for grade {representative.GradeCode}.");
        }

        var result = new List<FurnaceEnvelope>();
        foreach (var resource in resources)
        {
            if (requiredResourceIds.Length == 1 && resource.Id != requiredResourceIds[0]) continue;
            if (!resource.MinimumHeatWeightMt.HasValue ||
                !resource.NominalHeatWeightMt.HasValue ||
                !resource.MaximumHeatWeightMt.HasValue)
            {
                throw new InvalidOperationException($"EAF resource {resource.Code} is missing Minimum/Nominal/Maximum heat-weight master data.");
            }

            var matchingCapabilities = capabilities.Where(c =>
                    c.ResourceId == resource.Id &&
                    (!c.ProcessOperationType.HasValue || c.ProcessOperationType == ProcessOperationType.Eaf) &&
                    Matches(c.RouteCode, representative.RouteCode) &&
                    Matches(c.GradeCode, representative.GradeCode) &&
                    Matches(c.GradeFamilyCode, representative.GradeFamilyCode))
                .ToArray();
            if (capabilities.Any(c => c.ResourceId == resource.Id) && matchingCapabilities.Length == 0) continue;

            var minimum = resource.MinimumHeatWeightMt.Value;
            var target = resource.NominalHeatWeightMt.Value;
            var maximum = resource.MaximumHeatWeightMt.Value * Math.Clamp(resource.CapacityFactorPct, 0m, 100m) / 100m;

            if (gradeRequirement?.MinimumHeatWeightMt is { } gradeMin) minimum = Math.Max(minimum, gradeMin);
            if (gradeRequirement?.TargetHeatWeightMt is { } gradeTarget) target = gradeTarget;
            if (gradeRequirement?.MaximumHeatWeightMt is { } gradeMax) maximum = Math.Min(maximum, gradeMax);

            var capMinimum = matchingCapabilities.Where(x => x.MinimumQuantityMt.HasValue).Select(x => x.MinimumQuantityMt!.Value).DefaultIfEmpty(minimum).Max();
            var capMaximum = matchingCapabilities.Where(x => x.MaximumQuantityMt.HasValue).Select(x => x.MaximumQuantityMt!.Value).DefaultIfEmpty(maximum).Min();
            minimum = Math.Max(minimum, capMinimum);
            maximum = Math.Min(maximum, capMaximum);
            target = Math.Clamp(target, minimum, maximum);

            if (minimum <= 0m || maximum < minimum) continue;
            result.Add(new FurnaceEnvelope(resource.Id, minimum, target, maximum));
        }

        return result;
    }

    private static IEnumerable<int[]> EnumerateEnvelopeCounts(int envelopeCount, int totalCount)
    {
        var current = new int[envelopeCount];
        foreach (var result in Enumerate(0, totalCount)) yield return result;

        IEnumerable<int[]> Enumerate(int index, int remaining)
        {
            if (index == envelopeCount - 1)
            {
                current[index] = remaining;
                yield return (int[])current.Clone();
                yield break;
            }

            for (var count = 0; count <= remaining; count++)
            {
                current[index] = count;
                foreach (var result in Enumerate(index + 1, remaining - count)) yield return result;
            }
        }
    }

    private static IReadOnlyList<decimal> DistributeLegacyHeatQuantities(decimal totalQuantityMt, CampaignPlanningPolicy policy)
    {
        if (totalQuantityMt <= 0m) return Array.Empty<decimal>();
        var preferredCount = Math.Max(1, (int)Math.Round(totalQuantityMt / policy.NominalHeatSizeMt, MidpointRounding.AwayFromZero));
        var minimumCount = Math.Max(1, (int)Math.Ceiling(totalQuantityMt / policy.MaximumHeatSizeMt));
        var maximumCount = totalQuantityMt >= policy.MinimumHeatSizeMt
            ? Math.Max(1, (int)Math.Floor(totalQuantityMt / policy.MinimumHeatSizeMt))
            : 1;
        var heatCount = Math.Clamp(preferredCount, minimumCount, maximumCount);
        var average = decimal.Round(totalQuantityMt / heatCount, 4, MidpointRounding.AwayFromZero);
        var result = new List<decimal>(heatCount);
        var allocated = 0m;
        for (var i = 0; i < heatCount; i++)
        {
            var quantity = i == heatCount - 1 ? totalQuantityMt - allocated : average;
            result.Add(quantity);
            allocated += quantity;
        }
        return result;
    }

    private static string HeatRequirementSignature(ProductionOrder po)
    {
        var requirement = po.Requirement;
        if (requirement is null) return "*";

        var chemistry = string.Join(';', requirement.ChemistryOverrides
            .OrderBy(x => x.ElementCode, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.ElementCode}:{x.MinimumPct}:{x.TargetPct}:{x.MaximumPct}"));
        var processes = string.Join(';', requirement.ProcessOverrides
            .OrderBy(x => x.ProcessOperationType)
            .ThenBy(x => x.RequiredResourceId)
            .Select(x => $"{x.ProcessOperationType}:{x.Requirement}:{x.CapabilityClassCode}:{x.RequiredResourceId}:{x.MaximumQueueMinutes}"));

        return string.Join('|',
            requirement.QualityClassCode ?? "",
            requirement.SegregationPolicy,
            requirement.RequireVd,
            requirement.ForbidVd,
            requirement.RequireReheating,
            requirement.ForbidHotCharge,
            requirement.RequireTmt,
            requirement.RequiredRouteCode ?? "",
            requirement.RequiredResourceId,
            requirement.MinimumSuperheatC,
            requirement.TargetSuperheatC,
            requirement.MaximumSuperheatC,
            chemistry,
            processes);
    }

    private static void ValidatePolicy(CampaignPlanningPolicy policy)
    {
        if (policy.NominalHeatSizeMt <= 0m) throw new ArgumentOutOfRangeException(nameof(policy.NominalHeatSizeMt));
        if (policy.MinimumHeatSizeMt <= 0m || policy.MinimumHeatSizeMt > policy.NominalHeatSizeMt)
            throw new ArgumentOutOfRangeException(nameof(policy.MinimumHeatSizeMt));
        if (policy.MaximumHeatSizeMt < policy.NominalHeatSizeMt)
            throw new ArgumentOutOfRangeException(nameof(policy.MaximumHeatSizeMt));
        if (policy.MaximumCampaignQuantityMt < policy.MaximumHeatSizeMt)
            throw new ArgumentOutOfRangeException(nameof(policy.MaximumCampaignQuantityMt));
        if (policy.ExpectedCastingYieldPct <= 0m || policy.ExpectedCastingYieldPct > 100m)
            throw new ArgumentOutOfRangeException(nameof(policy.ExpectedCastingYieldPct));
    }

    private static string SequenceClass(ProductionOrder po) =>
        po.SteelGrade?.SequenceClassCode
        ?? (string.IsNullOrWhiteSpace(po.GradeSequenceClassCode) ? $"GRADE:{po.GradeCode}" : po.GradeSequenceClassCode);

    private static bool Matches(string? configured, string? actual) =>
        string.IsNullOrWhiteSpace(configured) || string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private sealed class InventoryPool(InventoryPosition position, decimal remainingQuantityMt)
    {
        public InventoryPosition Position { get; } = position;
        public decimal RemainingQuantityMt { get; set; } = remainingQuantityMt;
    }

    private sealed class ExternalSupplyPool(ExternalMaterialSupply supply, decimal remainingQuantityMt)
    {
        public ExternalMaterialSupply Supply { get; } = supply;
        public decimal RemainingQuantityMt { get; set; } = remainingQuantityMt;
    }

    private sealed record CampaignCompatibilityKey(
        string GradeSequenceClassCode,
        string CasterSectionCode,
        string RouteCode,
        string GradePartition,
        string DemandPartition,
        string SegregationPartition);

    private sealed record HeatCompatibilityKey(string GradeCode, string RequirementSignature);
    private sealed record FurnaceEnvelope(Guid ResourceId, decimal MinimumMt, decimal TargetMt, decimal MaximumMt);
    private sealed record HeatQuantityPlan(decimal QuantityMt, decimal MinimumMt, decimal TargetMt, decimal MaximumMt);
    private sealed record HeatPlanCandidate(IReadOnlyList<HeatQuantityPlan> Heats, decimal Score);

    private sealed class MutableHeatPlan(FurnaceEnvelope envelope, decimal quantityMt)
    {
        public FurnaceEnvelope Envelope { get; } = envelope;
        public decimal QuantityMt { get; set; } = quantityMt;
    }
}
