using APS.UI.Components.PlanningWorkbench.Gantt;
using APS.UI.State;

namespace APS.UI.Tests;

public sealed class GanttPreferencesStoreTests
{
    [Fact]
    public void Defaults_are_planner_safe_and_complete()
    {
        var preferences = GanttPreferences.Default;

        Assert.Equal(320, preferences.GridWidthPx);
        Assert.Equal(GanttDensity.Standard, preferences.Density);
        Assert.Equal(GanttSnapMode.FifteenMinutes, preferences.SnapMode);
        Assert.Equal(PlanningWorkbenchZoom.Fit, preferences.Zoom);
        Assert.True(preferences.ShowBaseline);
        Assert.False(preferences.ShowDependencies);
        Assert.NotEmpty(preferences.VisibleColumns);
        Assert.Equal(220, preferences.CapacityPanelHeightPx);
    }

    [Fact]
    public void Json_round_trip_clamps_unsafe_sizes_and_preserves_named_preferences()
    {
        var json = """
            {"gridWidthPx":40,"density":"Compact","snapMode":"FiveMinutes","zoom":"ThreeDays","showBaseline":false,"showDependencies":true,"capacityPanelHeightPx":9000,"visibleColumns":["resource","load"],"collapsedGroups":["SMS/LRF"]}
            """;

        var preferences = GanttPreferencesStore.Parse(json, availableWidthPx: 1200);
        var roundTrip = GanttPreferencesStore.Parse(GanttPreferencesStore.Serialize(preferences), 1200);

        Assert.Equal(220, roundTrip.GridWidthPx);
        Assert.Equal(GanttDensity.Compact, roundTrip.Density);
        Assert.Equal(GanttSnapMode.FiveMinutes, roundTrip.SnapMode);
        Assert.Equal(PlanningWorkbenchZoom.ThreeDays, roundTrip.Zoom);
        Assert.False(roundTrip.ShowBaseline);
        Assert.True(roundTrip.ShowDependencies);
        Assert.Equal(600, roundTrip.CapacityPanelHeightPx);
        Assert.Equal(["resource", "load"], roundTrip.VisibleColumns);
        Assert.Equal(["SMS/LRF"], roundTrip.CollapsedGroups);
    }

    [Fact]
    public void Empty_or_partial_json_merges_with_explicit_planner_defaults()
    {
        var empty = GanttPreferencesStore.Parse("{}", 1600);
        var partial = GanttPreferencesStore.Parse("{\"showDependencies\":true}", 1600);

        Assert.Equal(GanttPreferences.Default.GridWidthPx, empty.GridWidthPx);
        Assert.Equal(GanttPreferences.Default.Density, empty.Density);
        Assert.Equal(GanttPreferences.Default.SnapMode, empty.SnapMode);
        Assert.Equal(GanttPreferences.Default.Zoom, empty.Zoom);
        Assert.Equal(GanttPreferences.Default.ShowBaseline, empty.ShowBaseline);
        Assert.Equal(GanttPreferences.Default.ShowDependencies, empty.ShowDependencies);
        Assert.Equal(GanttPreferences.Default.VisibleColumns, empty.VisibleColumns);
        Assert.Equal(GanttDensity.Standard, partial.Density);
        Assert.Equal(GanttSnapMode.FifteenMinutes, partial.SnapMode);
        Assert.Equal(PlanningWorkbenchZoom.Fit, partial.Zoom);
        Assert.True(partial.ShowBaseline);
        Assert.True(partial.ShowDependencies);
    }
}
