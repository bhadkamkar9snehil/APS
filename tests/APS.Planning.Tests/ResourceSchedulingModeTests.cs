using APS.Application;
using APS.Domain;
using Xunit;

namespace APS.Planning.Tests;

/// <summary>
/// #35 - a resource's scheduling mode is master data describing physical behaviour, not a solver
/// preference. These cover the two supported modes and the boundary between them.
/// </summary>
public sealed class ResourceSchedulingModeTests
{
    private static readonly DateTime Start = new(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Disjunctive_resource_still_serializes_every_block()
    {
        var mill = Disjunctive("RM-1", ResourceType.RollingMill);
        var tasks = Enumerable.Range(0, 3)
            .Select(_ => Task(mill.Id, FiniteScheduleTaskType.HotRolling, 60))
            .ToArray();

        var result = Solve(tasks, new[] { mill });

        Assert.True(result.IsFeasible, Messages(result));
        // Three 60-minute blocks on one unary machine can only occupy three separate hours.
        Assert.Equal(3, result.Assignments.Select(x => x.StartUtc).Distinct().Count());
        AssertNoOverlap(result, mill.Id);
    }

    [Fact]
    public void Separate_physical_resources_stay_fully_parallel_in_both_modes()
    {
        var furnaceA = Cumulative("RHF-1", capacity: 3m);
        var furnaceB = Cumulative("RHF-2", capacity: 3m);
        var millA = Disjunctive("RM-1", ResourceType.RollingMill);
        var millB = Disjunctive("RM-2", ResourceType.RollingMill);

        var tasks = new[]
        {
            Task(furnaceA.Id, FiniteScheduleTaskType.Reheating, 60),
            Task(furnaceB.Id, FiniteScheduleTaskType.Reheating, 60),
            Task(millA.Id, FiniteScheduleTaskType.HotRolling, 60),
            Task(millB.Id, FiniteScheduleTaskType.HotRolling, 60)
        };

        var result = Solve(tasks, new[] { furnaceA, furnaceB, millA, millB });

        Assert.True(result.IsFeasible, Messages(result));
        // Nothing shares a resource, so every task can start at the horizon start.
        Assert.All(result.Assignments, assignment => Assert.Equal(Start, assignment.StartUtc));
    }

    [Fact]
    public void Cumulative_reheating_furnace_holds_several_blocks_at_once()
    {
        var furnace = Cumulative("RHF-1", capacity: 3m);
        var tasks = Enumerable.Range(0, 3)
            .Select(_ => Task(furnace.Id, FiniteScheduleTaskType.Reheating, 60))
            .ToArray();

        var result = Solve(tasks, new[] { furnace });

        Assert.True(result.IsFeasible, Messages(result));
        // Capacity 3 means all three blocks reside in the furnace over the same hour. Under the old
        // universal NoOverlap this took three hours.
        Assert.All(result.Assignments, assignment => Assert.Equal(Start, assignment.StartUtc));
    }

    [Fact]
    public void One_block_beyond_capacity_cannot_overlap_the_others()
    {
        var furnace = Cumulative("RHF-1", capacity: 3m);
        var tasks = Enumerable.Range(0, 4)
            .Select(_ => Task(furnace.Id, FiniteScheduleTaskType.Reheating, 60))
            .ToArray();

        var result = Solve(tasks, new[] { furnace });

        Assert.True(result.IsFeasible, Messages(result));
        // The fourth block is real capacity pressure, not an artificial serialization: three overlap
        // and exactly one is pushed out.
        Assert.Equal(3, result.Assignments.Count(x => x.StartUtc == Start));
        Assert.Equal(1, result.Assignments.Count(x => x.StartUtc >= Start.AddMinutes(60)));
    }

    [Fact]
    public void Mass_equivalent_basis_charges_tonnage_rather_than_a_slot()
    {
        var bed = Cumulative("COOL-1", capacity: 100m, basis: ResourceCapacityBasis.MassEquivalentMt);
        // 3 x 40 MT against a 100 MT bed: any two fit, all three do not.
        var tasks = Enumerable.Range(0, 3)
            .Select(_ => Task(bed.Id, FiniteScheduleTaskType.Finishing, 60, quantityMt: 40m))
            .ToArray();

        var result = Solve(tasks, new[] { bed });

        Assert.True(result.IsFeasible, Messages(result));
        Assert.Equal(2, result.Assignments.Count(x => x.StartUtc == Start));
    }

    [Fact]
    public void Capacity_derating_reduces_concurrent_load_during_the_derated_window()
    {
        var furnace = Cumulative("RHF-1", capacity: 3m);
        // Available, but two thirds of the furnace for the first hour.
        var derate = new ResourceCalendar
        {
            ResourceId = furnace.Id,
            Start = Start,
            End = Start.AddMinutes(60),
            IsAvailable = true,
            CapacityFactorPct = 67m,
            ReasonCode = "BURNER_OUT"
        };

        var tasks = Enumerable.Range(0, 3)
            .Select(_ => Task(furnace.Id, FiniteScheduleTaskType.Reheating, 60))
            .ToArray();

        var result = Solve(tasks, new[] { furnace }, new[] { derate });

        Assert.True(result.IsFeasible, Messages(result));
        // Exactly two of three fit while derated - one fewer than the undated furnace holds, one more
        // than a serialized resource would, so this pins the derate rather than either extreme.
        Assert.Equal(2, result.Assignments.Count(x => x.StartUtc == Start));
    }

    [Fact]
    public void Outage_calendar_blocks_a_cumulative_resource_completely()
    {
        var furnace = Cumulative("RHF-1", capacity: 3m);
        var outage = new ResourceCalendar
        {
            ResourceId = furnace.Id,
            Start = Start,
            End = Start.AddMinutes(120),
            IsAvailable = false,
            ReasonCode = "RELINING"
        };

        var tasks = Enumerable.Range(0, 2)
            .Select(_ => Task(furnace.Id, FiniteScheduleTaskType.Reheating, 60))
            .ToArray();

        var result = Solve(tasks, new[] { furnace }, new[] { outage });

        Assert.True(result.IsFeasible, Messages(result));
        // No residual capacity during an outage, however large the furnace is...
        Assert.All(result.Assignments, assignment => Assert.True(assignment.StartUtc >= Start.AddMinutes(120)));
        // ...but the furnace is still cumulative once it is back, not serialized by the outage.
        Assert.Equal(2, result.Assignments.Count(x => x.StartUtc == Start.AddMinutes(120)));
    }

    [Fact]
    public void Cumulative_resource_is_not_serialized_by_sequence_transition_rules()
    {
        var furnace = Cumulative("RHF-1", capacity: 3m);
        // A grade transition rule that would force a two-hour changeover between adjacent jobs. On a
        // disjunctive resource this orders them; on a residence unit there is no "adjacent job".
        var rules = new[]
        {
            Transition(furnace.Id, "G1", "G2"),
            Transition(furnace.Id, "G2", "G1")
        };

        var tasks = new[]
        {
            Task(furnace.Id, FiniteScheduleTaskType.Reheating, 60, grade: "G1"),
            Task(furnace.Id, FiniteScheduleTaskType.Reheating, 60, grade: "G2")
        };

        var result = Solve(tasks, new[] { furnace }, transitionRules: rules);

        Assert.True(result.IsFeasible, Messages(result));
        Assert.All(result.Assignments, assignment => Assert.Equal(Start, assignment.StartUtc));
    }

    [Fact]
    public void Cumulative_resource_without_capacity_is_a_master_data_error()
    {
        var furnace = Disjunctive("RHF-1", ResourceType.Furnace);
        furnace.SchedulingMode = ResourceSchedulingMode.Cumulative;
        furnace.CapacityBasis = ResourceCapacityBasis.Slots;
        furnace.NominalConcurrentCapacity = null;

        var result = Solve(new[] { Task(furnace.Id, FiniteScheduleTaskType.Reheating, 60) }, new[] { furnace });

        Assert.False(result.IsFeasible);
        // Never silently treat a mis-configured cumulative resource as unconstrained.
        Assert.Contains(result.Issues, issue => issue.Code == "RESOURCE_CUMULATIVE_CAPACITY_MISSING");
    }

    [Fact]
    public void Task_larger_than_the_resource_falls_back_to_an_eligible_alternative()
    {
        var small = Cumulative("COOL-SMALL", capacity: 20m, basis: ResourceCapacityBasis.MassEquivalentMt);
        var large = Cumulative("COOL-LARGE", capacity: 100m, basis: ResourceCapacityBasis.MassEquivalentMt);

        var task = new FiniteScheduleTask(
            Guid.NewGuid(),
            Guid.NewGuid(),
            FiniteScheduleTaskType.Finishing,
            "Cool 60MT",
            "G1",
            "16MM",
            60m,
            null,
            null,
            0,
            new[]
            {
                new FiniteScheduleResourceOption(small.Id, 60),
                new FiniteScheduleResourceOption(large.Id, 60)
            },
            Array.Empty<FiniteScheduleDependency>());

        var result = Solve(new[] { task }, new[] { small, large });

        Assert.True(result.IsFeasible, Messages(result));
        Assert.Equal(large.Id, Assert.Single(result.Assignments).ResourceId);
        Assert.Contains(result.Issues, issue => issue.Code == "TASK_DEMAND_EXCEEDS_RESOURCE_CAPACITY");
    }

    [Fact]
    public void Utilization_of_a_cumulative_resource_is_wall_clock_occupancy_not_summed_durations()
    {
        var hour = new[]
        {
            (Start, Start.AddHours(1)),
            (Start, Start.AddHours(1)),
            (Start, Start.AddHours(1))
        };

        // Three concurrent one-hour blocks are three hours of work content but one hour of occupancy.
        // Reporting the former as utilization is what makes a cumulative resource look overloaded.
        Assert.Equal(1d, ResourceCapacityModel.OccupiedHours(hour), 3);
        Assert.Equal(3, ResourceCapacityModel.PeakConcurrency(hour));
    }

    [Fact]
    public void Occupancy_merges_overlaps_and_leaves_idle_gaps_out()
    {
        var spans = new[]
        {
            (Start, Start.AddHours(2)),
            (Start.AddHours(1), Start.AddHours(3)),   // overlaps the first, counted once
            (Start.AddHours(5), Start.AddHours(6))    // after a two-hour idle gap
        };

        Assert.Equal(4d, ResourceCapacityModel.OccupiedHours(spans), 3);
        Assert.Equal(2, ResourceCapacityModel.PeakConcurrency(spans));
    }

    [Fact]
    public void Back_to_back_work_on_a_disjunctive_resource_never_reads_as_concurrent()
    {
        var spans = new[]
        {
            (Start, Start.AddHours(1)),
            (Start.AddHours(1), Start.AddHours(2))
        };

        Assert.Equal(2d, ResourceCapacityModel.OccupiedHours(spans), 3);
        // Touching at an instant is a handover, not two blocks in the machine at once.
        Assert.Equal(1, ResourceCapacityModel.PeakConcurrency(spans));
    }

    private static FiniteScheduleResult Solve(
        IReadOnlyCollection<FiniteScheduleTask> tasks,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyCollection<ResourceCalendar>? calendars = null,
        IReadOnlyCollection<TransitionRule>? transitionRules = null) =>
        new FiniteScheduleOptimizer().Solve(new FiniteScheduleRequest(
            Start,
            Start.AddHours(12),
            tasks,
            resources,
            calendars ?? Array.Empty<ResourceCalendar>(),
            transitionRules ?? Array.Empty<TransitionRule>(),
            10));

    private static Resource Disjunctive(string code, ResourceType type) => new()
    {
        PlantId = Guid.NewGuid(),
        ProcessStageId = Guid.NewGuid(),
        Code = code,
        Name = code,
        ResourceType = type
    };

    private static Resource Cumulative(
        string code,
        decimal capacity,
        ResourceCapacityBasis basis = ResourceCapacityBasis.Slots)
    {
        var resource = Disjunctive(code, ResourceType.Furnace);
        resource.ProcessUnitType = ProcessUnitType.ReheatingFurnace;
        resource.SchedulingMode = ResourceSchedulingMode.Cumulative;
        resource.CapacityBasis = basis;
        resource.NominalConcurrentCapacity = capacity;
        resource.AppliesSequenceRules = false;
        return resource;
    }

    private static FiniteScheduleTask Task(
        Guid resourceId,
        FiniteScheduleTaskType taskType,
        int durationMinutes,
        string grade = "G1",
        decimal quantityMt = 10m) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            taskType,
            $"{taskType} {grade}",
            grade,
            "16MM",
            quantityMt,
            null,
            null,
            0,
            new[] { new FiniteScheduleResourceOption(resourceId, durationMinutes) },
            Array.Empty<FiniteScheduleDependency>());

    private static TransitionRule Transition(Guid resourceId, string from, string to) => new()
    {
        ResourceId = resourceId,
        Dimension = TransitionDimension.Grade,
        FromCode = from,
        ToCode = to,
        IsAllowed = true,
        TransitionTime = TimeSpan.FromMinutes(120)
    };

    private static void AssertNoOverlap(FiniteScheduleResult result, Guid resourceId)
    {
        var ordered = result.Assignments
            .Where(x => x.ResourceId == resourceId)
            .OrderBy(x => x.StartUtc)
            .ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            Assert.True(ordered[i].StartUtc >= ordered[i - 1].EndUtc);
        }
    }

    private static string Messages(FiniteScheduleResult result) =>
        string.Join("; ", result.Issues.Select(issue => issue.Message));
}
