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

        var covered = new List<ProductionOrder>();
        var netRequirements = new Dictionary<Guid, decimal>();
        var inventoryPools = request.Inventory
            .GroupBy(i => InventoryKey(i.MaterialCode, i.GradeCode, i.CrossSectionCode))
            .ToDictionary(g => g.Key, g => Math.Max(0m, g.Sum(x => x.ProjectedAvailableQuantityMt)));

        // Inventory allocation is deterministic: committed MTO requirements are protected first,
        // followed by MTS replenishment, while preserving due-date and priority order.
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
            inventoryPools[key] = available - consumed;
            requirement -= consumed;
            netRequirements[po.Id] = requirement;

            if (requirement <= 0m)
            {
                covered.Add(po);
            }
        }

        // GradeSequenceClassCode is the configurable compatibility envelope. Different exact
        // grades may share a campaign only when master data places them in the same sequence class.
        var campaignInputs = ordered
            .Where(po => netRequirements[po.Id] > 0m)
            .GroupBy(po => new CampaignCompatibilityKey(
                SequenceClass(po), po.CasterSectionCode, po.RouteCode))
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
                        BuildGradeSequenceAndHeats(current, request.Policy);
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
                BuildGradeSequenceAndHeats(current, request.Policy);
                campaigns.Add(current);
            }
        }

        return new(campaigns, covered, netRequirements);
    }

    private static Campaign NewCampaign(string prefix, int sequence, CampaignCompatibilityKey key, DateTime requiredDate) =>
        new()
        {
            CampaignNumber = $"{prefix}-{sequence:00000}",
            GradeSequenceClassCode = key.GradeSequenceClassCode,
            CasterSectionCode = key.CasterSectionCode,
            RouteCode = key.RouteCode,
            PlannedQuantityMt = 0m,
            RequiredDate = requiredDate,
            Status = CampaignStatus.Draft
        };

    private static void BuildGradeSequenceAndHeats(Campaign campaign, CampaignPlanningPolicy policy)
    {
        campaign.GradeSequence.Clear();
        campaign.Heats.Clear();

        var gradeGroups = campaign.Allocations
            .Where(a => a.ProductionOrder is not null)
            .GroupBy(a => a.ProductionOrder!.GradeCode)
            .Select(g => new
            {
                GradeCode = g.Key,
                QuantityMt = g.Sum(x => x.PlannedQuantityMt),
                FirstIndex = campaign.Allocations.ToList().FindIndex(x => ReferenceEquals(x, g.First()))
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

    private static string InventoryKey(string material, string grade, string section) => $"{material}|{grade}|{section}";
    private sealed record CampaignCompatibilityKey(string GradeSequenceClassCode, string CasterSectionCode, string RouteCode);
}
