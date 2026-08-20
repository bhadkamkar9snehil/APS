using APS.Application;
using APS.Domain;
using Xunit;

namespace APS.Planning.Tests;

/// <summary>
/// GitHub #17: a plant operating-state scenario - an outage, a derating, a grade restriction - must
/// change the plan it is applied to. PlanningScenarioApplier and its entities existed but nothing
/// called them, so contingency planning had no effect on any plan at all. These go through the
/// whole engine because a scenario has to reach campaign formation, route projection and the solver
/// alike, not just one of them.
/// </summary>
public sealed class PlanningScenarioTests
{
    private static readonly DateTime Due = new(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Baseline_plan_keeps_both_casters_eligible()
    {
        var plant = Plant();
        var result = Run(plant, scenario: null);

        Assert.True(result.IsFeasible, Messages(result));

        // The reference point the scenario tests below move away from: with no scenario applied both
        // casters remain genuine alternatives for casting, whichever one the solver settles on.
        var castingOptions = result.ProductionStructure.SchedulingTasks
            .Where(x => x.ProcessOperationType == ProcessOperationType.Ccm)
            .SelectMany(x => x.ResourceOptions.Select(option => option.ResourceId))
            .ToHashSet();

        Assert.Contains(plant.CcmA.Id, castingOptions);
        Assert.Contains(plant.CcmB.Id, castingOptions);
        // ...and the cheaper one is the one actually used, which is what each scenario below overturns.
        Assert.All(CasterAssignments(result, plant), used => Assert.Equal(plant.CcmA.Id, used));
    }

    [Fact]
    public void Caster_taken_down_for_the_whole_horizon_is_not_planned_on()
    {
        var plant = Plant();

        var result = Run(plant, Scenario("SMS-DOWN", new ResourceScenarioOverride
        {
            ResourceId = plant.CcmA.Id,
            OperatingState = ResourceOperatingState.Breakdown,
            Reason = "Mould breakout"
        }));

        Assert.True(result.IsFeasible, Messages(result));
        // The whole horizon is covered, so this is a state change on the resource, not a calendar hole.
        Assert.All(CasterAssignments(result, plant), used => Assert.Equal(plant.CcmB.Id, used));
    }

    [Fact]
    public void Caster_down_for_part_of_the_horizon_becomes_an_outage_window_not_a_dead_resource()
    {
        var plant = Plant();
        var outageEnd = Due.AddDays(-5).AddHours(6);

        var result = Run(plant, Scenario("SMS-PARTIAL", new ResourceScenarioOverride
        {
            ResourceId = plant.CcmA.Id,
            OperatingState = ResourceOperatingState.PlannedMaintenance,
            EffectiveFromUtc = Due.AddDays(-5),
            EffectiveToUtc = outageEnd,
            Reason = "Tundish change"
        }));

        Assert.True(result.IsFeasible, Messages(result));

        // A partial outage must not disable the caster for the rest of the horizon - it may still be
        // used, just not during the window. Caster A is the cheaper option and would otherwise be
        // scheduled at the horizon start, which is inside the window.
        foreach (var assignment in result.Schedule.Assignments.Where(x => x.ResourceId == plant.CcmA.Id))
        {
            Assert.True(
                assignment.StartUtc >= outageEnd,
                $"Caster A was scheduled at {assignment.StartUtc:u}, inside its maintenance window ending {outageEnd:u}.");
        }
    }

    [Fact]
    public void Grade_restriction_moves_work_off_the_restricted_caster()
    {
        var plant = Plant();

        var result = Run(plant, Scenario("QUALITY-HOLD", new ResourceScenarioOverride
        {
            ResourceId = plant.CcmA.Id,
            OperatingState = ResourceOperatingState.QualityRestricted,
            ForbiddenGradeCode = GradeCode,
            Reason = "Mould powder qualification withdrawn"
        }));

        Assert.True(result.IsFeasible, Messages(result));
        // The resource stays available for other grades but loses its capability for this one.
        Assert.All(CasterAssignments(result, plant), used => Assert.Equal(plant.CcmB.Id, used));
    }

    [Fact]
    public void Scenario_leaves_the_configured_masters_untouched_for_the_next_run()
    {
        var plant = Plant();
        var casterA = plant.CcmA;

        Run(plant, Scenario("SMS-DOWN", new ResourceScenarioOverride
        {
            ResourceId = casterA.Id,
            OperatingState = ResourceOperatingState.Breakdown
        }));

        // A scenario is a what-if. If it mutated the caller's masters, the baseline would be gone and
        // every later plan in the process would silently inherit the outage.
        Assert.Equal(ResourceOperatingState.Available, casterA.OperatingState);
        Assert.Equal(100m, casterA.CapacityFactorPct);
    }

    [Fact]
    public void Scenario_preserves_resource_scheduling_mode()
    {
        var plant = Plant();
        plant.CcmB.SchedulingMode = ResourceSchedulingMode.Cumulative;
        plant.CcmB.CapacityBasis = ResourceCapacityBasis.Slots;
        plant.CcmB.NominalConcurrentCapacity = 2m;
        plant.CcmB.AppliesSequenceRules = false;

        var state = PlanningScenarioApplier.Apply(
            new[] { plant.CcmA, plant.CcmB },
            Array.Empty<ResourceCapability>(),
            Array.Empty<ResourceCalendar>(),
            Scenario("DERATE", new ResourceScenarioOverride
            {
                ResourceId = plant.CcmA.Id,
                OperatingState = ResourceOperatingState.CapacityDerated,
                CapacityFactorPct = 50m
            }),
            Due.AddDays(-5),
            Due.AddDays(5));

        // Cloning a resource for a scenario must carry its physical scheduling model across, or an
        // untouched cumulative resource would silently revert to one-block-at-a-time (#35).
        var clonedB = state.Resources.Single(x => x.Id == plant.CcmB.Id);
        Assert.Equal(ResourceSchedulingMode.Cumulative, clonedB.SchedulingMode);
        Assert.Equal(ResourceCapacityBasis.Slots, clonedB.CapacityBasis);
        Assert.Equal(2m, clonedB.NominalConcurrentCapacity);
        Assert.False(clonedB.AppliesSequenceRules);
    }

    private const string RouteCode = "SCENARIO-ROUTE";
    private const string GradeCode = "G-SCENARIO";

    private sealed record SteelPlant(Resource Eaf, Resource Lrf, Resource CcmA, Resource CcmB, PlantFlowLink[] Links);

    private static SteelPlant Plant()
    {
        var eaf = SteelResource("EAF-1", ProcessUnitType.Eaf, ResourceType.Furnace);
        eaf.MinimumHeatWeightMt = 50m;
        eaf.NominalHeatWeightMt = 60m;
        eaf.MaximumHeatWeightMt = 70m;
        var lrf = SteelResource("LRF-1", ProcessUnitType.Lrf, ResourceType.Refining);
        var ccmA = SteelResource("CCM-A", ProcessUnitType.Ccm, ResourceType.Caster);
        var ccmB = SteelResource("CCM-B", ProcessUnitType.Ccm, ResourceType.Caster);

        return new SteelPlant(eaf, lrf, ccmA, ccmB, new[]
        {
            Link(eaf.Id, lrf.Id),
            Link(lrf.Id, ccmA.Id),
            Link(lrf.Id, ccmB.Id)
        });
    }

    private static PlanningScenario Scenario(string code, params ResourceScenarioOverride[] overrides) => new()
    {
        ScenarioCode = code,
        Name = code,
        ResourceOverrides = overrides
    };

    private static PlanningRunResult Run(SteelPlant plant, PlanningScenario? scenario)
    {
        var engine = new PlanningEngine(
            new CampaignPlanningService(),
            new ProductionStructurePlanningService(),
            new FiniteScheduleOptimizer());

        return engine.Run(new PlanningRunRequest(
            new[] { Order() },
            Array.Empty<InventoryPosition>(),
            new[] { plant.Eaf, plant.Lrf, plant.CcmA, plant.CcmB },
            // Caster A casts six times faster, so a makespan-minimising solver reaches for it unless
            // something stops it. That makes "the plan moved off caster A" attributable to the scenario.
            new[] { CcmCapability(plant.CcmA.Id, throughputMtPerHour: 60m), CcmCapability(plant.CcmB.Id, throughputMtPerHour: 10m) },
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            plant.Links,
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
            SteelGrades: new[] { new SteelGrade { GradeCode = GradeCode, Description = GradeCode } },
            Scenario: scenario));
    }

    private static IReadOnlyCollection<Guid> CasterAssignments(PlanningRunResult result, SteelPlant plant)
    {
        var casterIds = new[] { plant.CcmA.Id, plant.CcmB.Id };
        var castingTasks = result.ProductionStructure.SchedulingTasks
            .Where(x => x.ProcessOperationType == ProcessOperationType.Ccm)
            .Select(x => x.TaskId)
            .ToHashSet();
        var used = result.Schedule.Assignments
            .Where(x => castingTasks.Contains(x.TaskId) && casterIds.Contains(x.ResourceId))
            .Select(x => x.ResourceId)
            .ToArray();

        Assert.NotEmpty(used);
        return used;
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

    private static ResourceCapability CcmCapability(Guid ccmId, decimal throughputMtPerHour = 60m) => new()
    {
        ResourceId = ccmId,
        RouteCode = RouteCode,
        GradeCode = GradeCode,
        ProcessOperationType = ProcessOperationType.Ccm,
        OutputCrossSectionCode = "150X150",
        ThroughputMtPerHour = throughputMtPerHour
    };

    private static ManufacturingRouteOperation RouteOperation(int sequence, ProcessOperationType operation) => new()
    {
        ManufacturingRouteId = Guid.NewGuid(),
        RouteCode = RouteCode,
        SequenceNumber = sequence,
        ProcessOperationType = operation,
        ReleaseWorkOrderType = operation == ProcessOperationType.Ccm ? WorkOrderType.Casting : WorkOrderType.Steelmaking,
        Requirement = RequirementDisposition.Required
    };

    private static ProductionOrder Order() => new()
    {
        ProductionOrderNumber = "PO-SCENARIO",
        DemandSource = DemandSourceType.MakeToOrder,
        MaterialCode = "BLT-SCENARIO",
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
