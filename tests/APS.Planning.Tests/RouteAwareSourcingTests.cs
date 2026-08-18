using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

public sealed class RouteAwareSourcingTests
{
    [Fact]
    public void Rare_alternate_lrf_keeps_internal_make_feasible_when_primary_lrf_is_down()
    {
        var due = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        var po = Order("PO-LRF-ALT", 100m, due);
        var eaf = SteelResource("EAF-1", ProcessUnitType.Eaf, ResourceType.Furnace);
        eaf.MinimumHeatWeightMt = 50m;
        eaf.NominalHeatWeightMt = 60m;
        eaf.MaximumHeatWeightMt = 70m;
        var primary = SteelResource("LRF-1", ProcessUnitType.Lrf, ResourceType.Refining);
        primary.OperatingState = ResourceOperatingState.Breakdown;
        var rare = SteelResource("LRF-2-RARE", ProcessUnitType.Lrf, ResourceType.Refining);
        var ccm = SteelResource("CCM-1", ProcessUnitType.Ccm, ResourceType.Caster);

        var result = new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            new[] { po },
            Array.Empty<InventoryPosition>(),
            Policy(),
            Resources: new[] { eaf, primary, rare, ccm },
            MaterialSupplyPolicy: new MaterialSupplyPlanningPolicy(
                AllowInternalMake: true,
                AllowExternalBuy: false,
                AllowTransfer: false,
                AllowManualSupply: false),
            PlanningReferenceTimeUtc: due.AddDays(-20),
            RoutePlanning: SteelRoute()));

        Assert.Equal(100m, result.FreshSteelRequirementsMt[po.Id]);
        Assert.NotEmpty(result.Campaigns.SelectMany(x => x.Heats));
        var make = Assert.Single(result.SourcingAlternatives!, x =>
            x.ProductionOrderId == po.Id && x.ActionType == MaterialSupplyActionType.Make);
        Assert.True(make.IsFeasible);
        Assert.True(make.IsSelected);
    }

    [Fact]
    public void Required_lrf_unavailable_rejects_make_and_selects_approved_buy_before_heat_formation()
    {
        var reference = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var due = reference.AddDays(20);
        var po = Order("PO-LRF-BUY", 100m, due);
        var eaf = SteelResource("EAF-1", ProcessUnitType.Eaf, ResourceType.Furnace);
        eaf.MinimumHeatWeightMt = 50m;
        eaf.NominalHeatWeightMt = 60m;
        eaf.MaximumHeatWeightMt = 70m;
        var lrf = SteelResource("LRF-1", ProcessUnitType.Lrf, ResourceType.Refining);
        lrf.OperatingState = ResourceOperatingState.Breakdown;
        var ccm = SteelResource("CCM-1", ProcessUnitType.Ccm, ResourceType.Caster);
        var rule = new MaterialSourcingRule
        {
            RuleCode = "BUY-WHEN-SMS-ROUTE-DOWN",
            GradeCode = po.GradeCode,
            CrossSectionCode = po.CasterSectionCode,
            AllowMake = true,
            AllowBuy = true,
            AllowTransfer = false,
            AllowManualSupply = false,
            PreferredAction = MaterialSupplyActionType.Make,
            PurchaseLeadTime = TimeSpan.FromDays(5),
            PreferredSupplierCode = "QUALIFIED-SUPPLIER"
        };

        var result = new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            new[] { po },
            Array.Empty<InventoryPosition>(),
            Policy(),
            Resources: new[] { eaf, lrf, ccm },
            MaterialSupplyPolicy: new MaterialSupplyPlanningPolicy(
                AllowInternalMake: true,
                AllowExternalBuy: false,
                AllowTransfer: false,
                AllowManualSupply: false),
            MaterialSourcingRules: new[] { rule },
            PlanningReferenceTimeUtc: reference,
            RoutePlanning: SteelRoute()));

        Assert.Equal(0m, result.FreshSteelRequirementsMt[po.Id]);
        Assert.Empty(result.Campaigns.SelectMany(x => x.Heats));
        var supply = Assert.Single(result.PlannedSupplyAllocations!, x => x.ProductionOrderId == po.Id);
        Assert.Equal(MaterialSupplyActionType.Buy, supply.ActionType);

        var make = Assert.Single(result.SourcingAlternatives!, x => x.ActionType == MaterialSupplyActionType.Make);
        Assert.False(make.IsFeasible);
        Assert.False(make.IsSelected);
        Assert.Contains("route", make.RejectionReason!, StringComparison.OrdinalIgnoreCase);

        var buy = Assert.Single(result.SourcingAlternatives!, x => x.ActionType == MaterialSupplyActionType.Buy);
        Assert.True(buy.IsFeasible);
        Assert.True(buy.IsSelected);
    }

    [Fact]
    public void Purchase_moq_and_multiple_create_excess_without_inflating_demand_reservation()
    {
        var reference = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var po = Order("PO-MOQ", 63m, reference.AddDays(15));
        var rule = new MaterialSourcingRule
        {
            RuleCode = "BUY-MOQ",
            GradeCode = po.GradeCode,
            CrossSectionCode = po.CasterSectionCode,
            AllowMake = false,
            AllowBuy = true,
            AllowTransfer = false,
            AllowManualSupply = false,
            PreferredAction = MaterialSupplyActionType.Buy,
            PurchaseLeadTime = TimeSpan.FromDays(2),
            MinimumBuyQuantityMt = 100m,
            BuyOrderMultipleMt = 25m
        };

        var result = new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            new[] { po },
            Array.Empty<InventoryPosition>(),
            Policy(),
            Resources: Array.Empty<Resource>(),
            MaterialSourcingRules: new[] { rule },
            PlanningReferenceTimeUtc: reference));

        var supply = Assert.Single(result.PlannedSupplyAllocations!);
        Assert.Equal(MaterialSupplyActionType.Buy, supply.ActionType);
        Assert.Equal(63m, supply.QuantityMt);
        Assert.Equal(100m, supply.PlannedReceiptQuantityMt);
        Assert.Equal(37m, supply.ProjectedExcessQuantityMt);

        var allocation = Assert.Single(result.InventoryAllocations, x => x.Use == PlanningInventoryUse.PlannedPurchaseFeed);
        Assert.Equal(63m, allocation.QuantityMt);
    }

    private static RoutePlanningInput SteelRoute() => new(
        new[]
        {
            RouteOperation(1, ProcessOperationType.Eaf),
            RouteOperation(2, ProcessOperationType.Lrf),
            RouteOperation(3, ProcessOperationType.Ccm)
        },
        Array.Empty<RouteResourceCapability>());

    private static ManufacturingRouteOperation RouteOperation(int sequence, ProcessOperationType operation) => new()
    {
        ManufacturingRouteId = Guid.NewGuid(),
        RouteCode = "STEEL-ROUTE",
        SequenceNumber = sequence,
        ProcessOperationType = operation,
        ReleaseWorkOrderType = operation == ProcessOperationType.Ccm ? WorkOrderType.Casting : WorkOrderType.Steelmaking,
        Requirement = RequirementDisposition.Required
    };

    private static CampaignPlanningPolicy Policy() => new(
        NominalHeatSizeMt: 60m,
        MinimumHeatSizeMt: 50m,
        MaximumHeatSizeMt: 70m,
        TargetCampaignQuantityMt: 500m,
        MaximumCampaignQuantityMt: 1000m);

    private static ProductionOrder Order(string number, decimal quantity, DateTime due) => new()
    {
        ProductionOrderNumber = number,
        DemandSource = DemandSourceType.MakeToOrder,
        MaterialCode = "FG-G42",
        GradeCode = "G42",
        FinalCrossSectionCode = "16MM",
        CasterSectionCode = "150X150",
        RouteCode = "STEEL-ROUTE",
        PlannedQuantityMt = quantity,
        RemainingQuantityMt = quantity,
        RequiredDate = due,
        Priority = 10,
        Status = ProductionOrderStatus.Planned
    };

    private static Resource SteelResource(string code, ProcessUnitType unitType, ResourceType type) => new()
    {
        PlantId = Guid.NewGuid(),
        ProcessStageId = Guid.NewGuid(),
        Code = code,
        Name = code,
        ProcessUnitType = unitType,
        ResourceType = type
    };
}
