using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

/// <summary>
/// GitHub #34: the pre-CCM steelmaking projector and its Make-feasibility pre-check must not
/// silently drop or reject route operations whose ProcessOperationType isn't Eaf/Lrf/Vd. A plant
/// can configure any secondary-metallurgy step (BOF, AOD/VOD, induction furnace, RH, a second
/// refining pass, ...) between the primary vessel and the caster; the route master defines what
/// exists, not a hard-coded type whitelist in code.
/// </summary>
public sealed class ConfigurableSteelmakingRouteTests
{
    [Fact]
    public void Route_operation_outside_eaf_lrf_vd_between_primary_vessel_and_ccm_is_still_scheduled()
    {
        var due = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        var po = Order();
        var eaf = SteelResource("PRIMARY-1", ProcessUnitType.Eaf, ResourceType.Furnace);
        eaf.MinimumHeatWeightMt = 50m;
        eaf.NominalHeatWeightMt = 60m;
        eaf.MaximumHeatWeightMt = 70m;
        // Stands in for a configured secondary-metallurgy step that is not Eaf/Lrf/Vd (e.g. RH, AOD/VOD,
        // or a plant-specific step) positioned before CCM - the point is that its ProcessOperationType is
        // outside the old whitelist and in a position (pre-CCM) that type would not normally occupy.
        var middle = SteelResource("SECONDARY-1", ProcessUnitType.TmtWaterBox, ResourceType.Generic);
        var ccm = SteelResource("CCM-1", ProcessUnitType.Ccm, ResourceType.Caster);
        // ConfiguredRouteProductionStructureBuilder currently requires every route to contain a HotRoll
        // operation regardless of product, and RollingFeedProjector currently requires a physical Reheat
        // hop between CCM and HotRoll - both are separate, still-open instances of the same #34 hard-coding
        // problem. Included here only to isolate this test to the pre-CCM fix under test.
        var reheat = SteelResource("RHF-1", ProcessUnitType.ReheatingFurnace, ResourceType.Generic);
        var hotMill = SteelResource("HRM-1", ProcessUnitType.HotRollingMill, ResourceType.RollingMill);
        var resources = new[] { eaf, middle, ccm, reheat, hotMill };

        var route = new RoutePlanningInput(
            new[]
            {
                RouteOperation(10, ProcessOperationType.Eaf),
                RouteOperation(20, ProcessOperationType.Tmt),
                RouteOperation(30, ProcessOperationType.Ccm),
                RouteOperation(40, ProcessOperationType.Reheat),
                RouteOperation(50, ProcessOperationType.HotRoll)
            },
            new[]
            {
                new RouteResourceCapability { ResourceId = hotMill.Id, RouteCode = "FLEX-ROUTE", ProcessOperationType = ProcessOperationType.HotRoll },
            });

        var links = new[]
        {
            new PlantFlowLink { FromResourceId = eaf.Id, ToResourceId = middle.Id, CouplingType = FlowCouplingType.HotTransfer, MinimumTransferTime = TimeSpan.FromMinutes(5), SupportsHotTransfer = true, IsEnabled = true },
            new PlantFlowLink { FromResourceId = middle.Id, ToResourceId = ccm.Id, CouplingType = FlowCouplingType.HotTransfer, MinimumTransferTime = TimeSpan.FromMinutes(5), SupportsHotTransfer = true, IsEnabled = true },
            new PlantFlowLink { FromResourceId = ccm.Id, ToResourceId = reheat.Id, CouplingType = FlowCouplingType.Buffered, MinimumTransferTime = TimeSpan.Zero, IsEnabled = true },
            new PlantFlowLink { FromResourceId = reheat.Id, ToResourceId = hotMill.Id, CouplingType = FlowCouplingType.Buffered, MinimumTransferTime = TimeSpan.Zero, IsEnabled = true },
            new PlantFlowLink { FromResourceId = ccm.Id, ToResourceId = hotMill.Id, CouplingType = FlowCouplingType.Buffered, MinimumTransferTime = TimeSpan.Zero, IsEnabled = true },
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
                new ResourceCapability { ResourceId = ccm.Id, RouteCode = "FLEX-ROUTE", GradeCode = "G-FLEX", OutputCrossSectionCode = "150X150", ThroughputMtPerHour = 60m },
                new ResourceCapability { ResourceId = hotMill.Id, RouteCode = "FLEX-ROUTE", GradeCode = "G-FLEX", InputCrossSectionCode = "150X150", OutputCrossSectionCode = "HRC", ThroughputMtPerHour = 60m },
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

        var middleTasks = result.ProductionStructure.SchedulingTasks.Where(x => x.ProcessOperationType == ProcessOperationType.Tmt).ToArray();
        Assert.NotEmpty(middleTasks);

        var middleAssignments = result.Schedule.Assignments.Where(a => middleTasks.Select(t => t.TaskId).Contains(a.TaskId)).ToArray();
        Assert.NotEmpty(middleAssignments);
        Assert.All(middleAssignments, a => Assert.Equal(middle.Id, a.ResourceId));
    }

    private static ManufacturingRouteOperation RouteOperation(int sequence, ProcessOperationType operation) => new()
    {
        ManufacturingRouteId = Guid.NewGuid(),
        RouteCode = "FLEX-ROUTE",
        SequenceNumber = sequence,
        ProcessOperationType = operation,
        ReleaseWorkOrderType = operation == ProcessOperationType.Ccm ? WorkOrderType.Casting : WorkOrderType.Steelmaking,
        Requirement = RequirementDisposition.Required
    };

    private static ProductionOrder Order() => new()
    {
        ProductionOrderNumber = "PO-FLEX-ROUTE",
        DemandSource = DemandSourceType.MakeToOrder,
        MaterialCode = "FG-FLEX",
        GradeCode = "G-FLEX",
        FinalCrossSectionCode = "HRC",
        CasterSectionCode = "150X150",
        RouteCode = "FLEX-ROUTE",
        PlannedQuantityMt = 60m,
        RemainingQuantityMt = 60m,
        RequiredDate = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
        Priority = 5,
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
