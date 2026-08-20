using APS.Application;
using APS.Domain;
using Xunit;

namespace APS.Planning.Tests;

/// <summary>
/// GitHub #9: superheat and temperature envelopes must actually constrain a plan. The thermal
/// projector and its master data existed but were never reached by PlanningEngine, so a grade's
/// casting window had no effect on which resources or transfer times a plan could use.
/// These run through the full engine, not the projector in isolation, because "is it wired in"
/// is the thing that was wrong.
/// </summary>
public sealed class ThermalConstraintTests
{
    private static readonly DateTime Due = new(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Ladle_that_cannot_reach_the_casting_window_is_rejected_even_when_it_is_cheaper()
    {
        var plant = Plant();

        // LRF-COLD tops out below the grade's minimum casting temperature; LRF-HOT can reach it.
        // The capability penalties deliberately favour LRF-COLD, so if the plan still picks LRF-HOT
        // it can only be the thermal constraint doing it.
        var hotLadle = plant.Lrf;
        var coldLadle = SteelResource("LRF-COLD", ProcessUnitType.Lrf, ResourceType.Refining);

        var resources = new[] { plant.Eaf, hotLadle, coldLadle, plant.Ccm };
        var links = plant.Links
            .Append(Link(plant.Eaf.Id, coldLadle.Id))
            .Append(Link(coldLadle.Id, plant.Ccm.Id))
            .ToArray();

        var result = Run(
            resources,
            links,
            capabilities: new[]
            {
                CcmCapability(plant.Ccm.Id),
                new ResourceCapability { ResourceId = hotLadle.Id, RouteCode = RouteCode, ProcessOperationType = ProcessOperationType.Lrf, AssignmentPenalty = 500 },
                new ResourceCapability { ResourceId = coldLadle.Id, RouteCode = RouteCode, ProcessOperationType = ProcessOperationType.Lrf, AssignmentPenalty = 0 }
            },
            resourceTemperatures: new[]
            {
                LadleCapability(hotLadle.Id, minimumExit: 1540m, nominalExit: 1560m, maximumExit: 1580m),
                LadleCapability(coldLadle.Id, minimumExit: 1450m, nominalExit: 1460m, maximumExit: 1470m)
            });

        Assert.True(result.IsFeasible, Messages(result));

        var lrfTasks = result.ProductionStructure.SchedulingTasks
            .Where(x => x.ProcessOperationType == ProcessOperationType.Lrf)
            .Select(x => x.TaskId)
            .ToHashSet();
        var lrfAssignments = result.Schedule.Assignments.Where(x => lrfTasks.Contains(x.TaskId)).ToArray();

        Assert.NotEmpty(lrfAssignments);
        Assert.All(lrfAssignments, assignment => Assert.Equal(hotLadle.Id, assignment.ResourceId));
    }

    [Fact]
    public void Grade_whose_casting_window_no_ladle_can_reach_is_reported_not_silently_planned()
    {
        var plant = Plant();

        var result = Run(
            new[] { plant.Eaf, plant.Lrf, plant.Ccm },
            plant.Links,
            capabilities: new[] { CcmCapability(plant.Ccm.Id) },
            // The only ladle tops out 80C below the grade's minimum casting temperature.
            resourceTemperatures: new[] { LadleCapability(plant.Lrf.Id, 1440m, 1450m, 1460m) });

        Assert.False(result.IsFeasible);
        Assert.Contains(
            result.ProductionStructure.Issues.Concat(result.Schedule.Issues),
            issue => issue.Code == "THERMAL_ROUTE_INFEASIBLE");
    }

    [Fact]
    public void Transfer_window_is_capped_so_the_heat_cannot_cool_below_the_casting_minimum()
    {
        var plant = Plant();

        // The ladle delivers at most 1560C, the grade must be cast at 1520C or above, and steel loses
        // 2C per minute in transfer: at most 20 minutes may elapse between LRF and CCM.
        // Two heats share one caster, so the second heat would otherwise sit refined in the ladle while
        // the caster finishes the first - the queue the cap has to prevent.
        var result = Run(
            new[] { plant.Eaf, plant.Lrf, plant.Ccm },
            plant.Links,
            capabilities: new[] { CcmCapability(plant.Ccm.Id) },
            resourceTemperatures: new[] { LadleCapability(plant.Lrf.Id, 1540m, 1550m, 1560m) },
            temperatureLossCPerMinute: 2m,
            orders: new[] { Order(), Order("PO-THERMAL-2") });

        Assert.True(result.IsFeasible, Messages(result));

        foreach (var (refining, casting) in HeatOperationPairs(result))
        {
            var gap = (casting.StartUtc - refining.EndUtc).TotalMinutes;
            Assert.True(
                gap <= 20d,
                $"Heat waited {gap:0.#} minutes between refining and casting; above 20 it arrives below the grade's minimum casting temperature.");
        }
    }

    [Fact]
    public void Heat_too_hot_to_cast_is_held_until_it_has_cooled_into_the_window()
    {
        var plant = Plant();

        // The ladle cannot deliver below 1600C but the grade must not be cast above 1580C. At 2C per
        // minute the heat has to wait at least 10 minutes rather than being cast straight away.
        var result = Run(
            new[] { plant.Eaf, plant.Lrf, plant.Ccm },
            plant.Links,
            capabilities: new[] { CcmCapability(plant.Ccm.Id) },
            resourceTemperatures: new[] { LadleCapability(plant.Lrf.Id, 1600m, 1610m, 1620m) },
            temperatureLossCPerMinute: 2m);

        Assert.True(result.IsFeasible, Messages(result));

        foreach (var (refining, casting) in HeatOperationPairs(result))
        {
            var gap = (casting.StartUtc - refining.EndUtc).TotalMinutes;
            Assert.True(
                gap >= 10d,
                $"Heat went to the caster after {gap:0.#} minutes; below 10 it is still above the grade's maximum casting temperature.");
        }
    }

    [Fact]
    public void Plant_without_thermal_master_data_is_unaffected()
    {
        var plant = Plant();

        var result = Run(
            new[] { plant.Eaf, plant.Lrf, plant.Ccm },
            plant.Links,
            capabilities: new[] { CcmCapability(plant.Ccm.Id) },
            resourceTemperatures: null,
            gradeTemperatures: null,
            grade: PlainGrade());

        // A plant that has not configured temperature master data must plan exactly as before.
        Assert.True(result.IsFeasible, Messages(result));
        Assert.DoesNotContain(
            result.ProductionStructure.Issues.Concat(result.Schedule.Issues),
            issue => issue.Code == "THERMAL_ROUTE_INFEASIBLE");
    }

    [Fact]
    public void Cooling_rate_on_a_link_does_not_by_itself_destroy_the_flow_path()
    {
        var plant = Plant();

        // A plant that declares how fast steel cools in transfer but has configured no superheat
        // window: SteelmakingRouteProjector used to read both ends of that absent window as 0, derive
        // a 0-minute maximum transfer lag, discard every resource pair for being below the link's own
        // minimum transfer time, and report PROCESS_FLOW_PATH_MISSING - a plant made unplannable by
        // supplying more master data, not less.
        var result = Run(
            new[] { plant.Eaf, plant.Lrf, plant.Ccm },
            plant.Links,
            capabilities: new[] { CcmCapability(plant.Ccm.Id) },
            resourceTemperatures: null,
            grade: PlainGrade(),
            temperatureLossCPerMinute: 2m);

        Assert.True(result.IsFeasible, Messages(result));
        Assert.DoesNotContain(
            result.ProductionStructure.Issues.Concat(result.Schedule.Issues),
            issue => issue.Code == "PROCESS_FLOW_PATH_MISSING");
    }

    private const string RouteCode = "THERMAL-ROUTE";
    private const string GradeCode = "G-THERMAL";

    private static PlanningRunResult Run(
        IReadOnlyCollection<Resource> resources,
        IReadOnlyCollection<PlantFlowLink> links,
        IReadOnlyCollection<ResourceCapability> capabilities,
        IReadOnlyCollection<ResourceTemperatureCapability>? resourceTemperatures,
        IReadOnlyCollection<GradeProcessTemperatureRequirement>? gradeTemperatures = null,
        SteelGrade? grade = null,
        decimal temperatureLossCPerMinute = 0m,
        IReadOnlyCollection<ProductionOrder>? orders = null)
    {
        grade ??= CastingWindowGrade();
        var thermalLinks = temperatureLossCPerMinute <= 0m
            ? links
            : links.Select(link => Clone(link, temperatureLossCPerMinute)).ToArray();

        var engine = new PlanningEngine(
            new CampaignPlanningService(),
            new ProductionStructurePlanningService(),
            new FiniteScheduleOptimizer());

        return engine.Run(new PlanningRunRequest(
            orders ?? new[] { Order() },
            Array.Empty<InventoryPosition>(),
            resources,
            capabilities,
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            thermalLinks,
            new CampaignPlanningPolicy(60m, 50m, 70m, 500m, 1000m),
            new ProductionStructurePlanningPolicy(),
            Due.AddDays(-5),
            Due.AddDays(5),
            10,
            RoutePlanning: new RoutePlanningInput(
                new[]
                {
                    RouteOperation(10, ProcessOperationType.Eaf),
                    RouteOperation(20, ProcessOperationType.Lrf),
                    RouteOperation(30, ProcessOperationType.Ccm)
                },
                Array.Empty<RouteResourceCapability>()),
            SteelGrades: new[] { grade },
            GradeTemperatureRequirements: gradeTemperatures,
            ResourceTemperatureCapabilities: resourceTemperatures));
    }

    private sealed record SteelPlant(Resource Eaf, Resource Lrf, Resource Ccm, PlantFlowLink[] Links);

    private static SteelPlant Plant()
    {
        var eaf = SteelResource("EAF-1", ProcessUnitType.Eaf, ResourceType.Furnace);
        eaf.MinimumHeatWeightMt = 50m;
        eaf.NominalHeatWeightMt = 60m;
        eaf.MaximumHeatWeightMt = 70m;
        var lrf = SteelResource("LRF-HOT", ProcessUnitType.Lrf, ResourceType.Refining);
        var ccm = SteelResource("CCM-1", ProcessUnitType.Ccm, ResourceType.Caster);
        return new SteelPlant(eaf, lrf, ccm, new[] { Link(eaf.Id, lrf.Id), Link(lrf.Id, ccm.Id) });
    }

    private static PlantFlowLink Link(Guid from, Guid to) => new()
    {
        FromResourceId = from,
        ToResourceId = to,
        CouplingType = FlowCouplingType.HotTransfer,
        MinimumTransferTime = TimeSpan.FromMinutes(5),
        SupportsHotTransfer = true,
        IsEnabled = true
    };

    private static PlantFlowLink Clone(PlantFlowLink link, decimal lossCPerMinute) => new()
    {
        Id = link.Id,
        FromResourceId = link.FromResourceId,
        ToResourceId = link.ToResourceId,
        CouplingType = link.CouplingType,
        MinimumTransferTime = link.MinimumTransferTime,
        MaximumTransferTime = link.MaximumTransferTime,
        SupportsHotTransfer = link.SupportsHotTransfer,
        IsInventoryDecouplingPoint = link.IsInventoryDecouplingPoint,
        NominalTemperatureLossCPerMinute = lossCPerMinute,
        IsEnabled = link.IsEnabled
    };

    private static ResourceTemperatureCapability LadleCapability(
        Guid resourceId,
        decimal minimumExit,
        decimal nominalExit,
        decimal maximumExit) => new()
    {
        ResourceId = resourceId,
        ProcessOperationType = ProcessOperationType.Lrf,
        MinimumAchievableExitTemperatureC = minimumExit,
        NominalExitTemperatureC = nominalExit,
        MaximumAchievableExitTemperatureC = maximumExit,
        CanCorrectTemperature = true
    };

    /// <summary>Grade that must be cast between 1520C and 1580C.</summary>
    private static SteelGrade CastingWindowGrade() => new()
    {
        GradeCode = GradeCode,
        Description = GradeCode,
        MinimumCastingTemperatureC = 1520m,
        TargetCastingTemperatureC = 1550m,
        MaximumCastingTemperatureC = 1580m
    };

    private static SteelGrade PlainGrade() => new() { GradeCode = GradeCode, Description = GradeCode };

    private static ResourceCapability CcmCapability(Guid ccmId) => new()
    {
        ResourceId = ccmId,
        RouteCode = RouteCode,
        GradeCode = GradeCode,
        OutputCrossSectionCode = "150X150",
        ThroughputMtPerHour = 60m
    };

    private static IEnumerable<(FiniteScheduleAssignment Refining, FiniteScheduleAssignment Casting)> HeatOperationPairs(
        PlanningRunResult result)
    {
        var byTask = result.ProductionStructure.SchedulingTasks.ToDictionary(x => x.TaskId);
        var assignments = result.Schedule.Assignments
            .Where(x => byTask.ContainsKey(x.TaskId))
            .ToArray();

        var refining = assignments
            .Where(x => byTask[x.TaskId].ProcessOperationType == ProcessOperationType.Lrf)
            .ToDictionary(x => byTask[x.TaskId].SourceEntityId);
        var casting = assignments
            .Where(x => byTask[x.TaskId].ProcessOperationType == ProcessOperationType.Ccm)
            .ToDictionary(x => byTask[x.TaskId].SourceEntityId);

        Assert.NotEmpty(refining);
        foreach (var heat in refining.Keys.Where(casting.ContainsKey))
        {
            yield return (refining[heat], casting[heat]);
        }
    }

    private static ManufacturingRouteOperation RouteOperation(int sequence, ProcessOperationType operation) => new()
    {
        ManufacturingRouteId = Guid.NewGuid(),
        RouteCode = RouteCode,
        SequenceNumber = sequence,
        ProcessOperationType = operation,
        ReleaseWorkOrderType = operation == ProcessOperationType.Ccm ? WorkOrderType.Casting : WorkOrderType.Steelmaking,
        Requirement = RequirementDisposition.Required
    };

    private static ProductionOrder Order(string number = "PO-THERMAL") => new()
    {
        ProductionOrderNumber = number,
        DemandSource = DemandSourceType.MakeToOrder,
        MaterialCode = "BLT-THERMAL",
        GradeCode = GradeCode,
        FinalCrossSectionCode = "150X150",
        CasterSectionCode = "150X150",
        RouteCode = RouteCode,
        PlannedQuantityMt = 60m,
        RemainingQuantityMt = 60m,
        RequiredDate = Due,
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

    private static string Messages(PlanningRunResult result) =>
        string.Join("; ", result.ProductionStructure.Issues.Concat(result.Schedule.Issues).Select(x => x.Message));
}
