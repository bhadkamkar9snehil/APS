using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

public sealed class PlanReleaseBuilderTests
{
    [Fact]
    public void Released_sms_and_rm_work_orders_preserve_po_and_so_lineage()
    {
        var salesOrder = new SalesOrder
        {
            SalesOrderNumber = "45000123",
            ItemNumber = "10",
            MaterialCode = "FG-16",
            GradeCode = "G1",
            FinalCrossSectionCode = "16MM",
            OrderQuantityMt = 100m,
            OpenQuantityMt = 100m,
            RequiredDate = new DateTime(2026, 8, 22)
        };
        var po = new ProductionOrder
        {
            ProductionOrderNumber = "PO-1001",
            DemandSource = DemandSourceType.MakeToOrder,
            MaterialCode = "FG-16",
            GradeCode = "G1",
            GradeSequenceClassCode = "SEQ-A",
            FinalCrossSectionCode = "16MM",
            CasterSectionCode = "150X150",
            RouteCode = "SMS-RM",
            PlannedQuantityMt = 100m,
            RemainingQuantityMt = 100m,
            RequiredDate = salesOrder.RequiredDate,
            SalesOrderId = salesOrder.Id,
            SalesOrder = salesOrder
        };
        var campaign = new Campaign
        {
            CampaignNumber = "CMP-00001",
            GradeSequenceClassCode = "SEQ-A",
            CasterSectionCode = "150X150",
            RouteCode = "SMS-RM",
            PlannedQuantityMt = 100m,
            FreshSteelRequirementMt = 100m,
            RequiredDate = po.RequiredDate
        };
        campaign.Allocations.Add(new CampaignAllocation
        {
            CampaignId = campaign.Id,
            Campaign = campaign,
            ProductionOrderId = po.Id,
            ProductionOrder = po,
            PlannedQuantityMt = 100m,
            FreshSteelQuantityMt = 100m
        });
        var gradeSequence = new CampaignGradeSequence
        {
            CampaignId = campaign.Id,
            Campaign = campaign,
            SequenceNumber = 1,
            GradeCode = "G1",
            PlannedQuantityMt = 100m
        };
        campaign.GradeSequence.Add(gradeSequence);
        var heat = new CampaignHeat
        {
            CampaignId = campaign.Id,
            Campaign = campaign,
            CampaignGradeSequenceId = gradeSequence.Id,
            CampaignGradeSequence = gradeSequence,
            SequenceNumber = 1,
            GradeCode = "G1",
            PlannedQuantityMt = 100m
        };
        campaign.Heats.Add(heat);

        var millId = Guid.NewGuid();
        var eafResourceId = Guid.NewGuid();
        var rollingPlan = new RollingPlan
        {
            CampaignId = campaign.Id,
            ProductionOrderId = po.Id,
            RollingMillResourceId = millId,
            SequenceNumber = 1,
            GradeCode = "G1",
            InputCrossSectionCode = "150X150",
            OutputCrossSectionCode = "16MM",
            RouteCode = "SMS-RM",
            PlannedQuantityMt = 100m,
            FreshSteelQuantityMt = 100m
        };
        rollingPlan.Allocations.Add(new RollingPlanAllocation
        {
            RollingPlanId = rollingPlan.Id,
            RollingPlan = rollingPlan,
            CampaignId = campaign.Id,
            ProductionOrderId = po.Id,
            ProductionOrder = po,
            PlannedQuantityMt = 100m,
            FreshSteelQuantityMt = 100m
        });

        var start = new DateTime(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);
        var steelmakingTaskId = Guid.NewGuid();
        var rollingTaskId = Guid.NewGuid();
        var steelmakingTask = new FiniteScheduleTask(
            steelmakingTaskId,
            heat.Id,
            // Not FiniteScheduleTaskType.Casting: PlanReleaseBuilder's casting-assignment filter treats
            // TaskType.Casting as a legacy fallback classifier (in addition to ProcessOperationType.Ccm),
            // so an Eaf/steelmaking task carrying it would get double-counted as a casting assignment too.
            FiniteScheduleTaskType.Finishing,
            "Heat 1",
            "G1",
            "150X150",
            100m,
            null,
            null,
            0,
            Array.Empty<FiniteScheduleResourceOption>(),
            Array.Empty<FiniteScheduleDependency>(),
            ProcessOperationType.Eaf);
        var rollingTask = new FiniteScheduleTask(
            rollingTaskId,
            rollingPlan.Id,
            FiniteScheduleTaskType.HotRolling,
            "Roll 1",
            "G1",
            "16MM",
            100m,
            null,
            null,
            0,
            Array.Empty<FiniteScheduleResourceOption>(),
            Array.Empty<FiniteScheduleDependency>(),
            ProcessOperationType.HotRoll);

        var structure = new ProductionStructurePlanningResult(
            Array.Empty<CastSequence>(),
            new[] { rollingPlan },
            Array.Empty<PlannedBilletSupply>(),
            new[] { steelmakingTask, rollingTask },
            Array.Empty<PlanningIssue>());
        var schedule = new FiniteScheduleResult(
            "Optimal",
            true,
            0,
            new[]
            {
                new FiniteScheduleAssignment(steelmakingTaskId, heat.Id, eafResourceId, start, start.AddHours(1)),
                new FiniteScheduleAssignment(rollingTaskId, rollingPlan.Id, millId, start.AddHours(1), start.AddHours(3))
            },
            Array.Empty<PlanningIssue>());
        var planVersionId = Guid.NewGuid();

        var release = new PlanReleaseBuilder().Build(new PlanReleaseBuildRequest(
            planVersionId,
            new[] { campaign },
            structure,
            schedule));

        Assert.Equal(2, release.WorkOrders.Count);
        var sms = release.WorkOrders.Single(w => w.WorkOrderType == WorkOrderType.Steelmaking);
        var rm = release.WorkOrders.Single(w => w.WorkOrderType == WorkOrderType.HotRolling);
        Assert.Equal(po.Id, Assert.Single(sms.Allocations).ProductionOrderId);
        Assert.Equal(po.Id, Assert.Single(rm.Allocations).ProductionOrderId);
        Assert.Equal(salesOrder.Id, Assert.Single(rm.Allocations).ProductionOrder!.SalesOrderId);
        Assert.Equal(2, release.Operations.Count);
    }

    /// <summary>
    /// GitHub #34. The release path classified steelmaking with a hard-coded Eaf/Lrf/Vd set, while
    /// SteelmakingRouteProjector builds heat tasks from route.Take(ccmIndex) - anything the route
    /// places before the caster. The two disagreed about what a steelmaking operation is, so a
    /// pre-caster operation outside that set was scheduled and then silently dropped from the released
    /// plan: correct schedule, incomplete release, no error anywhere.
    /// </summary>
    [Fact]
    public void Pre_caster_operation_outside_eaf_lrf_vd_survives_release()
    {
        var campaign = new Campaign
        {
            CampaignNumber = "CMP-00002",
            GradeSequenceClassCode = "SEQ-A",
            CasterSectionCode = "150X150",
            RouteCode = "BOF-RM",
            PlannedQuantityMt = 60m,
            FreshSteelRequirementMt = 60m,
            RequiredDate = new DateTime(2026, 8, 22)
        };
        var po = new ProductionOrder
        {
            ProductionOrderNumber = "PO-2001",
            DemandSource = DemandSourceType.MakeToOrder,
            MaterialCode = "FG-16",
            GradeCode = "G1",
            GradeSequenceClassCode = "SEQ-A",
            FinalCrossSectionCode = "16MM",
            CasterSectionCode = "150X150",
            RouteCode = "BOF-RM",
            PlannedQuantityMt = 60m,
            RemainingQuantityMt = 60m,
            RequiredDate = campaign.RequiredDate
        };
        campaign.Allocations.Add(new CampaignAllocation
        {
            CampaignId = campaign.Id,
            Campaign = campaign,
            ProductionOrderId = po.Id,
            ProductionOrder = po,
            PlannedQuantityMt = 60m,
            FreshSteelQuantityMt = 60m
        });
        var gradeSequence = new CampaignGradeSequence
        {
            CampaignId = campaign.Id,
            Campaign = campaign,
            SequenceNumber = 1,
            GradeCode = "G1",
            PlannedQuantityMt = 60m
        };
        campaign.GradeSequence.Add(gradeSequence);
        var heat = new CampaignHeat
        {
            CampaignId = campaign.Id,
            Campaign = campaign,
            CampaignGradeSequenceId = gradeSequence.Id,
            CampaignGradeSequence = gradeSequence,
            SequenceNumber = 1,
            GradeCode = "G1",
            PlannedQuantityMt = 60m
        };
        campaign.Heats.Add(heat);

        var start = new DateTime(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);

        // A primary vessel that is not an EAF. ProcessOperationType has no BOF/AOD/RH member yet, so a
        // plant configuring one today types it Unknown and the route still schedules it - which is
        // exactly the operation the old whitelist discarded.
        var primary = HeatTask(heat.Id, "Primary vessel", ProcessOperationType.Unknown, FiniteScheduleTaskType.Eaf);
        var refining = HeatTask(heat.Id, "Refining", ProcessOperationType.Lrf, FiniteScheduleTaskType.Lrf);
        var casting = HeatTask(heat.Id, "Cast", ProcessOperationType.Ccm, FiniteScheduleTaskType.Casting);

        var structure = new ProductionStructurePlanningResult(
            Array.Empty<CastSequence>(),
            Array.Empty<RollingPlan>(),
            Array.Empty<PlannedBilletSupply>(),
            new[] { primary, refining, casting },
            Array.Empty<PlanningIssue>());
        var schedule = new FiniteScheduleResult(
            "Optimal",
            true,
            0,
            new[]
            {
                new FiniteScheduleAssignment(primary.TaskId, heat.Id, Guid.NewGuid(), start, start.AddHours(1)),
                new FiniteScheduleAssignment(refining.TaskId, heat.Id, Guid.NewGuid(), start.AddHours(1), start.AddHours(2)),
                new FiniteScheduleAssignment(casting.TaskId, heat.Id, Guid.NewGuid(), start.AddHours(2), start.AddHours(3))
            },
            Array.Empty<PlanningIssue>());

        var release = new PlanReleaseBuilder().Build(new PlanReleaseBuildRequest(
            Guid.NewGuid(),
            new[] { campaign },
            structure,
            schedule));

        var sms = release.WorkOrders.Single(w => w.WorkOrderType == WorkOrderType.Steelmaking);
        var ccm = release.WorkOrders.Single(w => w.WorkOrderType == WorkOrderType.Casting);

        // Every scheduled operation reaches the shop floor: two upstream on the SMS order, one on the
        // caster. Under the whitelist the primary vessel vanished and the released plan began at the
        // ladle furnace.
        Assert.Equal(2, release.Operations.Count(x => x.WorkOrderId == sms.Id));
        Assert.Equal(1, release.Operations.Count(x => x.WorkOrderId == ccm.Id));
        Assert.Equal(3, release.Operations.Count);

        // The work order still spans the real upstream window rather than starting an hour late.
        Assert.Equal(start, sms.PlannedStart);
        Assert.Equal(start.AddHours(2), sms.PlannedEnd);
    }

    private static FiniteScheduleTask HeatTask(
        Guid heatId,
        string name,
        ProcessOperationType operationType,
        FiniteScheduleTaskType taskType) => new(
        Guid.NewGuid(),
        heatId,
        taskType,
        name,
        "G1",
        "150X150",
        60m,
        null,
        null,
        0,
        Array.Empty<FiniteScheduleResourceOption>(),
        Array.Empty<FiniteScheduleDependency>(),
        operationType);
}
