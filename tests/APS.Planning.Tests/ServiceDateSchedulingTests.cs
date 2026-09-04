using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

public sealed class ServiceDateSchedulingTests
{
    [Fact]
    public void Small_early_allocation_does_not_make_entire_shared_task_due_early()
    {
        var start = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var resource = Resource();
        var taskA = Task(resource.Id, "Shared block A", 100m, start.AddMinutes(60));
        var taskB = Task(resource.Id, "Block B", 100m, start.AddMinutes(90));

        var poAEarly = Guid.NewGuid();
        var poALate = Guid.NewGuid();
        var poB = Guid.NewGuid();
        var request = Request(
            start,
            resource,
            new[] { taskA, taskB },
            new[]
            {
                // Only 10 MT of A is actually required at the early date; 90 MT is comfortably later.
                new FiniteScheduleServiceObligation(taskA.TaskId, poAEarly, 10m, start.AddMinutes(60), 0),
                new FiniteScheduleServiceObligation(taskA.TaskId, poALate, 90m, start.AddMinutes(300), 0),
                new FiniteScheduleServiceObligation(taskB.TaskId, poB, 100m, start.AddMinutes(90), 0)
            });

        var result = new FiniteScheduleOptimizer().Solve(request);

        Assert.True(result.IsFeasible, string.Join("; ", result.Issues.Select(x => x.Message)));
        var a = result.Assignments.Single(x => x.TaskId == taskA.TaskId);
        var b = result.Assignments.Single(x => x.TaskId == taskB.TaskId);

        // Quantity-aware obligations choose B first. The old one-date-per-task model would treat all 100 MT
        // of A as due at +60 and therefore pull A ahead of B.
        Assert.Equal(start, b.StartUtc);
        Assert.Equal(start.AddMinutes(60), a.StartUtc);
    }

    [Fact]
    public void Higher_priority_order_wins_equal_due_date_contention()
    {
        var start = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var resource = Resource();
        var normal = Task(resource.Id, "Normal order", 50m, start.AddMinutes(60));
        var rush = Task(resource.Id, "Rush order", 50m, start.AddMinutes(60));
        var due = start.AddMinutes(60);
        var request = Request(
            start,
            resource,
            new[] { normal, rush },
            new[]
            {
                new FiniteScheduleServiceObligation(normal.TaskId, Guid.NewGuid(), 50m, due, 0),
                new FiniteScheduleServiceObligation(rush.TaskId, Guid.NewGuid(), 50m, due, 10)
            });

        var result = new FiniteScheduleOptimizer().Solve(request);

        Assert.True(result.IsFeasible, string.Join("; ", result.Issues.Select(x => x.Message)));
        var rushAssignment = result.Assignments.Single(x => x.TaskId == rush.TaskId);
        var normalAssignment = result.Assignments.Single(x => x.TaskId == normal.TaskId);
        Assert.Equal(start, rushAssignment.StartUtc);
        Assert.Equal(start.AddMinutes(60), normalAssignment.StartUtc);
    }

    private static Resource Resource() => new()
    {
        PlantId = Guid.NewGuid(),
        ProcessStageId = Guid.NewGuid(),
        Code = "RM-1",
        Name = "RM-1",
        ResourceType = ResourceType.RollingMill,
        ProcessUnitType = ProcessUnitType.HotRollingMill,
        IsActive = true
    };

    private static FiniteScheduleTask Task(Guid resourceId, string name, decimal quantityMt, DateTime dueUtc) =>
        new(
            Guid.NewGuid(), Guid.NewGuid(), FiniteScheduleTaskType.HotRolling,
            name, "G1", "16MM", quantityMt,
            null, dueUtc, 0,
            new[] { new FiniteScheduleResourceOption(resourceId, 60) },
            Array.Empty<FiniteScheduleDependency>(),
            ProcessOperationType.HotRoll);

    private static FiniteScheduleRequest Request(
        DateTime start,
        Resource resource,
        IReadOnlyCollection<FiniteScheduleTask> tasks,
        IReadOnlyCollection<FiniteScheduleServiceObligation> obligations) =>
        new(
            start,
            start.AddMinutes(300),
            tasks,
            new[] { resource },
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            MaxSolverSeconds: 5,
            ServiceObligations: obligations);
}
