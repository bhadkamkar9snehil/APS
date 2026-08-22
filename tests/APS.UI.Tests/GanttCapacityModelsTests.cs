using APS.Application;
using APS.Domain;
using APS.UI.Components.PlanningWorkbench.Gantt;
using APS.UI.State;

namespace APS.UI.Tests;

public sealed class GanttCapacityModelsTests
{
    private static readonly DateTime Start = new(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(2, GanttCapacityBucketScale.Hour)]
    [InlineData(7, GanttCapacityBucketScale.Shift)]
    [InlineData(30, GanttCapacityBucketScale.Day)]
    public void Capacity_bucket_scale_adapts_to_the_visible_duration(int visibleDays, GanttCapacityBucketScale expected)
    {
        var state = new PlanningWorkbenchState();
        state.SetPlanWindow(Start, Start.AddDays(40), Start, Start.AddDays(40));
        state.Viewport.FocusRange(Start, Start.AddDays(visibleDays));

        Assert.Equal(expected, GanttCapacityModels.ScaleFor(state));
    }

    [Fact]
    public void Capacity_segments_preserve_processing_downtime_and_overload_truth()
    {
        var resourceId = Guid.NewGuid();
        var state = new PlanningWorkbenchState();
        state.SetPlanWindow(Start, Start.AddDays(2), Start, Start.AddDays(2));
        state.Viewport.FocusRange(Start, Start.AddDays(2));
        var buckets = new[]
        {
            new PlanningCapacityBucketView(resourceId, Start, Start.AddHours(1), 40, 50, 20, 1.25m, PlanningCapacityBasis.MachineTime, ResourceSchedulingMode.Disjunctive),
            new PlanningCapacityBucketView(resourceId, Start.AddHours(1), Start.AddHours(2), 60, 30, 0, .5m, PlanningCapacityBasis.MachineTime, ResourceSchedulingMode.Disjunctive)
        };

        var segments = GanttCapacityModels.Build(buckets, state);

        Assert.Equal(2, segments.Count);
        Assert.True(segments[0].IsOverloaded);
        Assert.Equal(50, segments[0].ProcessingMinutes);
        Assert.Equal(20, segments[0].UnavailableMinutes);
        Assert.False(segments[1].IsOverloaded);
        Assert.Equal(50, segments[1].OccupancyPercent);
    }
}
