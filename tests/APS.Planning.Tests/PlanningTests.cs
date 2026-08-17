using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

public sealed class MtsProductionOrderServiceTests
{
    [Fact]
    public void Creates_mts_production_order_from_target_stock_gap()
    {
        var service = new MtsProductionOrderService();
        var policy = new StockPolicy("FG-16", "MAT-16", "G1", "16MM", "150X150", "SMS-RM", 250m, 50m, 300m, new DateTime(2026, 8, 22), 1);
        var inventory = new InventoryPosition
        {
            MaterialCode = "MAT-16",
            GradeCode = "G1",
            CrossSectionCode = "16MM",
            AvailableQuantityMt = 80m,
            ReservedQuantityMt = 0m,
            ConfirmedIncomingQuantityMt = 0m,
            AllocatedOutgoingQuantityMt = 0m
        };

        var result = service.Propose(policy, inventory);

        Assert.NotNull(result.ProductionOrder);
        Assert.Equal(DemandSourceType.MakeToStock, result.ProductionOrder!.DemandSource);
        Assert.Equal(170m, result.ProductionOrder.PlannedQuantityMt);
    }
}

public sealed class CampaignPlanningServiceTests
{
    [Fact]
    public void Nets_finished_inventory_then_combines_compatible_mto_and_mts_into_campaign()
    {
        var mto = NewPo("PO-MTO-1", DemandSourceType.MakeToOrder, 100m, 1);
        var mts = NewPo("PO-MTS-1", DemandSourceType.MakeToStock, 100m, 0);
        var inventory = new InventoryPosition
        {
            MaterialCode = "FG-16",
            GradeCode = "G1",
            CrossSectionCode = "16MM",
            AvailableQuantityMt = 40m,
            ReservedQuantityMt = 0m,
            ConfirmedIncomingQuantityMt = 0m,
            AllocatedOutgoingQuantityMt = 0m
        };

        var result = new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            new[] { mto, mts },
            new[] { inventory },
            new CampaignPlanningPolicy(50m, 40m, 55m, 250m, 300m)));

        var campaign = Assert.Single(result.Campaigns);
        Assert.Equal(160m, campaign.PlannedQuantityMt);
        Assert.Equal(2, campaign.Allocations.Count);
        Assert.Equal(4, campaign.Heats.Count);
        Assert.Equal(60m, result.NettedRequirementsMt[mto.Id]);
        Assert.Equal(100m, result.NettedRequirementsMt[mts.Id]);
    }

    private static ProductionOrder NewPo(string number, DemandSourceType type, decimal qty, int priority) => new()
    {
        ProductionOrderNumber = number,
        DemandSource = type,
        MaterialCode = "FG-16",
        GradeCode = "G1",
        FinalCrossSectionCode = "16MM",
        CasterSectionCode = "150X150",
        RouteCode = "SMS-RM",
        PlannedQuantityMt = qty,
        RemainingQuantityMt = qty,
        RequiredDate = new DateTime(2026, 8, 22),
        Priority = priority
    };
}
