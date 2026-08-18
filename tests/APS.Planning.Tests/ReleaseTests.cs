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
}
