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

    private static DateTime Snap(
        DateTime timestampUtc,
        GanttSnapMode mode,
        IReadOnlyCollection<DateTime> shiftBoundariesUtc)
    {
        if (mode == GanttSnapMode.Free) return timestampUtc;
        if (mode == GanttSnapMode.ShiftBoundary)
        {
            if (shiftBoundariesUtc.Count == 0) return timestampUtc;
            return shiftBoundariesUtc
                .Select(AsUtc)
                .OrderBy(x => Math.Abs(x.Ticks - timestampUtc.Ticks))
                .ThenBy(x => x)
                .First();
        }

        var increment = mode switch
        {
            GanttSnapMode.Hour => TimeSpan.FromHours(1),
            GanttSnapMode.ThirtyMinutes => TimeSpan.FromMinutes(30),
            GanttSnapMode.FifteenMinutes => TimeSpan.FromMinutes(15),
            GanttSnapMode.FiveMinutes => TimeSpan.FromMinutes(5),
            _ => TimeSpan.Zero
        };
        if (increment <= TimeSpan.Zero) return timestampUtc;
        var dayStart = timestampUtc.Date;
        var steps = decimal.Round(
            (decimal)(timestampUtc.Ticks - dayStart.Ticks) / increment.Ticks,
            0,
            MidpointRounding.AwayFromZero);
        return new DateTime(
            dayStart.Ticks + decimal.ToInt64(steps * increment.Ticks),
            DateTimeKind.Utc);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
