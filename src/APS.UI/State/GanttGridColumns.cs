namespace APS.UI.State;

public enum GanttGridColumn
{
    Resource,
    State,
    Busy,
    Load,
    Operations,
    Next,
    Exceptions
}

public enum GanttGridSortColumn
{
    Canonical,
    Resource,
    State,
    Busy,
    Load,
    Operations,
    Next,
    Exceptions
}

public sealed record GanttGridColumnDefinition(
    GanttGridColumn Column,
    string Key,
    string Label,
    double DefaultWidthPx,
    double MinimumWidthPx,
    double MaximumWidthPx);

public static class GanttGridColumns
{
    public static IReadOnlyList<GanttGridColumnDefinition> All { get; } =
    [
        new(GanttGridColumn.Resource, "resource", "Resource", 112, 90, 240),
        new(GanttGridColumn.State, "state", "State", 42, 36, 90),
        new(GanttGridColumn.Busy, "busy", "Busy", 42, 36, 90),
        new(GanttGridColumn.Load, "load", "Load", 38, 36, 90),
        new(GanttGridColumn.Operations, "operations", "Ops", 30, 28, 72),
        new(GanttGridColumn.Next, "next", "Next", 48, 42, 110),
        new(GanttGridColumn.Exceptions, "exceptions", "Exc", 36, 32, 72)
    ];

    public static IReadOnlyList<GanttGridColumn> DefaultVisible { get; } =
    [GanttGridColumn.Resource, GanttGridColumn.State, GanttGridColumn.Busy, GanttGridColumn.Load, GanttGridColumn.Operations, GanttGridColumn.Next];

    public static GanttGridColumnDefinition Definition(GanttGridColumn column) => All.Single(x => x.Column == column);
    public static bool TryParse(string? key, out GanttGridColumn column)
    {
        var definition = All.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        column = definition?.Column ?? default;
        return definition is not null;
    }
}
