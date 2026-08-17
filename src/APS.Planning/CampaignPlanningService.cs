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

        var coveredByFinishedGoods = new List<ProductionOrder>();
        var rollingRequirements = new Dictionary<Guid, decimal>();
        var freshSteelRequirements = new Dictionary<Guid, decimal>();
        var intermediateAllocated = new Dictionary<Guid, decimal>();
        var inventoryAllocations = new List<PlanningInventoryAllocation>();

        var finishedGoodsPools = request.Inventory
            .Where(i => i.Stage == InventoryStage.FinishedGoods && i.ProjectedAvailableQuantityMt > 0m)
            .Select(i => new InventoryPool(i, i.ProjectedAvailableQuantityMt))
            .ToList();

        var intermediatePools = request.Inventory
            .Where(i => i.Stage is InventoryStage.CastIntermediate or InventoryStage.OtherIntermediate)
            .Where(i => i.ProjectedAvailableQuantityMt > 0m)
            .Select(i => new InventoryPool(i, i.ProjectedAvailableQuantityMt))
            .ToList();

        // MTO is protected before MTS. Within each class, higher priority and earlier requirement date win inventory.
        var ordered = request.ProductionOrders
            .Where(x => x.Status is ProductionOrderStatus.Planned or ProductionOrderStatus.Firmed)
            .OrderBy(x => x.DemandSource == DemandSourceType.MakeToOrder ? 0 : 1)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.RequiredDate)
            .ThenBy(x => x.ProductionOrderNumber)
            .ToArray();

        foreach (var po in ordered)
        {
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
                continue;
            }

            // Existing compatible cast/intermediate stock can feed rolling without creating new heats.
            var intermediateUsed = AllocateInventory(
                po,
                rollingRequirement,
                intermediatePools,
                position =>
                    Same(position.GradeCode, po.GradeCode) &&
                    Same(position.CrossSectionCode, po.CasterSectionCode),
                PlanningInventoryUse.IntermediateFeed,
                inventoryAllocations);

            intermediateAllocated[po.Id] = intermediateUsed;
            freshSteelRequirements[po.Id] = rollingRequirement - intermediateUsed;
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
                var remainingIntermediate = intermediateAllocated[po.Id];
                var remainingFresh = freshSteelRequirements[po.Id];

                while (remainingRolling > 0m)
                {
                    var capacity = request.Policy.MaximumCampaignQuantityMt - current.PlannedQuantityMt;
                    if (capacity <= 0m)
                    {
                        BuildGradeSequenceAndHeats(current, request.Policy);
                        campaigns.Add(current);
                        current = NewCampaign(request.CampaignNumberPrefix, sequence++, group.Key, po.RequiredDate);
                        capacity = request.Policy.MaximumCampaignQuantityMt;
                    }

                    var allocationQty = Math.Min(remainingRolling, capacity);
                    var intermediateQty = Math.Min(remainingIntermediate, allocationQty);
                    var freshQty = allocationQty - intermediateQty;

                    // Fresh quantity is the residual after inventory; keep accounting defensive against rounding.
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
                BuildGradeSequenceAndHeats(current, request.Policy);
                campaigns.Add(current);
            }
        }

        return new CampaignPlanningResult(
            campaigns,
            coveredByFinishedGoods,
            rollingRequirements,
            freshSteelRequirements,
            intermediateAllocated,
            inventoryAllocations);
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
                     .OrderBy(pool => pool.Position.LocationCode)
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
                use));
        }

        return allocated;
    }

    private static CampaignCompatibilityKey CampaignKey(ProductionOrder po, CampaignPlanningPolicy policy)
    {
        var sequenceClass = SequenceClass(po);
        var gradePartition = policy.AllowMixedGradesWithinSequenceClass ? "*" : po.GradeCode;
        var demandPartition = policy.AllowMtoMtsMixing ? "*" : po.DemandSource.ToString();
        return new(sequenceClass, po.CasterSectionCode, po.RouteCode, gradePartition, demandPartition);
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

    private static void BuildGradeSequenceAndHeats(Campaign campaign, CampaignPlanningPolicy policy)
    {
        campaign.GradeSequence.Clear();
        campaign.Heats.Clear();

        var allocations = campaign.Allocations.ToList();
        var gradeGroups = allocations
            .Where(a => a.ProductionOrder is not null && a.FreshSteelQuantityMt > 0m)
            .GroupBy(a => a.ProductionOrder!.GradeCode)
            .Select(g => new
            {
                GradeCode = g.Key,
                QuantityMt = g.Sum(x => x.FreshSteelQuantityMt),
                FirstIndex = allocations.FindIndex(x => ReferenceEquals(x, g.First()))
            })
            .OrderBy(x => x.FirstIndex)
            .ToArray();

        var gradeSequenceNo = 1;
        var heatSequenceNo = 1;

        foreach (var grade in gradeGroups)
        {
            var gradeSequence = new CampaignGradeSequence
            {
                CampaignId = campaign.Id,
                Campaign = campaign,
                SequenceNumber = gradeSequenceNo++,
                GradeCode = grade.GradeCode,
                PlannedQuantityMt = grade.QuantityMt
            };
            campaign.GradeSequence.Add(gradeSequence);

            foreach (var heatQuantity in DistributeHeatQuantities(grade.QuantityMt, policy))
            {
                campaign.Heats.Add(new CampaignHeat
                {
                    CampaignId = campaign.Id,
                    Campaign = campaign,
                    CampaignGradeSequenceId = gradeSequence.Id,
                    CampaignGradeSequence = gradeSequence,
                    SequenceNumber = heatSequenceNo++,
                    GradeCode = grade.GradeCode,
                    PlannedQuantityMt = heatQuantity
                });
            }
        }
    }

    private static IReadOnlyList<decimal> DistributeHeatQuantities(decimal totalQuantityMt, CampaignPlanningPolicy policy)
    {
        if (totalQuantityMt <= 0m) return Array.Empty<decimal>();

        var preferredCount = Math.Max(1, (int)Math.Round(
            totalQuantityMt / policy.NominalHeatSizeMt,
            MidpointRounding.AwayFromZero));
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

    private static void ValidatePolicy(CampaignPlanningPolicy policy)
    {
        if (policy.NominalHeatSizeMt <= 0m) throw new ArgumentOutOfRangeException(nameof(policy.NominalHeatSizeMt));
        if (policy.MinimumHeatSizeMt <= 0m || policy.MinimumHeatSizeMt > policy.NominalHeatSizeMt)
            throw new ArgumentOutOfRangeException(nameof(policy.MinimumHeatSizeMt));
        if (policy.MaximumHeatSizeMt < policy.NominalHeatSizeMt)
            throw new ArgumentOutOfRangeException(nameof(policy.MaximumHeatSizeMt));
        if (policy.MaximumCampaignQuantityMt < policy.MaximumHeatSizeMt)
            throw new ArgumentOutOfRangeException(nameof(policy.MaximumCampaignQuantityMt));
    }

    private static string SequenceClass(ProductionOrder po) =>
        string.IsNullOrWhiteSpace(po.GradeSequenceClassCode) ? $"GRADE:{po.GradeCode}" : po.GradeSequenceClassCode;

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private sealed class InventoryPool(InventoryPosition position, decimal remainingQuantityMt)
    {
        public InventoryPosition Position { get; } = position;
        public decimal RemainingQuantityMt { get; set; } = remainingQuantityMt;
    }

    private sealed record CampaignCompatibilityKey(
        string GradeSequenceClassCode,
        string CasterSectionCode,
        string RouteCode,
        string GradePartition,
        string DemandPartition);
}
