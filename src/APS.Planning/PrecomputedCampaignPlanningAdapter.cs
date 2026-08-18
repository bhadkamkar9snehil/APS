using APS.Application;
using APS.Domain;

namespace APS.Planning;

/// <summary>
/// Makes Campaign a consumer of canonical material facts when the upstream BOM/time-phased pass has already
/// decided what each PO can cover from qualified supply and what must be made internally. Compatibility callers
/// without precomputed material continue through CampaignPlanningService's legacy material path unchanged.
/// </summary>
public static class PrecomputedCampaignPlanningAdapter
{
    private const decimal QuantityToleranceMt = 0.0001m;

    public static CampaignPlanningResult FormCampaigns(
        ICampaignPlanningService campaignPlanning,
        CampaignPlanningRequest request)
    {
        var precomputed = request.PrecomputedMaterialDemand;
        if (precomputed is null)
            return campaignPlanning.FormCampaigns(request);

        var rows = precomputed.ToDictionary(x => x.ProductionOrderId);
        var activeOrders = request.ProductionOrders
            .Where(x => x.Status is ProductionOrderStatus.Planned or ProductionOrderStatus.Firmed)
            .ToArray();

        foreach (var po in activeOrders)
        {
            if (!rows.TryGetValue(po.Id, out var row))
                throw new InvalidOperationException($"Canonical material demand is missing for Production Order {po.ProductionOrderNumber}.");

            var balance = row.CoveredIntermediateMt + row.FreshSteelRequirementMt;
            if (Math.Abs(row.RollingRequirementMt - balance) > QuantityToleranceMt)
                throw new InvalidOperationException(
                    $"Canonical material demand for Production Order {po.ProductionOrderNumber} does not conserve quantity: " +
                    $"rolling={row.RollingRequirementMt:0.####} MT, covered={row.CoveredIntermediateMt:0.####} MT, fresh={row.FreshSteelRequirementMt:0.####} MT.");
        }

        // Run only Campaign grouping/heat construction. All material pools are removed so the legacy service cannot
        // reserve inventory, committed receipts or external supply a second time. MAKE remains enabled because the
        // base grouping path expects a residual source, but its material answer is discarded below.
        var groupingRequest = request with
        {
            Inventory = Array.Empty<InventoryPosition>(),
            ExternalMaterialSupplies = Array.Empty<ExternalMaterialSupply>(),
            CommittedMaterialSupplies = Array.Empty<CommittedMaterialSupply>(),
            MaterialSupplyPolicy = new MaterialSupplyPlanningPolicy(
                AllowInternalMake: true,
                AllowExternalBuy: false,
                AllowTransfer: false,
                AllowManualSupply: false),
            MaterialSourcingRules = Array.Empty<MaterialSourcingRule>(),
            PrecomputedMaterialDemand = null
        };

        var grouped = campaignPlanning.FormCampaigns(groupingRequest);

        foreach (var campaign in grouped.Campaigns)
        {
            campaign.ExistingIntermediateInventoryMt = 0m;
            campaign.FreshSteelRequirementMt = 0m;

            var remainingByPo = campaign.Allocations
                .GroupBy(x => x.ProductionOrderId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var row = rows[g.Key];
                        return new Remaining(row.CoveredIntermediateMt, row.FreshSteelRequirementMt);
                    });

            foreach (var allocation in campaign.Allocations
                         .OrderBy(x => x.ProductionOrder?.RequiredDate ?? DateTime.MaxValue)
                         .ThenBy(x => x.ProductionOrderId))
            {
                var remaining = remainingByPo[allocation.ProductionOrderId];
                var covered = Math.Min(allocation.PlannedQuantityMt, remaining.CoveredMt);
                var fresh = Math.Min(
                    Math.Max(0m, allocation.PlannedQuantityMt - covered),
                    remaining.FreshMt);

                allocation.ExistingIntermediateInventoryMt = covered;
                allocation.FreshSteelQuantityMt = fresh;
                remaining.CoveredMt -= covered;
                remaining.FreshMt -= fresh;
                campaign.ExistingIntermediateInventoryMt += covered;
                campaign.FreshSteelRequirementMt += fresh;
            }
        }

        var rolling = rows.ToDictionary(x => x.Key, x => x.Value.RollingRequirementMt);
        var freshSteel = rows.ToDictionary(x => x.Key, x => x.Value.FreshSteelRequirementMt);
        var allocations = rows.Values
            .SelectMany(x => x.CoverageAllocations)
            .OrderBy(x => x.ProductionOrderId)
            .ThenBy(x => x.AvailableFromUtc ?? DateTime.MinValue)
            .ThenBy(x => x.MaterialCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var onHand = activeOrders.ToDictionary(
            x => x.Id,
            x => allocations
                .Where(a => a.ProductionOrderId == x.Id && a.Use == PlanningInventoryUse.IntermediateFeed)
                .Sum(a => a.QuantityMt));
        var external = activeOrders.ToDictionary(
            x => x.Id,
            x => allocations
                .Where(a => a.ProductionOrderId == x.Id && a.Use == PlanningInventoryUse.ExternalIntermediateFeed)
                .Sum(a => a.QuantityMt));

        return new CampaignPlanningResult(
            grouped.Campaigns,
            Array.Empty<ProductionOrder>(),
            rolling,
            freshSteel,
            onHand,
            allocations,
            external,
            grouped.HeatAllocations,
            Array.Empty<PlanningSupplyAllocation>(),
            activeOrders.ToDictionary(x => x.Id, _ => 0m),
            activeOrders.ToDictionary(x => x.Id, _ => 0m),
            Array.Empty<PlanningSupplyAlternative>());
    }

    private sealed class Remaining(decimal coveredMt, decimal freshMt)
    {
        public decimal CoveredMt { get; set; } = coveredMt;
        public decimal FreshMt { get; set; } = freshMt;
    }
}
