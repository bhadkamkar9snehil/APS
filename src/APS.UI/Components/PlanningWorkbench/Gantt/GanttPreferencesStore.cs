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
            payload.CollapsedGroups ?? defaults.CollapsedGroups,
            payload.CapacityPanelHeightPx ?? defaults.CapacityPanelHeightPx,
            payload.CapacityPanelOpen ?? defaults.CapacityPanelOpen);
        var maximumGridWidth = Math.Max(220d, availableWidthPx * .45d);
        var columns = parsed.VisibleColumns is { Count: > 0 }
            ? parsed.VisibleColumns.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : GanttPreferences.Default.VisibleColumns;
        return parsed with
        {
            GridWidthPx = Math.Clamp(parsed.GridWidthPx, 220d, maximumGridWidth),
            VisibleColumns = columns,
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
        public IReadOnlyList<string>? CollapsedGroups { get; set; }
        public int? CapacityPanelHeightPx { get; set; }
        public bool? CapacityPanelOpen { get; set; }
    }
}
