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
        var resource = new Resource
        {
            PlantId = Guid.NewGuid(),
            ProcessStageId = Guid.NewGuid(),
            Code = "RM-1",
            Name = "RM-1",
            ResourceType = ResourceType.RollingMill,
            ProcessUnitType = ProcessUnitType.HotRollingMill,
            IsActive = true
        };
        var taskA = new FiniteScheduleTask(
            Guid.NewGuid(), Guid.NewGuid(), FiniteScheduleTaskType.HotRolling,
            "Shared block A", "G1", "16MM", 100m,
            null, start.AddMinutes(60), 0,
            new[] { new FiniteScheduleResourceOption(resource.Id, 60) },
            Array.Empty<FiniteScheduleDependency>(),
            ProcessOperationType.HotRoll);
        var taskB = new FiniteScheduleTask(
            Guid.NewGuid(), Guid.NewGuid(), FiniteScheduleTaskType.HotRolling,
            "Block B", "G1", "16MM", 100m,
            null, start.AddMinutes(90), 0,
            new[] { new FiniteScheduleResourceOption(resource.Id, 60) },
            Array.Empty<FiniteScheduleDependency>(),
            ProcessOperationType.HotRoll);

        var poAEarly = Guid.NewGuid();
        var poALate = Guid.NewGuid();
        var poB = Guid.NewGuid();
        var request = new FiniteScheduleRequest(
            start,
            start.AddMinutes(300),
            new[] { taskA, taskB },
            new[] { resource },
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            MaxSolverSeconds: 5,
            ServiceObligations: new[]
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
}
