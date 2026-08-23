using APS.Application;
using APS.UI.State;

namespace APS.UI.Components.PlanningWorkbench.Gantt;

public enum GanttCapacityBucketScale
{
    Hour,
    Shift,
    Day
}

public sealed record GanttCapacitySegmentModel(
    Guid ResourceId,
    DateTime StartUtc,
    DateTime EndUtc,
    double LeftPercent,
    double WidthPercent,
    double AvailableMinutes,
    double ProcessingMinutes,
    double UnavailableMinutes,
    decimal OccupancyPercent,
    bool IsOverloaded,
    PlanningCapacityBasis Basis);

public static class GanttCapacityModels
{
    public static GanttCapacityBucketScale ScaleFor(PlanningWorkbenchState state) =>
        (state.VisibleEndUtc - state.VisibleStartUtc).TotalDays switch
        {
            <= 3d => GanttCapacityBucketScale.Hour,
            <= 14d => GanttCapacityBucketScale.Shift,
            _ => GanttCapacityBucketScale.Day
        };

    public static IReadOnlyList<GanttCapacitySegmentModel> Build(
        IEnumerable<PlanningCapacityBucketView> buckets,
        PlanningWorkbenchState state)
    {
        var period = ScaleFor(state) switch
        {
            GanttCapacityBucketScale.Hour => TimeSpan.FromHours(1),
            GanttCapacityBucketScale.Shift => TimeSpan.FromHours(8),
            _ => TimeSpan.FromDays(1)
        };

        return buckets
            .Where(x => x.EndUtc > state.VisibleStartUtc && x.StartUtc < state.VisibleEndUtc)
            .GroupBy(x => (x.ResourceId, StartUtc: FloorUtc(x.StartUtc, period), x.Basis))
            .Select(group =>
            {
                var start = group.Key.StartUtc < state.VisibleStartUtc ? state.VisibleStartUtc : group.Key.StartUtc;
                var periodEnd = group.Key.StartUtc + period;
                var end = periodEnd > state.VisibleEndUtc ? state.VisibleEndUtc : periodEnd;
                var available = group.Sum(x => x.AvailableMinutes);
                var processing = group.Sum(x => x.ProcessingMinutes);
                var unavailable = group.Sum(x => x.UnavailableMinutes);
                var occupancy = available <= 0d ? (processing > 0d ? 100m : 0m) : (decimal)(processing / available * 100d);
                return new GanttCapacitySegmentModel(
                    group.Key.ResourceId,
                    start,
                    end,
                    GanttModels.Percent(state, start),
                    GanttModels.WidthPercent(state, start, end),
                    available,
                    processing,
                    unavailable,
                    decimal.Round(occupancy, 1),
                    processing > available,
                    group.Key.Basis);
            })
            .OrderBy(x => x.ResourceId)
            .ThenBy(x => x.StartUtc)
            .ToArray();
    }

    private static DateTime FloorUtc(DateTime value, TimeSpan period)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return new DateTime(utc.Ticks / period.Ticks * period.Ticks, DateTimeKind.Utc);
    }
}
