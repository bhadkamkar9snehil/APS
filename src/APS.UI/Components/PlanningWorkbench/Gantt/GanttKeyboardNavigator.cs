using APS.Application;

namespace APS.UI.Components.PlanningWorkbench.Gantt;

public enum GanttKeyboardDirection
{
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    PageUp,
    PageDown
}

public sealed record GanttKeyboardNavigationRequest(string PlanningKey, GanttKeyboardDirection Direction);
public sealed record GanttContextMenuRequest(string PlanningKey, double ClientX, double ClientY, bool FromKeyboard);
public sealed record GanttOperationSelectionRequest(
    ScheduledProcessOperationView Operation,
    bool Toggle,
    bool Extend,
    string? FocusElementId = null);

/// <summary>
/// Element ids a <see cref="GanttOperationSelectionRequest"/> can ask the host page to focus after the
/// inspector opens, shared between the Gantt component that requests the focus and the page component
/// that owns the actual DOM element.
/// </summary>
public static class GanttElementIds
{
    public const string MoveTargetResource = "aps-move-target-resource";
}

public static class GanttKeyboardNavigator
{
    public static string? Next(GanttScene scene, string currentPlanningKey, GanttKeyboardDirection direction)
    {
        var rows = scene.Rows.OrderBy(x => x.SceneIndex).ToArray();
        var currentRowIndex = Array.FindIndex(rows, row => row.Operations.Any(x => KeyEquals(x.Operation.PlanningKey, currentPlanningKey)));
        if (currentRowIndex < 0) return currentPlanningKey;
        var currentRow = rows[currentRowIndex];
        var ordered = currentRow.Operations.OrderBy(x => x.Operation.StartUtc).ToArray();
        var operationIndex = Array.FindIndex(ordered, x => KeyEquals(x.Operation.PlanningKey, currentPlanningKey));
        if (operationIndex < 0) return currentPlanningKey;

        if (direction == GanttKeyboardDirection.Left)
            return operationIndex > 0 ? ordered[operationIndex - 1].Operation.PlanningKey : currentPlanningKey;
        if (direction == GanttKeyboardDirection.Right)
            return operationIndex < ordered.Length - 1 ? ordered[operationIndex + 1].Operation.PlanningKey : currentPlanningKey;
        if (direction == GanttKeyboardDirection.Home) return ordered[0].Operation.PlanningKey;
        if (direction == GanttKeyboardDirection.End) return ordered[^1].Operation.PlanningKey;

        var targetRowIndex = direction switch
        {
            GanttKeyboardDirection.Up => currentRowIndex - 1,
            GanttKeyboardDirection.Down => currentRowIndex + 1,
            GanttKeyboardDirection.PageUp => currentRowIndex - 5,
            GanttKeyboardDirection.PageDown => currentRowIndex + 5,
            _ => currentRowIndex
        };
        targetRowIndex = Math.Clamp(targetRowIndex, 0, rows.Length - 1);
        if (rows[targetRowIndex].Operations.Count == 0)
            return currentPlanningKey;
        var currentStart = ordered[operationIndex].Operation.StartUtc;
        return rows[targetRowIndex].Operations
            .OrderBy(x => Math.Abs((x.Operation.StartUtc - currentStart).Ticks))
            .ThenBy(x => x.Operation.StartUtc)
            .First()
            .Operation.PlanningKey;
    }

    private static bool KeyEquals(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);
}
