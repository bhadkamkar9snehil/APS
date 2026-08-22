using APS.UI.State;

namespace APS.UI.Tests;

public sealed class GanttViewportStateTests
{
    private static readonly DateTime PlanStart = new(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PlanEnd = PlanStart.AddDays(31);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(6, 300)]
    [InlineData(12, 600)]
    [InlineData(24, 1200)]
    public void Time_and_pixel_conversion_are_inverse(double hours, double expectedX)
    {
        var viewport = Viewport(PlanStart, PlanStart.AddDays(1), 1200);

        var x = viewport.TimeToX(PlanStart.AddHours(hours));
        var roundTrip = viewport.XToTime(x);

        Assert.Equal(expectedX, x, 6);
        Assert.Equal(PlanStart.AddHours(hours), roundTrip);
    }

    [Fact]
    public void Clip_preserves_true_visible_duration_without_a_fake_minimum_width()
    {
        var viewport = Viewport(PlanStart, PlanStart.AddHours(8), 800);

        var clipped = viewport.Clip(PlanStart.AddMinutes(-30), PlanStart.AddMinutes(3));

        Assert.True(clipped.IsVisible);
        Assert.Equal(PlanStart, clipped.VisibleStartUtc);
        Assert.Equal(PlanStart.AddMinutes(3), clipped.VisibleEndUtc);
        Assert.Equal(0, clipped.X, 6);
        Assert.Equal(5, clipped.Width, 6);
    }

    [Theory]
    [InlineData(GanttSnapMode.Hour, 10, 31, 11, 0)]
    [InlineData(GanttSnapMode.ThirtyMinutes, 10, 16, 10, 30)]
    [InlineData(GanttSnapMode.FifteenMinutes, 10, 8, 10, 15)]
    [InlineData(GanttSnapMode.FiveMinutes, 10, 3, 10, 5)]
    [InlineData(GanttSnapMode.Free, 10, 3, 10, 3)]
    public void Snap_uses_the_selected_policy(
        GanttSnapMode mode,
        int inputHour,
        int inputMinute,
        int expectedHour,
        int expectedMinute)
    {
        var viewport = Viewport(PlanStart, PlanStart.AddDays(1), 1200);
        var input = PlanStart.AddHours(inputHour).AddMinutes(inputMinute);

        var snapped = viewport.Snap(input, mode);

        Assert.Equal(PlanStart.AddHours(expectedHour).AddMinutes(expectedMinute), snapped);
    }

    [Fact]
    public void Shift_snap_uses_authoritative_boundaries()
    {
        var viewport = Viewport(PlanStart, PlanStart.AddDays(1), 1200);
        var boundaries = new[]
        {
            PlanStart.AddHours(6),
            PlanStart.AddHours(14),
            PlanStart.AddHours(22)
        };

        var snapped = viewport.Snap(PlanStart.AddHours(12), GanttSnapMode.ShiftBoundary, boundaries);

        Assert.Equal(PlanStart.AddHours(14), snapped);
    }

    [Fact]
    public void Pointer_anchored_zoom_keeps_the_same_time_under_the_pointer()
    {
        var viewport = Viewport(PlanStart, PlanStart.AddDays(7), 1400);
        var pointerX = 980d;
        var anchoredTime = viewport.XToTime(pointerX);

        viewport.ZoomAt(PlanningWorkbenchZoom.ThreeDays, pointerX);

        Assert.Equal(anchoredTime, viewport.XToTime(pointerX));
        Assert.Equal(TimeSpan.FromDays(3), viewport.VisibleEndUtc - viewport.VisibleStartUtc);
    }

    [Fact]
    public void Fit_and_reset_restore_the_exact_pre_fit_viewport()
    {
        var viewport = Viewport(PlanStart.AddDays(4), PlanStart.AddDays(7), 900);
        var originalStart = viewport.VisibleStartUtc;
        var originalEnd = viewport.VisibleEndUtc;
        var originalZoom = viewport.Zoom;

        viewport.Fit(PlanStart.AddDays(10), PlanStart.AddDays(20));
        Assert.True(viewport.VisibleStartUtc <= PlanStart.AddDays(10));
        Assert.True(viewport.VisibleEndUtc >= PlanStart.AddDays(20));
        Assert.Equal(PlanningWorkbenchZoom.Fit, viewport.Zoom);

        Assert.True(viewport.ResetFit());
        Assert.Equal(originalStart, viewport.VisibleStartUtc);
        Assert.Equal(originalEnd, viewport.VisibleEndUtc);
        Assert.Equal(originalZoom, viewport.Zoom);
    }

    [Fact]
    public void Pan_clamps_to_the_plan_horizon()
    {
        var viewport = Viewport(PlanStart, PlanStart.AddDays(1), 1200);

        viewport.Pan(-4);
        Assert.Equal(PlanStart, viewport.VisibleStartUtc);

        viewport.Pan(40);
        Assert.Equal(PlanEnd, viewport.VisibleEndUtc);
        Assert.Equal(TimeSpan.FromDays(1), viewport.VisibleEndUtc - viewport.VisibleStartUtc);
    }

    [Fact]
    public void Repeated_zoom_round_trip_does_not_accumulate_date_drift()
    {
        var viewport = Viewport(PlanStart.AddDays(3), PlanStart.AddDays(10), 1400);
        var pointerX = 777d;
        var anchor = viewport.XToTime(pointerX);

        for (var i = 0; i < 20; i++)
        {
            viewport.ZoomAt(PlanningWorkbenchZoom.Day, pointerX);
            viewport.ZoomAt(PlanningWorkbenchZoom.Week, pointerX);
        }

        Assert.Equal(anchor, viewport.XToTime(pointerX));
    }

    [Theory]
    [InlineData(PlanningWorkbenchZoom.Detail, 0.5)]
    [InlineData(PlanningWorkbenchZoom.Shift, 8)]
    [InlineData(PlanningWorkbenchZoom.Day, 24)]
    [InlineData(PlanningWorkbenchZoom.ThreeDays, 72)]
    [InlineData(PlanningWorkbenchZoom.Week, 168)]
    [InlineData(PlanningWorkbenchZoom.TwoWeeks, 336)]
    [InlineData(PlanningWorkbenchZoom.Month, 720)]
    public void Named_zoom_levels_cover_operational_detail_through_month(
        PlanningWorkbenchZoom zoom,
        double expectedHours)
    {
        var viewport = Viewport(PlanStart, PlanStart.AddDays(7), 1400);

        viewport.ZoomAt(zoom, 700);

        Assert.Equal(TimeSpan.FromHours(expectedHours), viewport.VisibleEndUtc - viewport.VisibleStartUtc);
    }

    [Fact]
    public void Grid_width_is_clamped_to_planner_safe_bounds()
    {
        var viewport = Viewport(PlanStart, PlanStart.AddDays(1), 1000);

        Assert.Equal(320, viewport.GridWidthPx);
        viewport.SetGridWidth(100, 1000);
        Assert.Equal(220, viewport.GridWidthPx);
        viewport.SetGridWidth(900, 1000);
        Assert.Equal(450, viewport.GridWidthPx);
    }

    [Fact]
    public void Snap_and_density_are_explicit_view_preferences()
    {
        var viewport = Viewport(PlanStart, PlanStart.AddDays(1), 1000);

        viewport.SetSnapMode(GanttSnapMode.FiveMinutes);
        viewport.SetDensity(GanttDensity.Expanded);

        Assert.Equal(GanttSnapMode.FiveMinutes, viewport.SnapMode);
        Assert.Equal(GanttDensity.Expanded, viewport.Density);
        Assert.Equal(80, viewport.RowHeightPx);
    }

    private static GanttViewportState Viewport(DateTime visibleStart, DateTime visibleEnd, double width)
    {
        var viewport = new GanttViewportState();
        viewport.Configure(PlanStart, PlanEnd, visibleStart, visibleEnd, width);
        return viewport;
    }
}
