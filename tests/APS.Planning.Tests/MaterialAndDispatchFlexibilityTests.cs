using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

public sealed class MaterialAndDispatchFlexibilityTests
{
    [Fact]
    public void Future_material_receipt_delays_consumption_instead_of_rejecting_the_plan()
    {
        var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var mill = Resource("RM-1", ProcessUnitType.HotRollingMill, ResourceType.RollingMill);
        var task = Task(Guid.NewGuid(), ProcessOperationType.HotRoll, FiniteScheduleTaskType.HotRolling,
            new[] { new FiniteScheduleResourceOption(mill.Id, 30) });

        var material = new[]
        {
            new ScheduledMaterialEvent("POOL", 10000, ScheduledMaterialEventTiming.FixedTime,
                FixedTimeUtc: start.AddHours(6), LedgerEventType: MaterialBalanceEventType.PlannedProductionReceipt),
            new ScheduledMaterialEvent("POOL", -10000, ScheduledMaterialEventTiming.TaskStart,
                TaskId: task.TaskId, LedgerEventType: MaterialBalanceEventType.PlannedConsumption)
        };

        var result = new FiniteScheduleOptimizer().Solve(new FiniteScheduleRequest(
            start,
            start.AddDays(2),
            new[] { task },
            new[] { mill },
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            5,
            MaterialEvents: material));

        Assert.True(result.IsFeasible, string.Join("; ", result.Issues.Select(x => x.Message)));
        var assignment = Assert.Single(result.Assignments);
        Assert.True(assignment.StartUtc >= start.AddHours(6));
    }

    [Fact]
    public void Progressive_future_receipts_can_feed_progressive_rolling_blocks()
    {
        var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var mill = Resource("RM-1", ProcessUnitType.HotRollingMill, ResourceType.RollingMill);
        var source = Guid.NewGuid();
        var first = Task(source, ProcessOperationType.HotRoll, FiniteScheduleTaskType.HotRolling,
            new[] { new FiniteScheduleResourceOption(mill.Id, 30) });
        var second = Task(source, ProcessOperationType.HotRoll, FiniteScheduleTaskType.HotRolling,
            new[] { new FiniteScheduleResourceOption(mill.Id, 30) });

        var material = new[]
        {
            new ScheduledMaterialEvent("POOL", 5000, ScheduledMaterialEventTiming.FixedTime,
                FixedTimeUtc: start.AddHours(2), LedgerEventType: MaterialBalanceEventType.PlannedProductionReceipt),
            new ScheduledMaterialEvent("POOL", 5000, ScheduledMaterialEventTiming.FixedTime,
                FixedTimeUtc: start.AddHours(8), LedgerEventType: MaterialBalanceEventType.PlannedProductionReceipt),
            new ScheduledMaterialEvent("POOL", -5000, ScheduledMaterialEventTiming.TaskStart,
                TaskId: first.TaskId, LedgerEventType: MaterialBalanceEventType.PlannedConsumption),
            new ScheduledMaterialEvent("POOL", -5000, ScheduledMaterialEventTiming.TaskStart,
                TaskId: second.TaskId, LedgerEventType: MaterialBalanceEventType.PlannedConsumption)
        };

        var result = new FiniteScheduleOptimizer().Solve(new FiniteScheduleRequest(
            start,
            start.AddDays(2),
            new[] { first, second },
            new[] { mill },
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            5,
            MaterialEvents: material));

        Assert.True(result.IsFeasible, string.Join("; ", result.Issues.Select(x => x.Message)));
        var ordered = result.Assignments.OrderBy(x => x.StartUtc).ToArray();
        Assert.Equal(2, ordered.Length);
        Assert.True(ordered[0].StartUtc >= start.AddHours(2));
        Assert.True(ordered[0].StartUtc < start.AddHours(8));
        Assert.True(ordered[1].StartUtc >= start.AddHours(8));
    }

    [Fact]
    public void Rare_alternate_lrf_remains_a_real_resource_option_and_can_be_selected_when_primary_is_unavailable()
    {
        var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var lrf1 = Resource("LRF-1", ProcessUnitType.Lrf, ResourceType.Refining);
        var lrf2 = Resource("LRF-2-RARE", ProcessUnitType.Lrf, ResourceType.Refining);
        var task = Task(Guid.NewGuid(), ProcessOperationType.Lrf, FiniteScheduleTaskType.Lrf,
            new[]
            {
                new FiniteScheduleResourceOption(lrf1.Id, 45, 0, "LRF_GRADE_ROUTE_CAPABILITY"),
                new FiniteScheduleResourceOption(lrf2.Id, 45, 100, "LRF_GRADE_ROUTE_CAPABILITY")
            });
        var outage = new ResourceCalendar
        {
            ResourceId = lrf1.Id,
            Start = start,
            End = start.AddHours(8),
            IsAvailable = false,
            ReasonCode = "OUTAGE"
        };

        var result = new FiniteScheduleOptimizer().Solve(new FiniteScheduleRequest(
            start,
            start.AddHours(8),
            new[] { task },
            new[] { lrf1, lrf2 },
            new[] { outage },
            Array.Empty<TransitionRule>(),
            5));

        Assert.True(result.IsFeasible, string.Join("; ", result.Issues.Select(x => x.Message)));
        var assignment = Assert.Single(result.Assignments);
        Assert.Equal(lrf2.Id, assignment.ResourceId);
    }

    [Fact]
    public void Sms_down_with_approved_buy_rule_creates_future_billet_supply_without_fake_sms_heats()
    {
        var due = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        var po = ProductionOrder("PO-COIL-1", due);
        var ccm = Resource("CCM-1", ProcessUnitType.Ccm, ResourceType.Caster);
        var rule = new MaterialSourcingRule
        {
            RuleCode = "BUY-G42-150",
            GradeCode = po.GradeCode,
            CrossSectionCode = po.CasterSectionCode,
            AllowMake = true,
            AllowBuy = true,
            AllowManualSupply = false,
            PreferredAction = MaterialSupplyActionType.Buy,
            PurchaseLeadTime = TimeSpan.FromDays(5),
            PreferredSupplierCode = "APPROVED-BILLET-SUPPLIER"
        };

        var result = new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            new[] { po },
            Array.Empty<InventoryPosition>(),
            CampaignPolicy(),
            Resources: new[] { ccm },
            MaterialSupplyPolicy: new MaterialSupplyPlanningPolicy(AllowInternalMake: true, AllowExternalBuy: false),
            MaterialSourcingRules: new[] { rule },
            PlanningReferenceTimeUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(0m, result.FreshSteelRequirementsMt[po.Id]);
        Assert.Empty(result.Campaigns.SelectMany(x => x.Heats));
        var supply = Assert.Single(result.PlannedSupplyAllocations!, x => x.ProductionOrderId == po.Id);
        Assert.Equal(MaterialSupplyActionType.Buy, supply.ActionType);
        Assert.Equal(new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc), supply.ExpectedReceiptUtc);
        Assert.Equal(100m, supply.QuantityMt);
    }

    [Fact]
    public void Sms_down_without_approved_external_path_stays_unsourced_instead_of_inventing_supply()
    {
        var po = ProductionOrder("PO-COIL-2", new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));
        var ccm = Resource("CCM-1", ProcessUnitType.Ccm, ResourceType.Caster);

        var result = new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            new[] { po },
            Array.Empty<InventoryPosition>(),
            CampaignPolicy(),
            Resources: new[] { ccm },
            MaterialSupplyPolicy: new MaterialSupplyPlanningPolicy(
                AllowInternalMake: true,
                AllowExternalBuy: false,
                AllowTransfer: false,
                AllowManualSupply: false),
            PlanningReferenceTimeUtc: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(0m, result.FreshSteelRequirementsMt[po.Id]);
        Assert.Empty(result.Campaigns.SelectMany(x => x.Heats));
        var supply = Assert.Single(result.PlannedSupplyAllocations!, x => x.ProductionOrderId == po.Id);
        Assert.Equal(MaterialSupplyActionType.Unsourced, supply.ActionType);
        Assert.Equal(100m, supply.QuantityMt);
    }

    private static CampaignPlanningPolicy CampaignPolicy() => new(
        NominalHeatSizeMt: 60m,
        MinimumHeatSizeMt: 50m,
        MaximumHeatSizeMt: 70m,
        TargetCampaignQuantityMt: 500m,
        MaximumCampaignQuantityMt: 1000m);

    private static ProductionOrder ProductionOrder(string number, DateTime due) => new()
    {
        ProductionOrderNumber = number,
        DemandSource = DemandSourceType.MakeToOrder,
        MaterialCode = "COIL-G42",
        GradeCode = "G42",
        FinalCrossSectionCode = "COIL-8MM",
        CasterSectionCode = "150X150",
        RouteCode = "COIL-ROUTE",
        ProductFamilyCode = "COIL",
        PlannedQuantityMt = 100m,
        RemainingQuantityMt = 100m,
        RequiredDate = due,
        Priority = 10,
        Status = ProductionOrderStatus.Planned
    };

    private static Resource Resource(string code, ProcessUnitType unitType, ResourceType resourceType) => new()
    {
        PlantId = Guid.NewGuid(),
        ProcessStageId = Guid.NewGuid(),
        Code = code,
        Name = code,
        ProcessUnitType = unitType,
        ResourceType = resourceType
    };

    private static FiniteScheduleTask Task(
        Guid source,
        ProcessOperationType operationType,
        FiniteScheduleTaskType taskType,
        IReadOnlyCollection<FiniteScheduleResourceOption> options) => new(
            Guid.NewGuid(),
            source,
            taskType,
            $"{operationType} test",
            "G42",
            "150X150",
            10m,
            null,
            null,
            0,
            options,
            Array.Empty<FiniteScheduleDependency>(),
            operationType);
}
