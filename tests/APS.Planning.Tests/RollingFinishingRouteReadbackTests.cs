using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class RollingFinishingRouteReadbackTests
{
    [Fact]
    public async Task One_route_operation_preserves_all_scheduled_blocks_in_workspace_readback()
    {
        var now = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var po = new ProductionOrder
        {
            ProductionOrderNumber = "PO-ROUTE-BLOCKS",
            DemandSource = DemandSourceType.MakeToOrder,
            MaterialCode = "FG-HRC",
            GradeCode = "G1",
            FinalCrossSectionCode = "HRC",
            CasterSectionCode = "150X150",
            RouteCode = "ROUTE-BLOCKS",
            PlannedQuantityMt = 100m,
            RemainingQuantityMt = 100m,
            RequiredDate = now.AddDays(2)
        };

        var rolling = new RollingPlan
        {
            SequenceNumber = 1,
            GradeCode = po.GradeCode,
            InputCrossSectionCode = po.CasterSectionCode,
            OutputCrossSectionCode = po.FinalCrossSectionCode,
            RouteCode = po.RouteCode,
            PlannedQuantityMt = 100m,
            FreshSteelQuantityMt = 100m
        };
        rolling.Allocations.Add(new RollingPlanAllocation
        {
            RollingPlanId = rolling.Id,
            RollingPlan = rolling,
            CampaignId = Guid.NewGuid(),
            ProductionOrderId = po.Id,
            ProductionOrder = po,
            PlannedQuantityMt = 100m,
            FreshSteelQuantityMt = 100m
        });

        var routePlan = new RouteOperationPlan
        {
            RouteCode = po.RouteCode,
            UpstreamPlanId = rolling.Id,
            ProcessOperationType = ProcessOperationType.HotRoll,
            ReleaseWorkOrderType = WorkOrderType.HotRolling,
            SequenceNumber = 20,
            GradeCode = po.GradeCode,
            InputCrossSectionCode = po.CasterSectionCode,
            OutputCrossSectionCode = po.FinalCrossSectionCode,
            PlannedQuantityMt = 100m
        };
        routePlan.Allocations.Add(new RouteOperationPlanAllocation
        {
            RouteOperationPlanId = routePlan.Id,
            RouteOperationPlan = routePlan,
            CampaignId = rolling.Allocations.Single().CampaignId,
            ProductionOrderId = po.Id,
            ProductionOrder = po,
            PlannedQuantityMt = 100m
        });

        var mill = new Resource
        {
            PlantId = Guid.NewGuid(),
            ProcessStageId = Guid.NewGuid(),
            Code = "HRM-1",
            Name = "Hot Rolling Mill 1",
            ResourceType = ResourceType.RollingMill,
            ProcessUnitType = ProcessUnitType.HotRollingMill,
            OperatingState = ResourceOperatingState.Available
        };
        var option = new FiniteScheduleResourceOption(mill.Id, 60);
        var task1 = new FiniteScheduleTask(
            Guid.NewGuid(), routePlan.Id, FiniteScheduleTaskType.HotRolling,
            "HotRoll block 1", po.GradeCode, po.FinalCrossSectionCode, 50m,
            now, po.RequiredDate, 1, new[] { option }, Array.Empty<FiniteScheduleDependency>(),
            ProcessOperationType.HotRoll);
        var task2 = new FiniteScheduleTask(
            Guid.NewGuid(), routePlan.Id, FiniteScheduleTaskType.HotRolling,
            "HotRoll block 2", po.GradeCode, po.FinalCrossSectionCode, 50m,
            now, po.RequiredDate, 1, new[] { option }, Array.Empty<FiniteScheduleDependency>(),
            ProcessOperationType.HotRoll);

        var structure = new ProductionStructurePlanningResult(
            Array.Empty<CastSequence>(),
            new[] { rolling },
            Array.Empty<PlannedBilletSupply>(),
            new[] { task1, task2 },
            Array.Empty<PlanningIssue>(),
            RouteOperationPlans: new[] { routePlan });
        var campaignPlan = new CampaignPlanningResult(
            Array.Empty<Campaign>(),
            Array.Empty<ProductionOrder>(),
            new Dictionary<Guid, decimal> { [po.Id] = 100m },
            new Dictionary<Guid, decimal> { [po.Id] = 100m },
            new Dictionary<Guid, decimal> { [po.Id] = 0m },
            Array.Empty<PlanningInventoryAllocation>());
        var schedule = new FiniteScheduleResult(
            "Optimal",
            true,
            0,
            new[]
            {
                new FiniteScheduleAssignment(task1.TaskId, routePlan.Id, mill.Id, now, now.AddHours(1)),
                new FiniteScheduleAssignment(task2.TaskId, routePlan.Id, mill.Id, now.AddHours(1), now.AddHours(2))
            },
            Array.Empty<PlanningIssue>());
        var planVersionId = Guid.NewGuid();
        var result = new PlanningRunResult(
            planVersionId,
            now,
            campaignPlan,
            structure,
            schedule,
            true,
            new[]
            {
                new PlanningTaskIdentity(task1.TaskId, routePlan.Id, "ROUTE:BLOCK-1", task1.TaskType),
                new PlanningTaskIdentity(task2.TaskId, routePlan.Id, "ROUTE:BLOCK-2", task2.TaskType)
            });
        var request = new PlanningRunRequest(
            new[] { po },
            Array.Empty<InventoryPosition>(),
            new[] { mill },
            Array.Empty<ResourceCapability>(),
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            Array.Empty<PlantFlowLink>(),
            new CampaignPlanningPolicy(60m, 50m, 70m, 500m, 1000m),
            new ProductionStructurePlanningPolicy(),
            now,
            now.AddDays(7));

        await using var db = CreateDb();
        db.Resources.Add(mill);
        await db.SaveChangesAsync();
        var repository = new PlanVersionRepository(db);
        await repository.SaveAsync(new PersistPlanningRunRequest(
            request,
            result,
            PlanTriggerType.Manual,
            now,
            "Multi-block route readback"));

        var workspace = await new PlannerWorkspaceQueryService(db, repository)
            .GetRollingFinishingAsync(planVersionId);

        var rollingView = Assert.Single(workspace!.RollingPlans);
        var routeView = Assert.Single(rollingView.DownstreamOperations);
        Assert.Equal(ProcessOperationType.HotRoll, routeView.ProcessOperationType);
        Assert.Equal(2, routeView.ScheduledOperations.Count);
        Assert.Equal(100m, routeView.ScheduledOperations.Sum(x => x.QuantityMt));
        Assert.All(routeView.ScheduledOperations, x => Assert.Equal(mill.Id, x.ResourceId));
    }

    private static ApsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-route-block-readback-{Guid.NewGuid():N}")
            .Options;
        return new ApsDbContext(options);
    }
}