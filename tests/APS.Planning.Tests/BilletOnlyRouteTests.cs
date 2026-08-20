using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

/// <summary>
/// GitHub #34, acceptance scenario 6: a route that sells cast intermediate (billet/bloom/slab)
/// directly, with no downstream rolling at all, must be a legitimate configuration - not an error
/// forced by an assumption that every route contains a HotRoll operation.
/// </summary>
public sealed class BilletOnlyRouteTests
{
    [Fact]
    public void Route_with_no_hot_roll_operation_plans_to_ccm_without_error()
    {
        var due = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        var po = new ProductionOrder
        {
            ProductionOrderNumber = "PO-BILLET-ONLY",
            DemandSource = DemandSourceType.MakeToOrder,
            MaterialCode = "BLT-G1",
            GradeCode = "G-BILLET",
            FinalCrossSectionCode = "150X150",
            CasterSectionCode = "150X150",
            RouteCode = "BILLET-ONLY-ROUTE",
            PlannedQuantityMt = 60m,
            RemainingQuantityMt = 60m,
            RequiredDate = due,
            Priority = 5,
            Status = ProductionOrderStatus.Planned
        };

        var eaf = SteelResource("EAF-1", ProcessUnitType.Eaf, ResourceType.Furnace);
        eaf.MinimumHeatWeightMt = 50m;
        eaf.NominalHeatWeightMt = 60m;
        eaf.MaximumHeatWeightMt = 70m;
        var lrf = SteelResource("LRF-1", ProcessUnitType.Lrf, ResourceType.Refining);
        var ccm = SteelResource("CCM-1", ProcessUnitType.Ccm, ResourceType.Caster);
        var resources = new[] { eaf, lrf, ccm };

        var route = new RoutePlanningInput(
            new[]
            {
                RouteOperation(10, ProcessOperationType.Eaf),
                RouteOperation(20, ProcessOperationType.Lrf),
                RouteOperation(30, ProcessOperationType.Ccm)
                // Deliberately no HotRoll operation - this plant sells billet directly.
            },
            Array.Empty<RouteResourceCapability>());

        var links = new[]
        {
            new PlantFlowLink { FromResourceId = eaf.Id, ToResourceId = lrf.Id, CouplingType = FlowCouplingType.HotTransfer, MinimumTransferTime = TimeSpan.FromMinutes(5), SupportsHotTransfer = true, IsEnabled = true },
            new PlantFlowLink { FromResourceId = lrf.Id, ToResourceId = ccm.Id, CouplingType = FlowCouplingType.HotTransfer, MinimumTransferTime = TimeSpan.FromMinutes(5), SupportsHotTransfer = true, IsEnabled = true },
        };

        var engine = new PlanningEngine(
            new CampaignPlanningService(),
            new ProductionStructurePlanningService(),
            new FiniteScheduleOptimizer());

        var result = engine.Run(new PlanningRunRequest(
            new[] { po },
            Array.Empty<InventoryPosition>(),
            resources,
            new[]
            {
                new ResourceCapability { ResourceId = ccm.Id, RouteCode = "BILLET-ONLY-ROUTE", GradeCode = "G-BILLET", OutputCrossSectionCode = "150X150", ThroughputMtPerHour = 60m },
            },
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            links,
            new CampaignPlanningPolicy(60m, 50m, 70m, 500m, 1000m),
            new ProductionStructurePlanningPolicy(),
            due.AddDays(-5),
            due.AddDays(5),
            10,
            RoutePlanning: route));

        Assert.True(result.IsFeasible, string.Join("; ", result.Schedule.Issues.Select(x => x.Message)));
        Assert.DoesNotContain(result.ProductionStructure.Issues, x => x.Code == "ROUTE_HOT_ROLLING_MISSING");
        Assert.NotEmpty(result.ProductionStructure.CastSequences.SelectMany(x => x.Heats));
        Assert.Empty(result.ProductionStructure.RollingPlans);
        Assert.DoesNotContain(result.ProductionStructure.SchedulingTasks, x => x.TaskType == FiniteScheduleTaskType.HotRolling);
    }

    private static ManufacturingRouteOperation RouteOperation(int sequence, ProcessOperationType operation) => new()
    {
        ManufacturingRouteId = Guid.NewGuid(),
        RouteCode = "BILLET-ONLY-ROUTE",
        SequenceNumber = sequence,
        ProcessOperationType = operation,
        ReleaseWorkOrderType = operation == ProcessOperationType.Ccm ? WorkOrderType.Casting : WorkOrderType.Steelmaking,
        Requirement = RequirementDisposition.Required
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
