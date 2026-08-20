using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

/// <summary>
/// #16: physical caster/CCM assignment must be a genuine CP-SAT decision (choosing between multiple
/// eligible casters, using them in parallel when needed) rather than the old pre-solve greedy pick of
/// the first eligible caster - while still keeping every heat in one continuous cast sequence on the
/// SAME physical caster, since tundish continuity requires it.
/// </summary>
public sealed class MultiCasterAssignmentTests
{
    [Fact]
    public void Solver_uses_both_eligible_casters_in_parallel_when_one_alone_cannot_fit_the_horizon()
    {
        var (casterA, casterB, capabilities) = TwoEquallyEligibleCasters();
        var start = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);

        // Two independent single-heat campaigns, each a 60-minute cast at 50 MT / 50 MT-per-hour.
        // A horizon of 90 minutes is enough for both to cast in parallel on two casters, but not enough
        // for them to run sequentially on one (120+ minutes) - so feasibility itself proves the solver
        // used both.
        var po1 = Order("PO-MC-1", "G1", start, dedicatedCampaign: true);
        var po2 = Order("PO-MC-2", "G1", start, dedicatedCampaign: true);

        var engine = new PlanningEngine(
            new CampaignPlanningService(),
            new ProductionStructurePlanningService(),
            new FiniteScheduleOptimizer());

        var result = engine.Run(new PlanningRunRequest(
            new[] { po1, po2 },
            Array.Empty<InventoryPosition>(),
            new[] { casterA, casterB },
            capabilities,
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            Array.Empty<PlantFlowLink>(),
            new CampaignPlanningPolicy(50m, 40m, 55m, 250m, 300m),
            new ProductionStructurePlanningPolicy(MaximumHeatsPerCastSequence: 8),
            start,
            start.AddMinutes(90),
            5,
            RoutePlanning: new RoutePlanningInput(RouteOperations(), Array.Empty<RouteResourceCapability>())));

        Assert.True(result.IsFeasible, string.Join("; ", result.Schedule.Issues.Select(x => x.Message)
            .Concat(result.ProductionStructure.Issues.Select(x => x.Message))));

        Assert.Equal(2, result.ProductionStructure.CastSequences.Count);
        var casterIds = result.ProductionStructure.CastSequences.Select(x => x.CasterResourceId).ToHashSet();
        Assert.Equal(2, casterIds.Count);
        Assert.Equal(new HashSet<Guid> { casterA.Id, casterB.Id }, casterIds);

        // Post-solve strand material units must be regenerated against the resolved caster, not left
        // pointing at a placeholder.
        Assert.All(result.ProductionStructure.PlannedStrandMaterialUnits!,
            unit => Assert.Contains(unit.CasterResourceId, casterIds));
    }

    [Fact]
    public void Heats_in_one_cast_sequence_always_resolve_to_the_same_physical_caster()
    {
        var (casterA, casterB, capabilities) = TwoEquallyEligibleCasters();
        var start = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);

        // One campaign large enough to split into multiple heats (100 MT / 50 MT nominal heat size),
        // so all heats land in a single continuous cast sequence with no time pressure forcing a choice -
        // without the cross-task linking constraint CP-SAT would be free to split them across casters.
        var po = Order("PO-MC-3", "G1", start, quantityMt: 100m);

        var engine = new PlanningEngine(
            new CampaignPlanningService(),
            new ProductionStructurePlanningService(),
            new FiniteScheduleOptimizer());

        var result = engine.Run(new PlanningRunRequest(
            new[] { po },
            Array.Empty<InventoryPosition>(),
            new[] { casterA, casterB },
            capabilities,
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            Array.Empty<PlantFlowLink>(),
            new CampaignPlanningPolicy(50m, 40m, 55m, 250m, 300m),
            new ProductionStructurePlanningPolicy(MaximumHeatsPerCastSequence: 8),
            start,
            start.AddHours(12),
            5,
            RoutePlanning: new RoutePlanningInput(RouteOperations(), Array.Empty<RouteResourceCapability>())));

        Assert.True(result.IsFeasible, string.Join("; ", result.Schedule.Issues.Select(x => x.Message)
            .Concat(result.ProductionStructure.Issues.Select(x => x.Message))));

        var sequence = Assert.Single(result.ProductionStructure.CastSequences);
        Assert.True(sequence.Heats.Count > 1);
        Assert.NotEqual(Guid.Empty, sequence.CasterResourceId);

        var castingAssignments = result.Schedule.Assignments
            .Where(a => result.ProductionStructure.SchedulingTasks
                .Single(t => t.TaskId == a.TaskId).TaskType == FiniteScheduleTaskType.Casting)
            .ToArray();
        Assert.Equal(sequence.Heats.Count, castingAssignments.Length);
        Assert.All(castingAssignments, a => Assert.Equal(sequence.CasterResourceId, a.ResourceId));

        Assert.All(result.ProductionStructure.PlannedStrandMaterialUnits!,
            unit => Assert.Equal(sequence.CasterResourceId, unit.CasterResourceId));
    }

    private static (Resource CasterA, Resource CasterB, ResourceCapability[] Capabilities) TwoEquallyEligibleCasters()
    {
        var plant = Guid.NewGuid();
        var casterA = new Resource
        {
            PlantId = plant,
            ProcessStageId = Guid.NewGuid(),
            Code = "CCM-A",
            Name = "CCM-A",
            ResourceType = ResourceType.Caster,
            StrandCount = 4
        };
        var casterB = new Resource
        {
            PlantId = plant,
            ProcessStageId = Guid.NewGuid(),
            Code = "CCM-B",
            Name = "CCM-B",
            ResourceType = ResourceType.Caster,
            StrandCount = 4
        };
        var capabilities = new[]
        {
            new ResourceCapability
            {
                ResourceId = casterA.Id,
                GradeCode = "G1",
                OutputCrossSectionCode = "150X150",
                RouteCode = "SMS-MC",
                ThroughputMtPerHour = 50m
            },
            new ResourceCapability
            {
                ResourceId = casterB.Id,
                GradeCode = "G1",
                OutputCrossSectionCode = "150X150",
                RouteCode = "SMS-MC",
                ThroughputMtPerHour = 50m
            }
        };
        return (casterA, casterB, capabilities);
    }

    private static ManufacturingRouteOperation[] RouteOperations() => new[]
    {
        new ManufacturingRouteOperation
        {
            ManufacturingRouteId = Guid.NewGuid(),
            RouteCode = "SMS-MC",
            SequenceNumber = 10,
            ProcessOperationType = ProcessOperationType.Ccm,
            ReleaseWorkOrderType = WorkOrderType.Casting
        }
    };

    private static ProductionOrder Order(string number, string grade, DateTime start, decimal quantityMt = 50m, bool dedicatedCampaign = false)
    {
        var po = new ProductionOrder
        {
            ProductionOrderNumber = number,
            DemandSource = DemandSourceType.MakeToOrder,
            MaterialCode = $"FG-{number}",
            GradeCode = grade,
            GradeSequenceClassCode = "SEQ-A",
            FinalCrossSectionCode = "150X150",
            CasterSectionCode = "150X150",
            RouteCode = "SMS-MC",
            PlannedQuantityMt = quantityMt,
            RemainingQuantityMt = quantityMt,
            RequiredDate = start.AddDays(1),
            Priority = 2
        };
        if (dedicatedCampaign)
        {
            // Forces each PO into its own campaign (rather than being merged with other same-grade/
            // route demand into one shared campaign) so the two heats in this test genuinely form two
            // independent cast sequences instead of one continuous tundish sequence.
            po.Requirement = new ProductionOrderRequirement
            {
                ProductionOrderId = po.Id,
                ProductionOrder = po,
                SegregationPolicy = SegregationPolicy.DedicatedCampaign
            };
        }
        return po;
    }
}
