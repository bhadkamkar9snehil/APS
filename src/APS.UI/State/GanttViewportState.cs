namespace APS.UI.State;

public enum GanttSnapMode
{
    ShiftBoundary,
    Hour,
    ThirtyMinutes,
    FifteenMinutes,
    FiveMinutes,
    Free
}

public enum GanttDensity
{
    Compact,
    Standard,
    Expanded
}

public readonly record struct GanttClipResult(
    bool IsVisible,
    DateTime VisibleStartUtc,
    DateTime VisibleEndUtc,
    double X,
    double Width);

public sealed class GanttViewportState
{
    private ViewportSnapshot? fitRestore;

    public DateTime PlanStartUtc { get; private set; }
    public DateTime PlanEndUtc { get; private set; }
    public DateTime ContentStartUtc { get; private set; }
    public DateTime ContentEndUtc { get; private set; }
    public DateTime VisibleStartUtc { get; private set; }
    public DateTime VisibleEndUtc { get; private set; }
    public double TimelineWidthPx { get; private set; } = 1d;
    public double GridWidthPx { get; private set; } = 320d;
    public GanttSnapMode SnapMode { get; private set; } = GanttSnapMode.FifteenMinutes;
    public GanttDensity Density { get; private set; } = GanttDensity.Standard;
    public int RowHeightPx => Density switch
    {
        GanttDensity.Compact => 48,
        GanttDensity.Expanded => 80,
        _ => 60
    };
    public int VisibleRowStart { get; private set; }
    public int VisibleRowEndExclusive { get; private set; } = int.MaxValue;
    public PlanningWorkbenchZoom Zoom { get; private set; } = PlanningWorkbenchZoom.Fit;

    public void Configure(
        DateTime planStartUtc,
        DateTime planEndUtc,
        DateTime visibleStartUtc,
        DateTime visibleEndUtc,
        double timelineWidthPx,
        DateTime? contentStartUtc = null,
        DateTime? contentEndUtc = null)
    {
        PlanStartUtc = AsUtc(planStartUtc);
        PlanEndUtc = AsUtc(planEndUtc);
        if (PlanEndUtc <= PlanStartUtc) PlanEndUtc = PlanStartUtc.AddHours(1);

        TimelineWidthPx = Math.Max(1d, timelineWidthPx);
        ContentStartUtc = Clamp(AsUtc(contentStartUtc ?? visibleStartUtc), PlanStartUtc, PlanEndUtc);
        ContentEndUtc = Clamp(AsUtc(contentEndUtc ?? visibleEndUtc), PlanStartUtc, PlanEndUtc);
        if (ContentEndUtc <= ContentStartUtc)
        {
            ContentStartUtc = PlanStartUtc;
            ContentEndUtc = PlanEndUtc;
        }
        SetVisibleRange(AsUtc(visibleStartUtc), AsUtc(visibleEndUtc));
        Zoom = InferZoom(VisibleEndUtc - VisibleStartUtc);
        fitRestore = null;
    }

    public double TimeToX(DateTime timestampUtc)
    {
        var durationTicks = VisibleEndUtc.Ticks - VisibleStartUtc.Ticks;
        if (durationTicks <= 0) return 0d;

        var elapsedTicks = AsUtc(timestampUtc).Ticks - VisibleStartUtc.Ticks;
        return (double)((decimal)elapsedTicks / durationTicks * (decimal)TimelineWidthPx);
    }

    public DateTime XToTime(double x)
    {
        var durationTicks = VisibleEndUtc.Ticks - VisibleStartUtc.Ticks;
        if (durationTicks <= 0) return VisibleStartUtc;

        var ratio = (decimal)x / (decimal)TimelineWidthPx;
        var elapsedTicks = decimal.ToInt64(decimal.Round(ratio * durationTicks, 0, MidpointRounding.AwayFromZero));
        return new DateTime(VisibleStartUtc.Ticks + elapsedTicks, DateTimeKind.Utc);
    }

    public GanttClipResult Clip(DateTime startUtc, DateTime endUtc)
    {
        var start = AsUtc(startUtc);
        var end = AsUtc(endUtc);
        if (end <= start || end <= VisibleStartUtc || start >= VisibleEndUtc)
            return new GanttClipResult(false, start, end, 0d, 0d);

        var visibleStart = start < VisibleStartUtc ? VisibleStartUtc : start;
        var visibleEnd = end > VisibleEndUtc ? VisibleEndUtc : end;
        var x = TimeToX(visibleStart);
        var width = TimeToX(visibleEnd) - x;
        return new GanttClipResult(true, visibleStart, visibleEnd, x, width);
    }

    public DateTime Snap(
        DateTime timestampUtc,
        GanttSnapMode mode,
        IReadOnlyCollection<DateTime>? shiftBoundariesUtc = null) =>
        SnapTimestamp(AsUtc(timestampUtc), mode, shiftBoundariesUtc ?? Array.Empty<DateTime>());

    /// <summary>
    /// The single source of truth for snap-mode rounding. <c>GanttDragGeometry</c> (the Components layer)
    /// calls in here rather than keeping its own copy, so the client-visible drag guide and the
    /// server-confirmed snap can never drift apart. <paramref name="timestampUtc"/> must already be
    /// UTC-normalized by the caller.
    /// </summary>
    public static DateTime SnapTimestamp(
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
                .OrderBy(boundary => Math.Abs(boundary.Ticks - timestampUtc.Ticks))
                .ThenBy(boundary => boundary)
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
        var elapsedTicks = timestampUtc.Ticks - dayStart.Ticks;
        var snappedSteps = decimal.Round(
            (decimal)elapsedTicks / increment.Ticks,
            0,
            MidpointRounding.AwayFromZero);
        return new DateTime(dayStart.Ticks + decimal.ToInt64(snappedSteps * increment.Ticks), DateTimeKind.Utc);
    }

    public void ZoomAt(PlanningWorkbenchZoom zoom, double pointerX)
    {
        if (zoom == PlanningWorkbenchZoom.Fit) return;

        var anchor = XToTime(pointerX);
        var ratio = Math.Clamp(pointerX / TimelineWidthPx, 0d, 1d);
        var requested = ZoomDuration(zoom);
        var planDuration = PlanEndUtc - PlanStartUtc;
        var duration = requested > planDuration ? planDuration : requested;
        var beforeAnchor = TimeSpan.FromTicks((long)Math.Round(duration.Ticks * ratio));
        SetVisibleRange(anchor - beforeAnchor, anchor - beforeAnchor + duration);
        Zoom = zoom;
        fitRestore = null;
    }

    public void Fit(DateTime contentStartUtc, DateTime contentEndUtc)
    {
        var start = AsUtc(contentStartUtc);
        var end = AsUtc(contentEndUtc);
        if (end <= start) end = start.AddHours(1);

        fitRestore ??= new ViewportSnapshot(VisibleStartUtc, VisibleEndUtc, Zoom);
        var duration = end - start;
        var padding = TimeSpan.FromTicks(Math.Clamp(
            (long)(duration.Ticks * 0.03d),
            TimeSpan.FromMinutes(15).Ticks,
            TimeSpan.FromHours(12).Ticks));
        SetVisibleRange(start - padding, end + padding);
        Zoom = PlanningWorkbenchZoom.Fit;
    }

    public void FitContent() => Fit(ContentStartUtc, ContentEndUtc);

    public void FocusRange(DateTime startUtc, DateTime endUtc)
    {
        SetVisibleRange(AsUtc(startUtc), AsUtc(endUtc));
        Zoom = InferZoom(VisibleEndUtc - VisibleStartUtc);
        fitRestore = null;
    }

    public bool ResetFit()
    {
        if (fitRestore is null) return false;
        var restore = fitRestore;
        fitRestore = null;
        SetVisibleRange(restore.VisibleStartUtc, restore.VisibleEndUtc);
        Zoom = restore.Zoom;
        return true;
    }

    public void Pan(double viewportFraction)
    {
        var duration = VisibleEndUtc - VisibleStartUtc;
        if (duration <= TimeSpan.Zero) return;

        var shift = TimeSpan.FromTicks((long)Math.Round(duration.Ticks * viewportFraction));
        SetVisibleRange(VisibleStartUtc + shift, VisibleEndUtc + shift);
    }

    public void SetTimelineWidth(double widthPx) => TimelineWidthPx = Math.Max(1d, widthPx);

    public void SetGridWidth(double widthPx, double availableWidthPx) =>
        GridWidthPx = Math.Clamp(widthPx, 220d, Math.Max(220d, availableWidthPx * 0.45d));

    public void SetSnapMode(GanttSnapMode mode) => SnapMode = mode;

    public void SetDensity(GanttDensity density) => Density = density;

    public void SetVisibleRowRange(int firstVisibleRow, int visibleRowCount, int overscan = 3)
    {
        var safeOverscan = Math.Max(0, overscan);
        VisibleRowStart = Math.Max(0, firstVisibleRow - safeOverscan);
        var requestedEnd = (long)Math.Max(0, firstVisibleRow) + Math.Max(1, visibleRowCount) + safeOverscan;
        VisibleRowEndExclusive = requestedEnd >= int.MaxValue ? int.MaxValue : (int)requestedEnd;
    }

    public Range MountedRowRange(int totalRowCount)
    {
        var total = Math.Max(0, totalRowCount);
        var start = Math.Min(VisibleRowStart, total);
        var end = Math.Clamp(VisibleRowEndExclusive, start, total);
        return start..end;
    }

    private void SetVisibleRange(DateTime startUtc, DateTime endUtc)
    {
        var duration = endUtc - startUtc;
        if (duration <= TimeSpan.Zero) duration = TimeSpan.FromHours(1);
        var planDuration = PlanEndUtc - PlanStartUtc;
        if (duration > planDuration) duration = planDuration;

        var start = startUtc;
        var end = start + duration;
        if (start < PlanStartUtc)
        {
            start = PlanStartUtc;
            end = start + duration;
        }
        if (end > PlanEndUtc)
        {
            end = PlanEndUtc;
            start = end - duration;
        }

        VisibleStartUtc = start;
        VisibleEndUtc = end;
    }

    private static TimeSpan ZoomDuration(PlanningWorkbenchZoom zoom) => zoom switch
    {
        PlanningWorkbenchZoom.Detail => TimeSpan.FromMinutes(30),
        PlanningWorkbenchZoom.Shift => TimeSpan.FromHours(8),
        PlanningWorkbenchZoom.Day => TimeSpan.FromDays(1),
        PlanningWorkbenchZoom.ThreeDays => TimeSpan.FromDays(3),
        PlanningWorkbenchZoom.Week => TimeSpan.FromDays(7),
        PlanningWorkbenchZoom.TwoWeeks => TimeSpan.FromDays(14),
        PlanningWorkbenchZoom.Month => TimeSpan.FromDays(30),
        _ => TimeSpan.FromDays(7)
    };

    private static PlanningWorkbenchZoom InferZoom(TimeSpan duration)
    {
        if (duration == TimeSpan.FromMinutes(30)) return PlanningWorkbenchZoom.Detail;
        if (duration == TimeSpan.FromHours(8)) return PlanningWorkbenchZoom.Shift;
        if (duration == TimeSpan.FromDays(1)) return PlanningWorkbenchZoom.Day;
        if (duration == TimeSpan.FromDays(3)) return PlanningWorkbenchZoom.ThreeDays;
        if (duration == TimeSpan.FromDays(7)) return PlanningWorkbenchZoom.Week;
        if (duration == TimeSpan.FromDays(14)) return PlanningWorkbenchZoom.TwoWeeks;
        if (duration == TimeSpan.FromDays(30)) return PlanningWorkbenchZoom.Month;
        return PlanningWorkbenchZoom.Fit;
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static DateTime Clamp(DateTime value, DateTime min, DateTime max) =>
        value < min ? min : value > max ? max : value;

    private sealed record ViewportSnapshot(
        DateTime VisibleStartUtc,
        DateTime VisibleEndUtc,
        PlanningWorkbenchZoom Zoom);
}
