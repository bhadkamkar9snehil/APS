using APS.Application;
using APS.Domain;
using APS.UI.Components.PlanningWorkbench.Gantt;
using APS.UI.State;

namespace APS.UI.Tests;

public sealed class GanttDragGeometryTests
{
    private static readonly DateTime Start = new(2026, 8, 21, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0d, 10, 0)]
    [InlineData(.5d, 9, 10)]
    [InlineData(.7d, 8, 50)]
    public void Candidate_start_preserves_the_pointer_grab_offset(double grabRatio, int expectedHour, int expectedMinute)
    {
        var pointerTime = Start.AddHours(2);

        var candidate = GanttDragGeometry.CandidateStart(
            pointerTime,
            TimeSpan.FromMinutes(100),
            grabRatio,
            GanttSnapMode.Free,
            Array.Empty<DateTime>());

        Assert.Equal(new DateTime(2026, 8, 21, expectedHour, expectedMinute, 0, DateTimeKind.Utc), candidate);
    }

    [Theory]
    [InlineData(GanttSnapMode.Hour, 10, 0)]
    [InlineData(GanttSnapMode.ThirtyMinutes, 10, 30)]
    [InlineData(GanttSnapMode.FifteenMinutes, 10, 30)]
    [InlineData(GanttSnapMode.FiveMinutes, 10, 25)]
    [InlineData(GanttSnapMode.Free, 10, 23)]
    public void Candidate_start_applies_every_time_grid_snap_mode(GanttSnapMode mode, int hour, int minute)
    {
        var candidate = GanttDragGeometry.CandidateStart(
            Start.AddHours(2).AddMinutes(23),
            TimeSpan.FromHours(1),
            0d,
            mode,
            Array.Empty<DateTime>());

        Assert.Equal(new DateTime(2026, 8, 21, hour, minute, 0, DateTimeKind.Utc), candidate);
    }

    [Fact]
    public void Shift_snap_uses_only_supplied_authoritative_boundaries()
    {
        var boundaries = new[] { Start.AddHours(6), Start.AddHours(14) };

        var candidate = GanttDragGeometry.CandidateStart(
            Start.AddHours(10),
            TimeSpan.FromHours(1),
            0d,
            GanttSnapMode.ShiftBoundary,
            boundaries);

        Assert.Equal(Start.AddHours(6), candidate);
    }

    [Theory]
    [InlineData(OperationExecutionStatus.Running)]
    [InlineData(OperationExecutionStatus.Completed)]
    public void Running_and_completed_operations_are_protected(OperationExecutionStatus executionStatus)
    {
        var operation = Operation();
        var detail = Detail(operation, executionStatus, operation.ResourceId);

        var decision = GanttDragGeometry.EvaluateDrop(
            operation,
            detail,
            operation.ResourceId,
            canEditSchedule: true,
            frozenFenceEndUtc: Start.AddMinutes(-1));

        Assert.False(decision.IsEligible);
        Assert.Equal("EXECUTION_STATE_PROTECTED", decision.Code);
    }

    [Fact]
    public void Ineligible_lane_is_rejected_before_server_validation()
    {
        var operation = Operation();
        var detail = Detail(operation, OperationExecutionStatus.Planned, Guid.NewGuid());

        var decision = GanttDragGeometry.EvaluateDrop(
            operation,
            detail,
            Guid.NewGuid(),
            canEditSchedule: true,
            frozenFenceEndUtc: Start.AddMinutes(-1));

        Assert.False(decision.IsEligible);
        Assert.Equal("RESOURCE_NOT_ELIGIBLE", decision.Code);
    }

    [Fact]
    public void Frozen_operation_exposes_override_policy_without_faking_ineligibility()
    {
        var operation = Operation();
        var detail = Detail(operation, OperationExecutionStatus.Planned, operation.ResourceId);

        var decision = GanttDragGeometry.EvaluateDrop(
            operation,
            detail,
            operation.ResourceId,
            canEditSchedule: true,
            frozenFenceEndUtc: operation.StartUtc.AddMinutes(30));

        Assert.True(decision.IsEligible);
        Assert.True(decision.RequiresFrozenOverride);
        Assert.Equal("FROZEN_OVERRIDE_REQUIRED", decision.Code);
    }

    [Fact]
    public void Bulk_horizontal_move_preserves_every_relative_offset_and_resource_assignment()
    {
        var first = Operation();
        var second = Operation() with
        {
            PlanningKey = "OP-02",
            ResourceId = Guid.NewGuid(),
            StartUtc = first.StartUtc.AddHours(2),
            EndUtc = first.EndUtc.AddHours(2)
        };

        var moves = GanttDragGeometry.BulkMoveItems([first, second], first.PlanningKey, first.StartUtc.AddHours(5));

        Assert.Equal(2, moves.Count);
        Assert.Equal(first.StartUtc.AddHours(5), moves[0].TargetStartUtc);
        Assert.Equal(second.StartUtc.AddHours(5), moves[1].TargetStartUtc);
        Assert.Equal(first.ResourceId, moves[0].TargetResourceId);
        Assert.Equal(second.ResourceId, moves[1].TargetResourceId);
        Assert.Equal(second.StartUtc - first.StartUtc, moves[1].TargetStartUtc - moves[0].TargetStartUtc);
    }

    private static ScheduledProcessOperationView Operation() => new(
        Guid.NewGuid(), "OP-01", Guid.NewGuid(), ProcessOperationType.Eaf, Guid.NewGuid(), "EAF-01", "EAF 01",
        ProcessUnitType.Eaf, ResourceOperatingState.Available, Start, Start.AddMinutes(100), 70m, "SAE1008", "BLT-150");

    private static PlanningOperationWorkbenchDetail Detail(
        ScheduledProcessOperationView operation,
        OperationExecutionStatus executionStatus,
        Guid eligibleResourceId) => new(
        operation.OperationSnapshotId,
        operation.PlanningKey,
        operation.SourceEntityId,
        OperationAssignmentCommitmentState.Flexible,
        executionStatus,
        null,
        null,
        0m,
        Array.Empty<string>(),
        [new PlanningOperationResourceOptionView(eligibleResourceId, "ELIGIBLE", "Eligible", 100, 0, true, null)],
        null,
        null,
        Array.Empty<string>());
}
