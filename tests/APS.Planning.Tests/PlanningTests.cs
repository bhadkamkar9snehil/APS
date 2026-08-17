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
        var policy = new StockPolicy("FG-16", "MAT-16", "G1", "16MM", "150X150", "SMS-RM", 250m, 50m, 300m, new DateTime(2026, 8, 22), 1, "SEQ-A");
        var inventory = new InventoryPosition
        {
            MaterialCode = "MAT-16",
            GradeCode = "G1",
            CrossSectionCode = "16MM",
            AvailableQuantityMt = 80m
        };

        var result = service.Propose(policy, inventory);

        Assert.NotNull(result.ProductionOrder);
        Assert.Equal(DemandSourceType.MakeToStock, result.ProductionOrder!.DemandSource);
        Assert.Equal(170m, result.ProductionOrder.PlannedQuantityMt);
        Assert.Equal("SEQ-A", result.ProductionOrder.GradeSequenceClassCode);
    }
}

public sealed class CampaignPlanningServiceTests
{
    [Fact]
    public void Nets_finished_inventory_then_combines_compatible_mto_and_mts_into_campaign()
    {
        var mto = NewPo("PO-MTO-1", DemandSourceType.MakeToOrder, 100m, 1, "G1", "SEQ-A");
        var mts = NewPo("PO-MTS-1", DemandSourceType.MakeToStock, 100m, 0, "G1", "SEQ-A");
        var inventory = new InventoryPosition
        {
            MaterialCode = "FG-16",
            GradeCode = "G1",
            CrossSectionCode = "16MM",
            Stage = InventoryStage.FinishedGoods,
            AvailableQuantityMt = 40m
        };

        var result = new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            new[] { mto, mts },
            new[] { inventory },
            new CampaignPlanningPolicy(50m, 40m, 55m, 250m, 300m)));

        var campaign = Assert.Single(result.Campaigns);
        Assert.Equal(160m, campaign.PlannedQuantityMt);
        Assert.Equal(160m, campaign.FreshSteelRequirementMt);
        Assert.Equal(2, campaign.Allocations.Count);
        Assert.Equal(3, campaign.Heats.Count);
        Assert.Single(campaign.GradeSequence);
        Assert.Equal(60m, result.RollingRequirementsMt[mto.Id]);
        Assert.Equal(100m, result.RollingRequirementsMt[mts.Id]);
        Assert.Equal(60m, result.FreshSteelRequirementsMt[mto.Id]);
        Assert.Equal(100m, result.FreshSteelRequirementsMt[mts.Id]);
        Assert.All(campaign.Heats, heat => Assert.InRange(heat.PlannedQuantityMt, 40m, 55m));
    }

    [Fact]
    public void Intermediate_inventory_reduces_sms_requirement_but_not_rolling_requirement()
    {
        var po = NewPo("PO-1", DemandSourceType.MakeToOrder, 100m, 1, "G1", "SEQ-A");
        var inventory = new[]
        {
            new InventoryPosition
            {
                MaterialCode = "FG-16",
                GradeCode = "G1",
                CrossSectionCode = "16MM",
                Stage = InventoryStage.FinishedGoods,
                AvailableQuantityMt = 40m
            },
            new InventoryPosition
            {
                MaterialCode = "BILLET-G1",
                GradeCode = "G1",
                CrossSectionCode = "150X150",
                Stage = InventoryStage.CastIntermediate,
                AvailableQuantityMt = 30m
            }
        };

        var result = new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            new[] { po },
            inventory,
            new CampaignPlanningPolicy(50m, 25m, 55m, 250m, 300m)));

        var campaign = Assert.Single(result.Campaigns);
        Assert.Equal(60m, result.RollingRequirementsMt[po.Id]);
        Assert.Equal(30m, result.IntermediateInventoryAllocatedMt[po.Id]);
        Assert.Equal(30m, result.FreshSteelRequirementsMt[po.Id]);
        Assert.Equal(60m, campaign.PlannedQuantityMt);
        Assert.Equal(30m, campaign.ExistingIntermediateInventoryMt);
        Assert.Equal(30m, campaign.FreshSteelRequirementMt);
        Assert.Equal(30m, Assert.Single(campaign.Heats).PlannedQuantityMt);
    }

    [Fact]
    public void Expected_casting_yield_inflates_heat_input_inside_campaign_planning()
    {
        var po = NewPo("PO-YIELD", DemandSourceType.MakeToOrder, 100m, 1, "G1", "SEQ-A");

        var campaign = Assert.Single(new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            new[] { po },
            Array.Empty<InventoryPosition>(),
            new CampaignPlanningPolicy(
                50m,
                40m,
                55m,
                250m,
                300m,
                ExpectedCastingYieldPct: 95m))).Campaigns);

        Assert.Equal(100m, campaign.FreshSteelRequirementMt);
        Assert.Equal(105.2632m, campaign.Heats.Sum(x => x.PlannedQuantityMt));
        Assert.Equal(105.2632m, Assert.Single(campaign.GradeSequence).PlannedQuantityMt);
        Assert.Equal(2, campaign.Heats.Count);
    }

    [Fact]
    public void Allows_multiple_exact_grades_in_one_campaign_only_through_shared_sequence_class()
    {
        var grade1 = NewPo("PO-1", DemandSourceType.MakeToOrder, 100m, 1, "G1", "SEQ-COMPAT");
        var grade2 = NewPo("PO-2", DemandSourceType.MakeToOrder, 100m, 1, "G2", "SEQ-COMPAT");

        var result = new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            new[] { grade1, grade2 },
            Array.Empty<InventoryPosition>(),
            new CampaignPlanningPolicy(50m, 40m, 55m, 250m, 300m)));

        var campaign = Assert.Single(result.Campaigns);
        Assert.Equal(2, campaign.GradeSequence.Count);
        Assert.Equal(new[] { "G1", "G2" }, campaign.GradeSequence.OrderBy(x => x.SequenceNumber).Select(x => x.GradeCode));
        Assert.Equal(4, campaign.Heats.Count);
    }

    internal static ProductionOrder NewPo(
        string number,
        DemandSourceType type,
        decimal qty,
        int priority,
        string grade,
        string sequenceClass) => new()
    {
        ProductionOrderNumber = number,
        DemandSource = type,
        MaterialCode = "FG-16",
        GradeCode = grade,
        GradeSequenceClassCode = sequenceClass,
        FinalCrossSectionCode = "16MM",
        CasterSectionCode = "150X150",
        RouteCode = "SMS-RM",
        PlannedQuantityMt = qty,
        RemainingQuantityMt = qty,
        RequiredDate = new DateTime(2026, 8, 22),
        Priority = priority
    };
}

public sealed class ProductionStructurePlanningServiceTests
{
    [Fact]
    public void Builds_caster_sequence_billet_supply_and_rolling_plan_with_dependency()
    {
        var po = CampaignPlanningServiceTests.NewPo("PO-1", DemandSourceType.MakeToOrder, 100m, 2, "G1", "SEQ-A");
        var campaign = Assert.Single(new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            new[] { po },
            Array.Empty<InventoryPosition>(),
            new CampaignPlanningPolicy(50m, 40m, 55m, 250m, 300m))).Campaigns);

        var plantId = Guid.NewGuid();
        var casterStage = Guid.NewGuid();
        var millStage = Guid.NewGuid();
        var caster1 = NewResource(plantId, casterStage, "CCM-1", ResourceType.Caster, 4);
        var caster2 = NewResource(plantId, casterStage, "CCM-2", ResourceType.Caster, 4);
        var mill1 = NewResource(plantId, millStage, "RM-1", ResourceType.RollingMill);
        var mill2 = NewResource(plantId, millStage, "RM-2", ResourceType.RollingMill);
        var resources = new[] { caster1, caster2, mill1, mill2 };

        var capabilities = new[]
        {
            CasterCapability(caster1.Id, 60m), CasterCapability(caster2.Id, 55m),
            MillCapability(mill1.Id, 50m), MillCapability(mill2.Id, 45m)
        };
        var links = new[]
        {
            Link(caster1.Id, mill1.Id), Link(caster1.Id, mill2.Id),
            Link(caster2.Id, mill1.Id), Link(caster2.Id, mill2.Id)
        };

        var result = new ProductionStructurePlanningService().Build(new ProductionStructurePlanningRequest(
            new[] { campaign }, resources, capabilities, Array.Empty<TransitionRule>(), links,
            new ProductionStructurePlanningPolicy()));

        Assert.DoesNotContain(result.Issues, i => i.Severity == PlanningIssueSeverity.Error);
        var cast = Assert.Single(result.CastSequences);
        Assert.Equal(2, cast.Heats.Count);
        Assert.Equal(2, result.PlannedBilletSupplies.Count);
        Assert.Equal(100m, result.PlannedBilletSupplies.Sum(x => x.QuantityMt));
        var rolling = Assert.Single(result.RollingPlans);
        Assert.Equal(100m, rolling.PlannedQuantityMt);
        Assert.Single(rolling.Allocations);

        var rollingTask = Assert.Single(result.SchedulingTasks, t => t.TaskType == FiniteScheduleTaskType.HotRolling);
        Assert.NotEmpty(rollingTask.Dependencies);
    }

    private static Resource NewResource(Guid plant, Guid stage, string code, ResourceType type, int? strands = null) => new()
    {
        PlantId = plant,
        ProcessStageId = stage,
        Code = code,
        Name = code,
        ResourceType = type,
        StrandCount = strands
    };

    private static ResourceCapability CasterCapability(Guid id, decimal throughput) => new()
    {
        ResourceId = id,
        GradeCode = "G1",
        OutputCrossSectionCode = "150X150",
        RouteCode = "SMS-RM",
        ThroughputMtPerHour = throughput
    };

    private static ResourceCapability MillCapability(Guid id, decimal throughput) => new()
    {
        ResourceId = id,
        GradeCode = "G1",
        InputCrossSectionCode = "150X150",
        OutputCrossSectionCode = "16MM",
        RouteCode = "SMS-RM",
        ThroughputMtPerHour = throughput
    };

    private static PlantFlowLink Link(Guid caster, Guid mill) => new()
    {
        FromResourceId = caster,
        ToResourceId = mill,
        CouplingType = FlowCouplingType.HotTransfer,
        MinimumTransferTime = TimeSpan.FromMinutes(10),
        MaximumTransferTime = TimeSpan.FromMinutes(120),
        SupportsHotTransfer = true
    };
}

public sealed class FiniteScheduleOptimizerTests
{
    [Fact]
    public void Schedules_cast_before_rolling_with_transfer_lag()
    {
        var caster = new Resource
        {
            PlantId = Guid.NewGuid(), ProcessStageId = Guid.NewGuid(), Code = "CCM-1", Name = "CCM-1", ResourceType = ResourceType.Caster
        };
        var mill = new Resource
        {
            PlantId = caster.PlantId, ProcessStageId = Guid.NewGuid(), Code = "RM-1", Name = "RM-1", ResourceType = ResourceType.RollingMill
        };
        var castTaskId = Guid.NewGuid();
        var rollTaskId = Guid.NewGuid();
        var origin = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);

        var castTask = new FiniteScheduleTask(
            castTaskId, Guid.NewGuid(), FiniteScheduleTaskType.Casting, "Cast", "G1", "150X150", 100m,
            origin, origin.AddHours(4), 2,
            new[] { new FiniteScheduleResourceOption(caster.Id, 60) },
            Array.Empty<FiniteScheduleDependency>());
        var rollTask = new FiniteScheduleTask(
            rollTaskId, Guid.NewGuid(), FiniteScheduleTaskType.HotRolling, "Roll", "G1", "16MM", 100m,
            origin, origin.AddHours(6), 2,
            new[] { new FiniteScheduleResourceOption(mill.Id, 60) },
            new[] { new FiniteScheduleDependency(castTaskId, 10, 120) });

        var result = new FiniteScheduleOptimizer().Solve(new FiniteScheduleRequest(
            origin, origin.AddHours(8), new[] { castTask, rollTask }, new[] { caster, mill },
            Array.Empty<ResourceCalendar>(), Array.Empty<TransitionRule>(), 5));

        Assert.True(result.IsFeasible, string.Join("; ", result.Issues.Select(i => i.Message)));
        var cast = result.Assignments.Single(a => a.TaskId == castTaskId);
        var roll = result.Assignments.Single(a => a.TaskId == rollTaskId);
        Assert.True(roll.StartUtc >= cast.EndUtc.AddMinutes(10));
        Assert.True(roll.StartUtc <= cast.EndUtc.AddMinutes(120));
    }
}
