using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

public sealed class MultiStageRouteTests
{
    [Fact]
    public void Configured_route_schedules_hot_cold_and_finishing_on_stage_specific_resources()
    {
        var po = new ProductionOrder
        {
            ProductionOrderNumber = "PO-ROUTE-1",
            DemandSource = DemandSourceType.MakeToOrder,
            MaterialCode = "FG-COLD-1",
            GradeCode = "G1",
            GradeSequenceClassCode = "SEQ-A",
            FinalCrossSectionCode = "1.0MM",
            CasterSectionCode = "150X150",
            RouteCode = "ROUTE-COLD",
            PlannedQuantityMt = 100m,
            RemainingQuantityMt = 100m,
            RequiredDate = new DateTime(2026, 8, 23),
            Priority = 2
        };
        var plant = Guid.NewGuid();
        var caster = Resource(plant, "CCM-1", ResourceType.Caster, 4);
        var hotMill = Resource(plant, "HRM-1", ResourceType.RollingMill);
        var coldMill = Resource(plant, "CRM-1", ResourceType.RollingMill);
        var finishing = Resource(plant, "FIN-1", ResourceType.FinishingLine);
        var resources = new[] { caster, hotMill, coldMill, finishing };

        var casterCapabilities = new[]
        {
            new ResourceCapability
            {
                ResourceId = caster.Id,
                RouteCode = "ROUTE-COLD",
                GradeCode = "G1",
                OutputCrossSectionCode = "150X150",
                ThroughputMtPerHour = 60m
            }
        };
        var routeId = Guid.NewGuid();
        var routeOperations = new[]
        {
            Operation(routeId, 10, WorkOrderType.HotRolling, "150X150", "HRC"),
            Operation(routeId, 20, WorkOrderType.ColdRolling, "HRC", "CRC", minQueueMinutes: 30),
            Operation(routeId, 30, WorkOrderType.Finishing, "CRC", "1.0MM", minQueueMinutes: 15)
        };
        var routeCapabilities = new[]
        {
            Capability(hotMill.Id, WorkOrderType.HotRolling, "150X150", "HRC", 50m),
            Capability(coldMill.Id, WorkOrderType.ColdRolling, "HRC", "CRC", 40m),
            Capability(finishing.Id, WorkOrderType.Finishing, "CRC", "1.0MM", 35m)
        };
        var links = new[]
        {
            new PlantFlowLink
            {
                FromResourceId = caster.Id,
                ToResourceId = hotMill.Id,
                CouplingType = FlowCouplingType.HotTransfer,
                MinimumTransferTime = TimeSpan.FromMinutes(10),
                MaximumTransferTime = TimeSpan.FromMinutes(180),
                SupportsHotTransfer = true
            }
        };
        var start = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);
        var engine = new PlanningEngine(
            new CampaignPlanningService(),
            new ProductionStructurePlanningService(),
            new FiniteScheduleOptimizer());

        var result = engine.Run(new PlanningRunRequest(
            new[] { po },
            Array.Empty<InventoryPosition>(),
            resources,
            casterCapabilities,
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            links,
            new CampaignPlanningPolicy(50m, 40m, 55m, 250m, 300m),
            new ProductionStructurePlanningPolicy(MaximumHeatsPerCastSequence: 8),
            start,
            start.AddHours(24),
            5,
            RoutePlanning: new RoutePlanningInput(routeOperations, routeCapabilities)));

        Assert.True(result.IsFeasible, string.Join("; ", result.Schedule.Issues.Select(x => x.Message)));
        var hot = Assert.Single(result.ProductionStructure.RollingPlans);
        Assert.Equal("HRC", hot.OutputCrossSectionCode);
        Assert.Equal(hotMill.Id, hot.RollingMillResourceId);

        var downstream = Assert.IsAssignableFrom<IReadOnlyCollection<RouteOperationPlan>>(
            result.ProductionStructure.RouteOperationPlans);
        Assert.Equal(2, downstream.Count);
        var cold = downstream.Single(x => x.OperationType == WorkOrderType.ColdRolling);
        var finish = downstream.Single(x => x.OperationType == WorkOrderType.Finishing);
        Assert.Equal(coldMill.Id, cold.ResourceId);
        Assert.Equal(finishing.Id, finish.ResourceId);
        Assert.Equal("CRC", cold.OutputCrossSectionCode);
        Assert.Equal("1.0MM", finish.OutputCrossSectionCode);

        var coldTasks = result.ProductionStructure.SchedulingTasks.Where(x => x.SourceEntityId == cold.Id).ToArray();
        var finishTasks = result.ProductionStructure.SchedulingTasks.Where(x => x.SourceEntityId == finish.Id).ToArray();
        Assert.NotEmpty(coldTasks);
        Assert.Equal(coldTasks.Length, finishTasks.Length);
        Assert.All(coldTasks, task => Assert.All(task.Dependencies, dependency => Assert.Equal(30, dependency.MinimumLagMinutes)));
        Assert.All(finishTasks, task => Assert.All(task.Dependencies, dependency => Assert.Equal(15, dependency.MinimumLagMinutes)));

        var release = new PlanReleaseBuilder().Build(new PlanReleaseBuildRequest(
            result.PlanVersionId,
            result.CampaignPlan.Campaigns,
            result.ProductionStructure,
            result.Schedule));
        Assert.Contains(release.WorkOrders, x => x.WorkOrderType == WorkOrderType.HotRolling);
        Assert.Contains(release.WorkOrders, x => x.WorkOrderType == WorkOrderType.ColdRolling);
        Assert.Contains(release.WorkOrders, x => x.WorkOrderType == WorkOrderType.Finishing);
        Assert.All(release.WorkOrders.Where(x => x.WorkOrderType is WorkOrderType.HotRolling or WorkOrderType.ColdRolling or WorkOrderType.Finishing),
            workOrder => Assert.Contains(workOrder.Allocations, allocation => allocation.ProductionOrderId == po.Id));
    }

    private static Resource Resource(Guid plant, string code, ResourceType type, int? strands = null) => new()
    {
        PlantId = plant,
        ProcessStageId = Guid.NewGuid(),
        Code = code,
        Name = code,
        ResourceType = type,
        StrandCount = strands
    };

    private static ManufacturingRouteOperation Operation(
        Guid routeId,
        int sequence,
        WorkOrderType type,
        string input,
        string output,
        int minQueueMinutes = 0) => new()
    {
        ManufacturingRouteId = routeId,
        RouteCode = "ROUTE-COLD",
        SequenceNumber = sequence,
        OperationType = type,
        InputCrossSectionCode = input,
        OutputCrossSectionCode = output,
        MinimumQueueTime = TimeSpan.FromMinutes(minQueueMinutes),
        YieldPct = 100m
    };

    private static RouteResourceCapability Capability(
        Guid resourceId,
        WorkOrderType type,
        string input,
        string output,
        decimal throughput) => new()
    {
        ResourceId = resourceId,
        RouteCode = "ROUTE-COLD",
        OperationType = type,
        GradeCode = "G1",
        InputCrossSectionCode = input,
        OutputCrossSectionCode = output,
        ThroughputMtPerHour = throughput
    };
}
