using APS.Application;
using APS.Domain;
using Xunit;

namespace APS.Planning.Tests;

/// <summary>
/// GitHub #19: an infeasible plan used to come back with one sentence naming every constraint family
/// at once, which tells a planner nothing about what to change. Each test here builds a plan that is
/// infeasible for exactly one reason and asserts the solver names that reason - and, where the
/// horizon is at fault, how much more time the plan actually needs.
/// </summary>
public sealed class ScheduleInfeasibilityDiagnosticsTests
{
    private static readonly DateTime Start = new(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Work_that_does_not_fit_the_horizon_is_named_with_the_time_it_is_short_by()
    {
        var mill = Resource("RM-1");
        // Four hours of work against a two-hour horizon on one unary mill.
        var tasks = Enumerable.Range(0, 4)
            .Select(_ => Task(mill.Id, 60))
            .ToArray();

        var result = Solve(tasks, new[] { mill }, horizon: TimeSpan.FromHours(2));

        Assert.False(result.IsFeasible);
        var issue = Assert.Single(result.Issues, x => x.Code == "SCHEDULE_INFEASIBLE_HORIZON");
        // The plan needs until 12:00; the horizon ends at 10:00. The message has to carry the gap,
        // not just the fact that there is one.
        Assert.Contains("2 hours", issue.Message);
        Assert.Contains("12:00", issue.Message);
    }

    [Fact]
    public void Outage_covering_the_whole_horizon_is_named_as_a_calendar_problem()
    {
        var mill = Resource("RM-1");
        var outage = new ResourceCalendar
        {
            ResourceId = mill.Id,
            Start = Start,
            End = Start.AddHours(8),
            IsAvailable = false,
            ReasonCode = "RELINING"
        };

        var result = Solve(
            new[] { Task(mill.Id, 60) },
            new[] { mill },
            horizon: TimeSpan.FromHours(2),
            calendars: new[] { outage });

        Assert.False(result.IsFeasible);
        Assert.Contains(result.Issues, x => x.Code == "SCHEDULE_INFEASIBLE_CALENDAR");
    }

    [Fact]
    public void Queue_window_that_cannot_be_met_is_named_rather_than_blamed_on_capacity()
    {
        var caster = Resource("CCM-1");
        var mill = Resource("RM-1");

        var castTask = Task(caster.Id, 60);
        // The billet needs 30 minutes to physically reach the mill, but the grade's thermal window
        // allows only 10 - the conflict #9's thermal projector produces when a route's transfer time
        // outruns the temperature the heat can hold. No amount of extra horizon or capacity fixes it.
        var rollTask = Task(mill.Id, 60) with
        {
            Dependencies = new[] { new FiniteScheduleDependency(castTask.TaskId, 30, 10) }
        };

        var result = Solve(
            new[] { castTask, rollTask },
            new[] { caster, mill },
            horizon: TimeSpan.FromHours(8));

        Assert.False(result.IsFeasible);
        Assert.Contains(result.Issues, x => x.Code == "SCHEDULE_INFEASIBLE_QUEUE_LIMIT");
    }

    [Fact]
    public void Frozen_time_fence_that_conflicts_with_the_plan_is_named()
    {
        var mill = Resource("RM-1");
        var first = Task(mill.Id, 60);
        var second = Task(mill.Id, 60);

        // Both are frozen onto the same mill at the same instant, which no schedule can honour.
        var fences = new[]
        {
            new FiniteScheduleStabilityConstraint(first.TaskId, TimeFenceZone.Frozen, mill.Id, Start),
            new FiniteScheduleStabilityConstraint(second.TaskId, TimeFenceZone.Frozen, mill.Id, Start)
        };

        var result = Solve(
            new[] { first, second },
            new[] { mill },
            horizon: TimeSpan.FromHours(8),
            stabilityConstraints: fences);

        Assert.False(result.IsFeasible);
        Assert.Contains(result.Issues, x => x.Code == "SCHEDULE_INFEASIBLE_TIME_FENCE");
    }

    [Fact]
    public void Feasible_plan_carries_no_diagnostic_noise()
    {
        var mill = Resource("RM-1");
        var result = Solve(new[] { Task(mill.Id, 60) }, new[] { mill }, horizon: TimeSpan.FromHours(8));

        Assert.True(result.IsFeasible);
        Assert.DoesNotContain(result.Issues, x => x.Code.StartsWith("SCHEDULE_INFEASIBLE", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_causes_at_once_are_reported_as_unresolved_rather_than_guessed_at()
    {
        var mill = Resource("RM-1");
        // Four hours of work, a two-hour horizon, and an outage over the first hour. Lifting either
        // one alone still leaves the plan unsolvable, so no single family is the answer.
        var outage = new ResourceCalendar
        {
            ResourceId = mill.Id,
            Start = Start,
            End = Start.AddHours(1),
            IsAvailable = false,
            ReasonCode = "RELINING"
        };

        var result = Solve(
            Enumerable.Range(0, 4).Select(_ => Task(mill.Id, 60)).ToArray(),
            new[] { mill },
            horizon: TimeSpan.FromHours(2),
            calendars: new[] { outage });

        Assert.False(result.IsFeasible);
        // The horizon probe lifts the horizon far enough that the outage stops mattering, so it still
        // resolves - what must not happen is the calendar being named on its own when lifting it
        // changes nothing.
        Assert.DoesNotContain(result.Issues, x => x.Code == "SCHEDULE_INFEASIBLE_CALENDAR");
        Assert.Contains(result.Issues, x => x.Code == "SCHEDULE_INFEASIBLE_HORIZON");
    }

    private static FiniteScheduleResult Solve(
        IReadOnlyCollection<FiniteScheduleTask> tasks,
        IReadOnlyCollection<Resource> resources,
        TimeSpan horizon,
        IReadOnlyCollection<ResourceCalendar>? calendars = null,
        IReadOnlyCollection<FiniteScheduleStabilityConstraint>? stabilityConstraints = null) =>
        new FiniteScheduleOptimizer().Solve(new FiniteScheduleRequest(
            Start,
            Start + horizon,
            tasks,
            resources,
            calendars ?? Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            5,
            stabilityConstraints));

    private static Resource Resource(string code) => new()
    {
        PlantId = Guid.NewGuid(),
        ProcessStageId = Guid.NewGuid(),
        Code = code,
        Name = code,
        ResourceType = ResourceType.RollingMill
    };

    private static FiniteScheduleTask Task(Guid resourceId, int durationMinutes) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        FiniteScheduleTaskType.HotRolling,
        "Rolling",
        "G1",
        "16MM",
        10m,
        null,
        null,
        0,
        new[] { new FiniteScheduleResourceOption(resourceId, durationMinutes) },
        Array.Empty<FiniteScheduleDependency>());
}
