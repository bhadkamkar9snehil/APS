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

        return new(po, projected, proposed, "APS-generated MTS production order required to restore target stock.");
    }
}

public sealed class CampaignPlanningService : ICampaignPlanningService
{
    public CampaignPlanningResult FormCampaigns(CampaignPlanningRequest request)
    {
        ValidatePolicy(request.Policy);

        var covered = new List<ProductionOrder>();
        var netRequirements = new Dictionary<Guid, decimal>();
        var inventoryPools = request.Inventory
            .GroupBy(i => InventoryKey(i.MaterialCode, i.GradeCode, i.CrossSectionCode))
            .ToDictionary(g => g.Key, g => Math.Max(0m, g.Sum(x => x.ProjectedAvailableQuantityMt)));

        // Finished-goods inventory is netted against PO demand before campaign formation.
        // MTO receives precedence over MTS, then earlier required date and higher priority.
        var ordered = request.ProductionOrders
            .Where(x => x.Status is ProductionOrderStatus.Planned or ProductionOrderStatus.Firmed)
            .OrderBy(x => x.DemandSource == DemandSourceType.MakeToOrder ? 0 : 1)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.RequiredDate)
            .ThenBy(x => x.ProductionOrderNumber)
            .ToArray();

        foreach (var po in ordered)
        {
            var requirement = Math.Max(0m, po.RemainingQuantityMt);
            var key = InventoryKey(po.MaterialCode, po.GradeCode, po.FinalCrossSectionCode);
            inventoryPools.TryGetValue(key, out var available);
            var consumed = Math.Min(requirement, available);
            available -= consumed;
            requirement -= consumed;
            inventoryPools[key] = available;
            netRequirements[po.Id] = requirement;

            if (requirement <= 0m)
            {
                covered.Add(po);
            }
        }

        var campaignInputs = ordered
            .Where(po => netRequirements[po.Id] > 0m)
            .GroupBy(po => new CampaignCompatibilityKey(po.GradeCode, po.CasterSectionCode, po.RouteCode))
            .OrderBy(g => g.Min(x => x.RequiredDate));

        var campaigns = new List<Campaign>();
        var sequence = 1;

        foreach (var group in campaignInputs)
        {
            var groupOrders = group
                .OrderBy(x => x.DemandSource == DemandSourceType.MakeToOrder ? 0 : 1)
                .ThenByDescending(x => x.Priority)
                .ThenBy(x => x.RequiredDate)
                .ToArray();

            var current = NewCampaign(request.CampaignNumberPrefix, sequence++, group.Key, groupOrders.Min(x => x.RequiredDate));

            foreach (var po in groupOrders)
            {
                var remaining = netRequirements[po.Id];
                while (remaining > 0m)
                {
                    var capacity = request.Policy.MaximumCampaignQuantityMt - current.PlannedQuantityMt;
                    if (capacity <= 0m)
                    {
                        FinalizeHeatStructure(current, request.Policy);
                        campaigns.Add(current);
                        current = NewCampaign(request.CampaignNumberPrefix, sequence++, group.Key, po.RequiredDate);
                        capacity = request.Policy.MaximumCampaignQuantityMt;
                    }

                    var allocationQty = Math.Min(remaining, capacity);
                    current.Allocations.Add(new CampaignAllocation
                    {
                        CampaignId = current.Id,
                        Campaign = current,
                        ProductionOrderId = po.Id,
                        ProductionOrder = po,
                        PlannedQuantityMt = allocationQty
                    });
                    current.PlannedQuantityMt += allocationQty;
                    current.RequiredDate = current.RequiredDate <= po.RequiredDate ? current.RequiredDate : po.RequiredDate;
                    remaining -= allocationQty;
                }
            }

            if (current.PlannedQuantityMt > 0m)
            {
                FinalizeHeatStructure(current, request.Policy);
                campaigns.Add(current);
            }
        }

        return new(campaigns, covered, netRequirements);
    }

    private static Campaign NewCampaign(string prefix, int sequence, CampaignCompatibilityKey key, DateTime requiredDate) =>
        new()
        {
            CampaignNumber = $"{prefix}-{sequence:00000}",
            GradeCode = key.GradeCode,
            CasterSectionCode = key.CasterSectionCode,
            RouteCode = key.RouteCode,
            PlannedQuantityMt = 0m,
            RequiredDate = requiredDate,
            Status = CampaignStatus.Draft
        };

    private static void FinalizeHeatStructure(Campaign campaign, CampaignPlanningPolicy policy)
    {
        var remaining = campaign.PlannedQuantityMt;
        var heatNo = 1;

        while (remaining > 0m)
        {
            var quantity = Math.Min(policy.NominalHeatSizeMt, remaining);

            if (remaining > policy.MaximumHeatSizeMt)
            {
                quantity = Math.Min(policy.NominalHeatSizeMt, policy.MaximumHeatSizeMt);
            }
            else if (remaining < policy.MinimumHeatSizeMt && campaign.Heats.Count > 0)
            {
                var previous = campaign.Heats.Last();
                var room = policy.MaximumHeatSizeMt - previous.PlannedQuantityMt;
                var moved = Math.Min(room, remaining);
                previous.PlannedQuantityMt += moved;
                remaining -= moved;
                if (remaining <= 0m) break;
                quantity = remaining;
            }

            campaign.Heats.Add(new CampaignHeat
            {
                CampaignId = campaign.Id,
                Campaign = campaign,
                SequenceNumber = heatNo++,
                GradeCode = campaign.GradeCode,
                PlannedQuantityMt = quantity
            });
            remaining -= quantity;
        }
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

    private static string InventoryKey(string material, string grade, string section) => $"{material}|{grade}|{section}";
    private sealed record CampaignCompatibilityKey(string GradeCode, string CasterSectionCode, string RouteCode);
}
