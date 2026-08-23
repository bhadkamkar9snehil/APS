using APS.Application;
using APS.Domain;
using APS.UI.State;

namespace APS.UI.Components.PlanningWorkbench.Gantt;

public sealed record GanttDropDecision(
    bool IsEligible,
    bool RequiresFrozenOverride,
    string Code,
    string Message);

public static class GanttDragGeometry
{
    public static DateTime CandidateStart(
        DateTime pointerTimeUtc,
        TimeSpan operationDuration,
        double grabRatio,
        GanttSnapMode snapMode,
        IReadOnlyCollection<DateTime> shiftBoundariesUtc)
    {
        var pointer = AsUtc(pointerTimeUtc);
        var offset = TimeSpan.FromTicks((long)Math.Round(
            operationDuration.Ticks * Math.Clamp(grabRatio, 0d, 1d)));
        return Snap(pointer - offset, snapMode, shiftBoundariesUtc);
    }

    public static GanttDropDecision EvaluateDrop(
        ScheduledProcessOperationView operation,
        PlanningOperationWorkbenchDetail? detail,
        Guid targetResourceId,
        bool canEditSchedule,
        DateTime frozenFenceEndUtc)
    {
        if (!canEditSchedule)
            return new GanttDropDecision(false, false, "PLAN_READ_ONLY", "Create a working or recovery scenario before moving operations.");
        if (detail?.ExecutionStatus is OperationExecutionStatus.Running or OperationExecutionStatus.Completed)
            return new GanttDropDecision(false, false, "EXECUTION_STATE_PROTECTED", $"{detail.ExecutionStatus} work cannot be moved.");

        var eligible = targetResourceId == operation.ResourceId ||
                       detail?.ResourceOptions.Any(x => x.ResourceId == targetResourceId) == true;
        if (!eligible)
            return new GanttDropDecision(false, false, "RESOURCE_NOT_ELIGIBLE", "This resource is not eligible for the operation.");

        var requiresOverride = operation.StartUtc <= frozenFenceEndUtc;
        return requiresOverride
            ? new GanttDropDecision(true, true, "FROZEN_OVERRIDE_REQUIRED", "This operation is inside the frozen fence and needs an authorized override.")
            : new GanttDropDecision(true, false, "ELIGIBLE", "Eligible move target.");
    }

    public static IReadOnlyList<PlanningBulkMoveItem> BulkMoveItems(
        IReadOnlyCollection<ScheduledProcessOperationView> operations,
        string anchorPlanningKey,
        DateTime targetAnchorStartUtc)
    {
        if (operations.Count < 2) throw new ArgumentException("A bulk move requires at least two operations.", nameof(operations));
        var anchor = operations.SingleOrDefault(x => x.PlanningKey.Equals(anchorPlanningKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("The bulk-move anchor is not present in the selection.", nameof(anchorPlanningKey));
        var delta = AsUtc(targetAnchorStartUtc) - AsUtc(anchor.StartUtc);
        return operations.Select(operation => new PlanningBulkMoveItem(
            operation.PlanningKey,
            operation.ResourceId,
            AsUtc(operation.StartUtc) + delta)).ToArray();
    }

    // GanttViewportState (the State layer, which also owns GanttSnapMode) is the single source of truth
    // for snap-mode rounding - delegating here keeps the client-visible drag guide and the
    // server-confirmed snap from ever drifting apart, without this Components-layer class depending on
    // its own duplicate copy of the algorithm.
    private static DateTime Snap(
        DateTime timestampUtc,
        GanttSnapMode mode,
        IReadOnlyCollection<DateTime> shiftBoundariesUtc) =>
        GanttViewportState.SnapTimestamp(timestampUtc, mode, shiftBoundariesUtc);

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
