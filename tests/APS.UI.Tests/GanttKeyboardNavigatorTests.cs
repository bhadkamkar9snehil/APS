using APS.Application;
using APS.Domain;
using APS.UI.Components.PlanningWorkbench.Gantt;

namespace APS.UI.Tests;

public sealed class GanttKeyboardNavigatorTests
{
    [Fact]
    public void Horizontal_navigation_stays_in_lane_and_vertical_navigation_chooses_nearest_time()
    {
        var row0 = Row(0, ("A", 1), ("B", 4), ("C", 8));
        var row1 = Row(1, ("D", 2), ("E", 5), ("F", 10));
        var scene = Scene(row0, row1);

        Assert.Equal("C", GanttKeyboardNavigator.Next(scene, "B", GanttKeyboardDirection.Right));
        Assert.Equal("A", GanttKeyboardNavigator.Next(scene, "B", GanttKeyboardDirection.Left));
        Assert.Equal("E", GanttKeyboardNavigator.Next(scene, "B", GanttKeyboardDirection.Down));
        Assert.Equal("B", GanttKeyboardNavigator.Next(scene, "E", GanttKeyboardDirection.Up));
    }

    [Fact]
    public void Navigation_at_an_edge_keeps_the_current_operation()
    {
        var scene = Scene(Row(0, ("A", 1)));

        Assert.Equal("A", GanttKeyboardNavigator.Next(scene, "A", GanttKeyboardDirection.Left));
        Assert.Equal("A", GanttKeyboardNavigator.Next(scene, "A", GanttKeyboardDirection.Up));
    }

    [Fact]
    public void Home_end_and_page_navigation_have_deterministic_mounted_row_semantics()
    {
        var rows = Enumerable.Range(0, 8)
            .Select(index => Row(index, ($"A{index}", 1), ($"B{index}", 6)))
            .ToArray();
        var scene = Scene(rows);

        Assert.Equal("A3", GanttKeyboardNavigator.Next(scene, "B3", GanttKeyboardDirection.Home));
        Assert.Equal("B3", GanttKeyboardNavigator.Next(scene, "A3", GanttKeyboardDirection.End));
        Assert.Equal("B7", GanttKeyboardNavigator.Next(scene, "B2", GanttKeyboardDirection.PageDown));
        Assert.Equal("B0", GanttKeyboardNavigator.Next(scene, "B4", GanttKeyboardDirection.PageUp));
    }

    private static GanttRowModel Row(int index, params (string key, int hour)[] entries)
    {
        var resourceId = Guid.NewGuid();
        var lane = new ScheduleResourceLaneView(resourceId, $"R{index}", $"Resource {index}", ProcessUnitType.Eaf, ResourceOperatingState.Available, 0, Array.Empty<ScheduledProcessOperationView>(), DisplayOrder: index);
        var operations = entries.Select(entry =>
        {
            var operation = new ScheduledProcessOperationView(Guid.NewGuid(), entry.key, Guid.NewGuid(), ProcessOperationType.Eaf, resourceId, lane.ResourceCode, lane.ResourceName, ProcessUnitType.Eaf, ResourceOperatingState.Available, Start.AddHours(entry.hour), Start.AddHours(entry.hour + 1), 70m, "G", "S");
            return new GanttOperationModel(operation, null, OperationExecutionStatus.Planned, 0, 1, 12, entry.key, entry.key, entry.key, false, 0, GanttBaselineChange.Added);
        }).ToArray();
        return new GanttRowModel(index, lane, operations, Array.Empty<GanttBaselineModel>(), Array.Empty<PlanningResourceCalendarIntervalView>(), Array.Empty<GanttCampaignSpanModel>(), 0, null);
    }

    private static readonly DateTime Start = new(2026, 8, 22, 0, 0, 0, DateTimeKind.Utc);
    private static GanttScene Scene(params GanttRowModel[] rows) => new(
        rows, rows.Length, Array.Empty<GanttAxisTickModel>(), Array.Empty<GanttDueMarkerModel>(), Array.Empty<GanttDependencyLineModel>(),
        rows.SelectMany(x => x.Operations).ToDictionary(x => x.Operation.PlanningKey, x => x.Operation),
        new Dictionary<string, PlanningOperationWorkbenchDetail>(), Array.Empty<PlanningBindingEvidenceView>());
}
