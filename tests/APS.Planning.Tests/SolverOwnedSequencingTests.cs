using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

public sealed class SolverOwnedSequencingTests
{
    [Fact]
    public void Independent_resource_circuits_allow_parallel_casters_and_mills()
    {
        var start = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);
        var ccm1 = Resource("CCM-1", ResourceType.Caster);
        var ccm2 = Resource("CCM-2", ResourceType.Caster);
        var mill1 = Resource("RM-1", ResourceType.RollingMill);
        var mill2 = Resource("RM-2", ResourceType.RollingMill);

        var tasks = new[]
        {
            ScheduleTask(Guid.NewGuid(), ccm1.Id, FiniteScheduleTaskType.Casting, "G1", "150X150", 30),
            ScheduleTask(Guid.NewGuid(), ccm1.Id, FiniteScheduleTaskType.Casting, "G1", "150X150", 30),
            ScheduleTask(Guid.NewGuid(), ccm2.Id, FiniteScheduleTaskType.Casting, "G1", "150X150", 30),
            ScheduleTask(Guid.NewGuid(), ccm2.Id, FiniteScheduleTaskType.Casting, "G1", "150X150", 30),
            ScheduleTask(Guid.NewGuid(), mill1.Id, FiniteScheduleTaskType.HotRolling, "G1", "16MM", 30),
            ScheduleTask(Guid.NewGuid(), mill1.Id, FiniteScheduleTaskType.HotRolling, "G1", "16MM", 30),
            ScheduleTask(Guid.NewGuid(), mill2.Id, FiniteScheduleTaskType.HotRolling, "G1", "16MM", 30),
            ScheduleTask(Guid.NewGuid(), mill2.Id, FiniteScheduleTaskType.HotRolling, "G1", "16MM", 30)
        };

        var result = new FiniteScheduleOptimizer().Solve(new FiniteScheduleRequest(
            start,
            start.AddHours(4),
            tasks,
            new[] { ccm1, ccm2, mill1, mill2 },
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            5));

        Assert.True(result.IsFeasible, string.Join("; ", result.Issues.Select(issue => issue.Message)));

        Assert.Equal(start, FirstStart(result, ccm1.Id));
        Assert.Equal(start, FirstStart(result, ccm2.Id));
        Assert.Equal(start, FirstStart(result, mill1.Id));
        Assert.Equal(start, FirstStart(result, mill2.Id));

        Assert.Equal(2, result.Assignments.Count(assignment => assignment.ResourceId == ccm1.Id));
        Assert.Equal(2, result.Assignments.Count(assignment => assignment.ResourceId == ccm2.Id));
        Assert.Equal(2, result.Assignments.Count(assignment => assignment.ResourceId == mill1.Id));
        Assert.Equal(2, result.Assignments.Count(assignment => assignment.ResourceId == mill2.Id));
    }

    [Fact]
    public void Same_source_feed_siblings_can_be_interleaved_by_another_plan_when_material_readiness_requires_it()
    {
        var start = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);
        var mill = Resource("RM-1", ResourceType.RollingMill);
        var feedSource = Guid.NewGuid();

        var delayedFeed = ScheduleTask(
            feedSource,
            mill.Id,
            FiniteScheduleTaskType.HotRolling,
            "G1",
            "16MM",
            10,
            earliestStartUtc: start.AddMinutes(30));
        var immediatelyAvailableFeed = ScheduleTask(
            feedSource,
            mill.Id,
            FiniteScheduleTaskType.HotRolling,
            "G1",
            "16MM",
            10,
            dueUtc: start.AddMinutes(10),
            priority: 2);
        var otherPlan = ScheduleTask(
            Guid.NewGuid(),
            mill.Id,
            FiniteScheduleTaskType.HotRolling,
            "G1",
            "16MM",
            10,
            dueUtc: start.AddMinutes(20),
            priority: 2);

        var result = new FiniteScheduleOptimizer().Solve(new FiniteScheduleRequest(
            start,
            start.AddHours(2),
            new[] { delayedFeed, immediatelyAvailableFeed, otherPlan },
            new[] { mill },
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            5));

        Assert.True(result.IsFeasible, string.Join("; ", result.Issues.Select(issue => issue.Message)));
        Assert.Equal(
            start,
            Assert.Single(result.Assignments, assignment => assignment.TaskId == immediatelyAvailableFeed.TaskId).StartUtc);
        Assert.Equal(
            start.AddMinutes(10),
            Assert.Single(result.Assignments, assignment => assignment.TaskId == otherPlan.TaskId).StartUtc);
        Assert.Equal(
            start.AddMinutes(30),
            Assert.Single(result.Assignments, assignment => assignment.TaskId == delayedFeed.TaskId).StartUtc);
    }

    [Fact]
    public void Setup_time_and_penalty_are_charged_only_for_selected_adjacent_distinct_plans()
    {
        var start = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);
        var mill = Resource("RM-1", ResourceType.RollingMill);
        var taskA = ScheduleTask(Guid.NewGuid(), mill.Id, FiniteScheduleTaskType.HotRolling, "G1", "A", 10);
        var taskB = ScheduleTask(Guid.NewGuid(), mill.Id, FiniteScheduleTaskType.HotRolling, "G1", "B", 10);
        var taskC = ScheduleTask(Guid.NewGuid(), mill.Id, FiniteScheduleTaskType.HotRolling, "G1", "C", 10);
        var transitions = DirectionalSectionTransitions(mill.Id, new[] { "A", "B", "C" }, 15, 7);

        var result = new FiniteScheduleOptimizer().Solve(new FiniteScheduleRequest(
            start,
            start.AddHours(4),
            new[] { taskA, taskB, taskC },
            new[] { mill },
            Array.Empty<ResourceCalendar>(),
            transitions,
            5));

        Assert.True(result.IsFeasible, string.Join("; ", result.Issues.Select(issue => issue.Message)));

        var ordered = result.Assignments.OrderBy(assignment => assignment.StartUtc).ToArray();
        Assert.Equal(3, ordered.Length);
        Assert.Equal(TimeSpan.FromMinutes(15), ordered[1].StartUtc - ordered[0].EndUtc);
        Assert.Equal(TimeSpan.FromMinutes(15), ordered[2].StartUtc - ordered[1].EndUtc);

        // 30 process minutes + two selected 15-minute adjacencies = 60-minute makespan.
        // Only those two adjacency literals carry the 7-point transition penalty: 60 + 14 = 74.
        Assert.Equal(74L, result.ObjectiveValue);
    }

    [Fact]
    public void Forbidden_directional_transition_is_not_available_as_a_machine_adjacency()
    {
        var start = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);
        var mill = Resource("RM-1", ResourceType.RollingMill);
        var taskA = ScheduleTask(Guid.NewGuid(), mill.Id, FiniteScheduleTaskType.HotRolling, "G1", "A", 10);
        var taskB = ScheduleTask(Guid.NewGuid(), mill.Id, FiniteScheduleTaskType.HotRolling, "G1", "B", 10);
        var transitions = new[]
        {
            new TransitionRule
            {
                ResourceId = mill.Id,
                Dimension = TransitionDimension.CrossSection,
                FromCode = "A",
                ToCode = "B",
                IsAllowed = false
            },
            new TransitionRule
            {
                ResourceId = mill.Id,
                Dimension = TransitionDimension.CrossSection,
                FromCode = "B",
                ToCode = "A",
                IsAllowed = true
            }
        };

        var result = new FiniteScheduleOptimizer().Solve(new FiniteScheduleRequest(
            start,
            start.AddHours(2),
            new[] { taskA, taskB },
            new[] { mill },
            Array.Empty<ResourceCalendar>(),
            transitions,
            5));

        Assert.True(result.IsFeasible, string.Join("; ", result.Issues.Select(issue => issue.Message)));
        var ordered = result.Assignments.OrderBy(assignment => assignment.StartUtc).ToArray();
        Assert.Equal(taskB.TaskId, ordered[0].TaskId);
        Assert.Equal(taskA.TaskId, ordered[1].TaskId);
    }

    private static DateTime FirstStart(FiniteScheduleResult result, Guid resourceId) =>
        result.Assignments
            .Where(assignment => assignment.ResourceId == resourceId)
            .Min(assignment => assignment.StartUtc);

    private static Resource Resource(string code, ResourceType type) => new()
    {
        PlantId = Guid.NewGuid(),
        ProcessStageId = Guid.NewGuid(),
        Code = code,
        Name = code,
        ResourceType = type
    };

    private static FiniteScheduleTask ScheduleTask(
        Guid sourceEntityId,
        Guid resourceId,
        FiniteScheduleTaskType taskType,
        string grade,
        string section,
        int durationMinutes,
        DateTime? earliestStartUtc = null,
        DateTime? dueUtc = null,
        int priority = 0) =>
        new(
            Guid.NewGuid(),
            sourceEntityId,
            taskType,
            $"{taskType} {grade}/{section}",
            grade,
            section,
            10m,
            earliestStartUtc,
            dueUtc,
            priority,
            new[] { new FiniteScheduleResourceOption(resourceId, durationMinutes) },
            Array.Empty<FiniteScheduleDependency>());

    private static IReadOnlyCollection<TransitionRule> DirectionalSectionTransitions(
        Guid resourceId,
        IReadOnlyCollection<string> sections,
        int transitionMinutes,
        int penalty)
    {
        var rules = new List<TransitionRule>();
        foreach (var from in sections)
        {
            foreach (var to in sections)
            {
                if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) continue;
                rules.Add(new TransitionRule
                {
                    ResourceId = resourceId,
                    Dimension = TransitionDimension.CrossSection,
                    FromCode = from,
                    ToCode = to,
                    IsAllowed = true,
                    Penalty = penalty,
                    TransitionTime = TimeSpan.FromMinutes(transitionMinutes)
                });
            }
        }

        return rules;
    }
}