using System.Text.Json;
using System.Text.Json.Serialization;
using APS.UI.State;

namespace APS.UI.Components.PlanningWorkbench.Gantt;

public sealed record GanttPreferences(
    double GridWidthPx,
    GanttDensity Density,
    GanttSnapMode SnapMode,
    PlanningWorkbenchZoom Zoom,
    bool ShowBaseline,
    GanttBaselineMode BaselineMode,
    bool ShowDependencies,
    IReadOnlyList<string> VisibleColumns,
    IReadOnlyDictionary<string, double> ColumnWidths,
    GanttGridSortColumn SortColumn,
    bool SortDescending,
    bool ShowDueMarkers,
    bool ShowNowMarker,
    bool ShowReferenceMarker,
    bool ShowFrozenFence,
    IReadOnlyList<string> CollapsedGroups,
    int CapacityPanelHeightPx,
    bool CapacityPanelOpen)
{
    public static GanttPreferences Default { get; } = new(
        320d,
        GanttDensity.Standard,
        GanttSnapMode.FifteenMinutes,
        PlanningWorkbenchZoom.Fit,
        true,
        GanttBaselineMode.Ghost,
        false,
        ["resource", "state", "busy", "load", "operations", "next"],
        GanttGridColumns.All.ToDictionary(x => x.Key, x => x.DefaultWidthPx, StringComparer.OrdinalIgnoreCase),
        GanttGridSortColumn.Canonical,
        false,
        true,
        true,
        true,
        true,
        Array.Empty<string>(),
        220,
        false);
}

public static class GanttPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(GanttPreferences preferences) =>
        JsonSerializer.Serialize(preferences, JsonOptions);

    public static GanttPreferences Parse(string? json, double availableWidthPx)
    {
        GanttPreferencesPayload payload;
        try
        {
            payload = string.IsNullOrWhiteSpace(json)
                ? new GanttPreferencesPayload()
                : JsonSerializer.Deserialize<GanttPreferencesPayload>(json, JsonOptions) ?? new GanttPreferencesPayload();
        }
        catch (JsonException)
        {
            payload = new GanttPreferencesPayload();
        }

        var defaults = GanttPreferences.Default;
        var parsed = new GanttPreferences(
            payload.GridWidthPx ?? defaults.GridWidthPx,
            payload.Density ?? defaults.Density,
            payload.SnapMode ?? defaults.SnapMode,
            payload.Zoom ?? defaults.Zoom,
            payload.ShowBaseline ?? defaults.ShowBaseline,
            payload.BaselineMode ?? defaults.BaselineMode,
            payload.ShowDependencies ?? defaults.ShowDependencies,
            payload.VisibleColumns ?? defaults.VisibleColumns,
            payload.ColumnWidths ?? defaults.ColumnWidths,
            payload.SortColumn ?? defaults.SortColumn,
            payload.SortDescending ?? defaults.SortDescending,
            payload.ShowDueMarkers ?? defaults.ShowDueMarkers,
            payload.ShowNowMarker ?? defaults.ShowNowMarker,
            payload.ShowReferenceMarker ?? defaults.ShowReferenceMarker,
            payload.ShowFrozenFence ?? defaults.ShowFrozenFence,
            payload.CollapsedGroups ?? defaults.CollapsedGroups,
            payload.CapacityPanelHeightPx ?? defaults.CapacityPanelHeightPx,
            payload.CapacityPanelOpen ?? defaults.CapacityPanelOpen);
        var maximumGridWidth = Math.Max(220d, availableWidthPx * .45d);
        var columns = parsed.VisibleColumns is { Count: > 0 }
            ? parsed.VisibleColumns.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : GanttPreferences.Default.VisibleColumns;
        var widths = GanttGridColumns.All.ToDictionary(
            definition => definition.Key,
            definition => parsed.ColumnWidths.TryGetValue(definition.Key, out var width)
                ? Math.Clamp(width, definition.MinimumWidthPx, definition.MaximumWidthPx)
                : definition.DefaultWidthPx,
            StringComparer.OrdinalIgnoreCase);
        return parsed with
        {
            GridWidthPx = Math.Clamp(parsed.GridWidthPx, 220d, maximumGridWidth),
            VisibleColumns = columns,
            ColumnWidths = widths,
            CollapsedGroups = parsed.CollapsedGroups?.Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>(),
            CapacityPanelHeightPx = Math.Clamp(parsed.CapacityPanelHeightPx, 120, 600)
        };
    }

    private sealed class GanttPreferencesPayload
    {
        public double? GridWidthPx { get; set; }
        public GanttDensity? Density { get; set; }
        public GanttSnapMode? SnapMode { get; set; }
        public PlanningWorkbenchZoom? Zoom { get; set; }
        public bool? ShowBaseline { get; set; }
        public GanttBaselineMode? BaselineMode { get; set; }
        public bool? ShowDependencies { get; set; }
        public IReadOnlyList<string>? VisibleColumns { get; set; }
        public IReadOnlyDictionary<string, double>? ColumnWidths { get; set; }
        public GanttGridSortColumn? SortColumn { get; set; }
        public bool? SortDescending { get; set; }
        public bool? ShowDueMarkers { get; set; }
        public bool? ShowNowMarker { get; set; }
        public bool? ShowReferenceMarker { get; set; }
        public bool? ShowFrozenFence { get; set; }
        public IReadOnlyList<string>? CollapsedGroups { get; set; }
        public int? CapacityPanelHeightPx { get; set; }
        public bool? CapacityPanelOpen { get; set; }
    }
}
