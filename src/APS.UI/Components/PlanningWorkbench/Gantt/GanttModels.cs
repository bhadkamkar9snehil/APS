using APS.Application;
using APS.Domain;
using APS.UI.State;

namespace APS.UI.Components.PlanningWorkbench.Gantt;

public sealed record GanttScene(
    IReadOnlyList<GanttRowModel> Rows,
    int TotalRowCount,
    IReadOnlyList<GanttAxisTickModel> Ticks,
    IReadOnlyList<GanttDueMarkerModel> DueMarkers,
    IReadOnlyList<GanttDependencyLineModel> DependencyLines,
    IReadOnlyDictionary<string, ScheduledProcessOperationView> OperationsByKey,
    IReadOnlyDictionary<string, PlanningOperationWorkbenchDetail> DetailsByKey,
    IReadOnlyList<PlanningBindingEvidenceView> BindingEvidence)
{
    public int VisibleOperationCount => Rows.Sum(x => x.Operations.Count);
    public IReadOnlyList<GanttResourceGroupModel> ResourceGroups { get; init; } = Array.Empty<GanttResourceGroupModel>();
    public int ResourceCount { get; init; }
    public int TotalResourceCount { get; init; }
}

public enum GanttResourceGroupLevel
{
    Plant,
    Area,
    ProcessStage
}

public sealed record GanttResourceGroupModel(
    int SceneIndex,
    string Key,
    string? ParentKey,
    GanttResourceGroupLevel Level,
    string Code,
    string Label,
    int ResourceCount,
    bool IsCollapsed);

public sealed record GanttRowModel(
    int SceneIndex,
    ScheduleResourceLaneView Lane,
    IReadOnlyList<GanttOperationModel> Operations,
    IReadOnlyList<GanttBaselineModel> Baselines,
    IReadOnlyList<PlanningResourceCalendarIntervalView> CalendarIntervals,
    IReadOnlyList<GanttCampaignSpanModel> CampaignSpans,
    decimal VisibleUtilization,
    ScheduledProcessOperationView? NextOperation,
    int ExceptionCount = 0);

public sealed record GanttOperationModel(
    ScheduledProcessOperationView Operation,
    PlanningOperationWorkbenchDetail? Detail,
    OperationExecutionStatus ExecutionStatus,
    double LeftPercent,
    double WidthPercent,
    double LogicalWidthPx,
    string Label,
    string CompactLabel,
    string AccessibleName,
    bool IsSingleSourced,
    double RunningProgressPercent,
    GanttBaselineChange BaselineChange)
{
    public OperationAssignmentCommitmentState? CommitmentState { get; init; }
    public int EligibleResourceCount { get; init; }
    public DateTime? ActualStartUtc { get; init; }
    public DateTime? ActualEndUtc { get; init; }
}

public enum GanttBaselineChange
{
    Unchanged,
    TimeMoved,
    ResourceChanged,
    Added,
    Removed
}

public sealed record GanttBaselineModel(
    PlanningBaselinePlacementView Placement,
    double LeftPercent,
    double WidthPercent,
    GanttBaselineChange Change,
    int StartDeltaMinutes,
    int EndDeltaMinutes);

public sealed record GanttCampaignSpanModel(
    string CampaignNumber,
    double LeftPercent,
    double WidthPercent,
    int OperationCount);

public sealed record GanttAxisTickModel(
    DateTime TimeUtc,
    double LeftPercent,
    string PrimaryLabel,
    string SecondaryLabel,
    bool IsMajor);

public sealed record GanttDueMarkerModel(DateTime TimeUtc, double LeftPercent, string Label);

public sealed record GanttDependencyLineModel(
    string PredecessorPlanningKey,
    string SuccessorPlanningKey,
    double StartXPercent,
    double StartYpx,
    double EndXPercent,
    double EndYpx,
    PlanningDependencyType Type,
    PlanningDependencyCategory Category,
    int? MinimumLagMinutes,
    int CurrentLagMinutes,
    int? HeadroomMinutes);

public static class GanttModels
{
    private const double LogicalTimelineWidthPx = 1200d;

    public static GanttScene BuildScene(PlanningWorkbenchView workbench, PlanningWorkbenchState state)
    {
        var details = workbench.OperationDetails.ToDictionary(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase);
        var allOperations = workbench.Schedule.ResourceLanes
            .SelectMany(x => x.Operations)
            .ToDictionary(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase);
        var baselineByResource = workbench.BaselinePlacements
            .GroupBy(x => x.ResourceId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var baselineByKey = workbench.BaselinePlacements
            .GroupBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var calendarsByResource = workbench.ResourceCalendarIntervals
            .GroupBy(x => x.ResourceId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var constrainedResources = workbench.Exceptions
            .Where(x => x.Entity?.EntityType == PlannerEntityType.Resource)
            .Select(x => x.Entity!.EntityId)
            .ToHashSet();

        var candidateLanes = workbench.Schedule.ResourceLanes
            .Where(lane => lane.Operations.Any(operation => Matches(operation, details, state.SearchText)))
            .Concat(BuildMatchingBaselineOnlyLanes(workbench, state.SearchText))
            .DistinctBy(x => x.ResourceId)
            .ToArray();
        if (state.Mode == PlanningWorkbenchMode.Recovery)
        {
            var recoveryLanes = candidateLanes
                .Where(x => constrainedResources.Contains(x.ResourceId) || x.OperatingState != ResourceOperatingState.Available)
                .ToArray();
            if (recoveryLanes.Length > 0) candidateLanes = recoveryLanes;
        }

        var orderedLanes = candidateLanes
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.ProcessUnitType)
            .ThenBy(x => x.ResourceCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hierarchy = BuildHierarchy(orderedLanes, state.CollapsedResourceGroups, workbench, state);
        var mountedRange = state.Viewport.MountedRowRange(hierarchy.TotalRowCount);
        var mountedStart = mountedRange.Start.Value;
        var mountedEnd = mountedRange.End.Value;
        var rows = hierarchy.Lanes
            .Where(item => item.SceneIndex >= mountedStart && item.SceneIndex < mountedEnd)
            .Select(item => BuildRow(workbench, state, item.Lane, item.SceneIndex, details, allOperations, baselineByKey, baselineByResource, calendarsByResource))
            .ToArray();
        var resourceGroups = hierarchy.Groups
            .Where(item => item.SceneIndex >= mountedStart && item.SceneIndex < mountedEnd)
            .ToArray();
        var dependencyLines = BuildFocusedDependencyLines(workbench.DependencyLinks, hierarchy.Lanes, state);

        return new GanttScene(
            rows,
            hierarchy.TotalRowCount,
            BuildTicks(state),
            workbench.Demand.Rows
                .Where(x => x.RequiredDate >= state.VisibleStartUtc && x.RequiredDate <= state.VisibleEndUtc)
                .GroupBy(x => x.RequiredDate)
                .Select(x => new GanttDueMarkerModel(x.Key, Percent(state, x.Key), $"{x.Count()} order(s) due"))
                .OrderBy(x => x.TimeUtc)
                .ToArray(),
            dependencyLines,
            allOperations,
            details,
            (workbench.BindingEvidence ?? Array.Empty<PlanningBindingEvidenceView>()).ToArray())
        {
            ResourceGroups = resourceGroups,
            ResourceCount = orderedLanes.Length,
            TotalResourceCount = workbench.Schedule.ResourceLanes.Select(x => x.ResourceId)
                .Concat(workbench.BaselinePlacements.Select(x => x.ResourceId)).Distinct().Count()
        };
    }

    private static GanttHierarchyLayout BuildHierarchy(
        IReadOnlyList<ScheduleResourceLaneView> orderedLanes,
        IReadOnlySet<string> collapsedGroups,
        PlanningWorkbenchView workbench,
        PlanningWorkbenchState state)
    {
        var descriptorsByResource = orderedLanes.ToDictionary(x => x.ResourceId, GroupDescriptors);
        var laneOrdinal = orderedLanes.Select((lane, index) => (lane.ResourceId, index))
            .ToDictionary(x => x.ResourceId, x => x.index);
        var groupCounts = descriptorsByResource.Values
            .SelectMany(x => x)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        var groupFirstOrdinal = orderedLanes
            .SelectMany(lane => descriptorsByResource[lane.ResourceId].Select(group => (group.Key, Ordinal: laneOrdinal[lane.ResourceId])))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Min(item => item.Ordinal), StringComparer.OrdinalIgnoreCase);
        var canonicalHierarchyLanes = orderedLanes
            .OrderBy(lane => GroupOrdinal(lane, 0))
            .ThenBy(lane => GroupOrdinal(lane, 1))
            .ThenBy(lane => GroupOrdinal(lane, 2))
            .ThenBy(lane => laneOrdinal[lane.ResourceId])
            .ToArray();
        var hierarchyOrderedLanes = state.GridSortColumn == GanttGridSortColumn.Canonical
            ? canonicalHierarchyLanes
            : canonicalHierarchyLanes
                .GroupBy(lane => descriptorsByResource[lane.ResourceId].LastOrDefault()?.Key ?? $"resource:{lane.ResourceId:N}", StringComparer.OrdinalIgnoreCase)
                .SelectMany(group => SortResourceGroup(group, workbench, state))
                .ToArray();
        var emittedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = new List<GanttResourceGroupModel>();
        var lanes = new List<GanttLaneLayout>();
        var sceneIndex = 0;

        foreach (var lane in hierarchyOrderedLanes)
        {
            var ancestorCollapsed = false;
            foreach (var descriptor in descriptorsByResource[lane.ResourceId])
            {
                if (ancestorCollapsed) break;
                var collapsed = collapsedGroups.Contains(descriptor.Key);
                if (emittedGroups.Add(descriptor.Key))
                {
                    groups.Add(new GanttResourceGroupModel(
                        sceneIndex++,
                        descriptor.Key,
                        descriptor.ParentKey,
                        descriptor.Level,
                        descriptor.Code,
                        descriptor.Label,
                        groupCounts[descriptor.Key],
                        collapsed));
                }
                ancestorCollapsed = collapsed;
            }
            if (!ancestorCollapsed) lanes.Add(new GanttLaneLayout(sceneIndex++, lane));
        }

        return new GanttHierarchyLayout(lanes, groups, sceneIndex);

        int GroupOrdinal(ScheduleResourceLaneView lane, int level)
        {
            var descriptors = descriptorsByResource[lane.ResourceId];
            return descriptors.Count > level ? groupFirstOrdinal[descriptors[level].Key] : laneOrdinal[lane.ResourceId];
        }
    }

    private static IEnumerable<ScheduleResourceLaneView> SortResourceGroup(
        IEnumerable<ScheduleResourceLaneView> lanes,
        PlanningWorkbenchView workbench,
        PlanningWorkbenchState state)
    {
        Func<ScheduleResourceLaneView, IComparable?> key = state.GridSortColumn switch
        {
            GanttGridSortColumn.Resource => lane => lane.ResourceCode,
            GanttGridSortColumn.State => lane => lane.OperatingState,
            GanttGridSortColumn.Busy => lane => lane.OccupiedHours,
            GanttGridSortColumn.Load => lane => VisibleUtilization(workbench, state, lane.ResourceId),
            GanttGridSortColumn.Operations => lane => lane.Operations.Count,
            GanttGridSortColumn.Next => lane => lane.Operations.Where(x => x.StartUtc >= workbench.Plan.ReferenceTimeUtc).Select(x => (DateTime?)x.StartUtc).Min(),
            GanttGridSortColumn.Exceptions => lane => ExceptionCount(workbench, lane),
            _ => lane => lane.DisplayOrder
        };
        return state.GridSortDescending
            ? lanes.OrderByDescending(key).ThenBy(x => x.DisplayOrder).ThenBy(x => x.ResourceCode, StringComparer.OrdinalIgnoreCase)
            : lanes.OrderBy(key).ThenBy(x => x.DisplayOrder).ThenBy(x => x.ResourceCode, StringComparer.OrdinalIgnoreCase);
    }

    private static decimal VisibleUtilization(PlanningWorkbenchView workbench, PlanningWorkbenchState state, Guid resourceId)
    {
        var buckets = workbench.CapacityBuckets.Where(x => x.ResourceId == resourceId && x.EndUtc > state.VisibleStartUtc && x.StartUtc < state.VisibleEndUtc).ToArray();
        var available = buckets.Sum(x => x.AvailableMinutes);
        return available > 0d ? decimal.Round((decimal)(buckets.Sum(x => x.ProcessingMinutes) / available), 3) : 0m;
    }

    private static int ExceptionCount(PlanningWorkbenchView workbench, ScheduleResourceLaneView lane)
    {
        var operationIds = lane.Operations.Select(x => x.OperationSnapshotId).ToHashSet();
        return workbench.Exceptions.Count(x => x.Entity is not null &&
            ((x.Entity.EntityType == PlannerEntityType.Resource && x.Entity.EntityId == lane.ResourceId) ||
             (x.Entity.EntityType == PlannerEntityType.Operation && operationIds.Contains(x.Entity.EntityId))));
    }

    private static IReadOnlyList<GanttGroupDescriptor> GroupDescriptors(ScheduleResourceLaneView lane)
    {
        var result = new List<GanttGroupDescriptor>(3);
        string? parentKey = null;
        Add(GanttResourceGroupLevel.Plant, lane.PlantId, lane.PlantCode, lane.PlantName);
        Add(GanttResourceGroupLevel.Area, lane.AreaId, lane.AreaCode, lane.AreaName);
        Add(GanttResourceGroupLevel.ProcessStage, lane.ProcessStageId, lane.ProcessStageCode, lane.ProcessStageName);
        return result;

        void Add(GanttResourceGroupLevel level, Guid? id, string? code, string? name)
        {
            if (id is null && string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name)) return;
            var identity = id?.ToString("N") ?? code?.Trim().ToUpperInvariant() ?? name!.Trim().ToUpperInvariant();
            var key = $"{level.ToString().ToLowerInvariant()}:{parentKey ?? "root"}:{identity}";
            result.Add(new GanttGroupDescriptor(
                key,
                parentKey,
                level,
                string.IsNullOrWhiteSpace(code) ? name ?? level.ToString() : code,
                string.IsNullOrWhiteSpace(name) ? code ?? level.ToString() : name));
            parentKey = key;
        }
    }

    private static IEnumerable<ScheduleResourceLaneView> BuildBaselineOnlyLanes(PlanningWorkbenchView workbench)
    {
        var currentResourceIds = workbench.Schedule.ResourceLanes.Select(x => x.ResourceId).ToHashSet();
        return workbench.BaselinePlacements
            .Where(x => !currentResourceIds.Contains(x.ResourceId))
            .GroupBy(x => x.ResourceId)
            .Select(group => BaselineOnlyLane(group.First()));
    }

    private static IEnumerable<ScheduleResourceLaneView> BuildMatchingBaselineOnlyLanes(
        PlanningWorkbenchView workbench,
        string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText)) return BuildBaselineOnlyLanes(workbench);
        var currentResourceIds = workbench.Schedule.ResourceLanes.Select(x => x.ResourceId).ToHashSet();
        return workbench.BaselinePlacements
            .Where(x => !currentResourceIds.Contains(x.ResourceId))
            .Where(x => Contains(x.ResourceCode, searchText) || Contains(x.ResourceName, searchText) ||
                        Contains(x.GradeCode, searchText) || Contains(x.CrossSectionCode, searchText) ||
                        Contains(x.PlanningKey, searchText))
            .GroupBy(x => x.ResourceId)
            .Select(group => BaselineOnlyLane(group.First()));
    }

    private static ScheduleResourceLaneView BaselineOnlyLane(PlanningBaselinePlacementView placement) => new(
        placement.ResourceId,
        placement.ResourceCode,
        placement.ResourceName,
        placement.ProcessUnitType,
        placement.OperatingState,
        0d,
        Array.Empty<ScheduledProcessOperationView>(),
        placement.SchedulingMode,
        0d,
        0,
        null,
        placement.PlantId,
        placement.PlantCode,
        placement.PlantName,
        placement.AreaId,
        placement.AreaCode,
        placement.AreaName,
        placement.ProcessStageId,
        placement.ProcessStageCode,
        placement.ProcessStageName,
        placement.DisplayOrder);

    private static GanttRowModel BuildRow(
        PlanningWorkbenchView workbench,
        PlanningWorkbenchState state,
        ScheduleResourceLaneView lane,
        int sceneIndex,
        IReadOnlyDictionary<string, PlanningOperationWorkbenchDetail> details,
        IReadOnlyDictionary<string, ScheduledProcessOperationView> allOperations,
        IReadOnlyDictionary<string, PlanningBaselinePlacementView> baselineByKey,
        IReadOnlyDictionary<Guid, PlanningBaselinePlacementView[]> baselineByResource,
        IReadOnlyDictionary<Guid, PlanningResourceCalendarIntervalView[]> calendarsByResource)
    {
        var operations = lane.Operations
            .Where(x => x.EndUtc > state.VisibleStartUtc && x.StartUtc < state.VisibleEndUtc)
            .Where(x => Matches(x, details, state.SearchText))
            .OrderBy(x => x.StartUtc)
            .Select(operation =>
            {
                details.TryGetValue(operation.PlanningKey, out var detail);
                var clippedStart = operation.StartUtc < state.VisibleStartUtc ? state.VisibleStartUtc : operation.StartUtc;
                var clippedEnd = operation.EndUtc > state.VisibleEndUtc ? state.VisibleEndUtc : operation.EndUtc;
                var widthPercent = WidthPercent(state, clippedStart, clippedEnd);
                var execution = detail?.ExecutionStatus ?? OperationExecutionStatus.Planned;
                var eligibleResourceCount = detail is null
                    ? 0
                    : detail.ResourceOptions.Select(x => x.ResourceId).Append(operation.ResourceId).Distinct().Count();
                var accessibleCommitment = detail is null ? "commitment not returned" : detail.CommitmentState.ToString();
                var accessibleEligibility = eligibleResourceCount == 0
                    ? "eligible resources not returned"
                    : $"{eligibleResourceCount} eligible resource{(eligibleResourceCount == 1 ? string.Empty : "s")}";
                return new GanttOperationModel(
                    operation,
                    detail,
                    execution,
                    Percent(state, clippedStart),
                    widthPercent,
                    widthPercent / 100d * LogicalTimelineWidthPx,
                    OperationLabel(state.Mode, operation, detail),
                    detail?.HeatSequenceNumber is { } heatSequence
                        ? $"H{heatSequence:00}"
                        : operation.ProcessOperationType.ToString().ToUpperInvariant(),
                    $"{OperationLabel(state.Mode, operation, detail)}, {operation.ProcessOperationType}, " +
                    $"{operation.ResourceCode}, {operation.StartUtc:dd MMM HH:mm} to {operation.EndUtc:dd MMM HH:mm}, " +
                    $"{operation.QuantityMt:0.##} metric tonnes, {execution}, {accessibleCommitment}, {accessibleEligibility}",
                    eligibleResourceCount == 1,
                    RunningProgress(workbench.Plan.ReferenceTimeUtc, operation),
                    BaselineChange(operation, baselineByKey.GetValueOrDefault(operation.PlanningKey)))
                {
                    CommitmentState = detail?.CommitmentState,
                    EligibleResourceCount = eligibleResourceCount,
                    ActualStartUtc = detail?.ActualStartUtc,
                    ActualEndUtc = detail?.ActualEndUtc
                };
            })
            .ToArray();
        var baselines = (baselineByResource.GetValueOrDefault(lane.ResourceId) ?? [])
            .Where(x => x.EndUtc > state.VisibleStartUtc && x.StartUtc < state.VisibleEndUtc)
            .Select(x =>
            {
                var start = x.StartUtc < state.VisibleStartUtc ? state.VisibleStartUtc : x.StartUtc;
                var end = x.EndUtc > state.VisibleEndUtc ? state.VisibleEndUtc : x.EndUtc;
                allOperations.TryGetValue(x.PlanningKey, out var current);
                var change = current is null ? GanttBaselineChange.Removed : BaselineChange(current, x);
                return new GanttBaselineModel(
                    x,
                    Percent(state, start),
                    WidthPercent(state, start, end),
                    change,
                    current is null ? 0 : (int)Math.Round((current.StartUtc - x.StartUtc).TotalMinutes),
                    current is null ? 0 : (int)Math.Round((current.EndUtc - x.EndUtc).TotalMinutes));
            })
            .ToArray();
        var campaignSpans = operations
            .Where(x => !string.IsNullOrWhiteSpace(x.Detail?.CampaignNumber))
            .GroupBy(x => x.Detail!.CampaignNumber!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new GanttCampaignSpanModel(
                group.Key,
                group.Min(x => x.LeftPercent),
                group.Max(x => x.LeftPercent + x.WidthPercent) - group.Min(x => x.LeftPercent),
                group.Count()))
            .ToArray();
        var calendars = (calendarsByResource.GetValueOrDefault(lane.ResourceId) ?? [])
            .Where(x => x.EndUtc > state.VisibleStartUtc && x.StartUtc < state.VisibleEndUtc)
            .OrderBy(x => x.StartUtc)
            .ToArray();
        var utilization = VisibleUtilization(workbench, state, lane.ResourceId);

        return new GanttRowModel(
            sceneIndex,
            lane,
            operations,
            baselines,
            calendars,
            campaignSpans,
            utilization,
            lane.Operations.Where(x => x.StartUtc >= workbench.Plan.ReferenceTimeUtc).OrderBy(x => x.StartUtc).FirstOrDefault(),
            ExceptionCount(workbench, lane));
    }

    private static IReadOnlyList<GanttDependencyLineModel> BuildFocusedDependencyLines(
        IReadOnlyCollection<PlanningDependencyLinkView> links,
        IReadOnlyList<GanttLaneLayout> orderedLanes,
        PlanningWorkbenchState state)
    {
        if (!state.ShowDependencies || string.IsNullOrWhiteSpace(state.SelectedPlanningKey))
            return Array.Empty<GanttDependencyLineModel>();

        var placements = orderedLanes
            .SelectMany(lane => lane.Lane.Operations.Select(operation => (operation, rowIndex: lane.SceneIndex)))
            .ToDictionary(x => x.operation.PlanningKey, StringComparer.OrdinalIgnoreCase);
        if (!placements.ContainsKey(state.SelectedPlanningKey)) return Array.Empty<GanttDependencyLineModel>();

        var focusedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { state.SelectedPlanningKey };
        var pending = new Queue<string>();
        pending.Enqueue(state.SelectedPlanningKey);
        while (pending.TryDequeue(out var key))
        {
            foreach (var link in links.Where(x =>
                         x.PredecessorPlanningKey.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                         x.SuccessorPlanningKey.Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                var adjacent = link.PredecessorPlanningKey.Equals(key, StringComparison.OrdinalIgnoreCase)
                    ? link.SuccessorPlanningKey
                    : link.PredecessorPlanningKey;
                if (focusedKeys.Add(adjacent)) pending.Enqueue(adjacent);
            }
        }

        return links
            .Where(x => focusedKeys.Contains(x.PredecessorPlanningKey) && focusedKeys.Contains(x.SuccessorPlanningKey))
            .Where(x => placements.ContainsKey(x.PredecessorPlanningKey) && placements.ContainsKey(x.SuccessorPlanningKey))
            .Select(x =>
            {
                var predecessor = placements[x.PredecessorPlanningKey];
                var successor = placements[x.SuccessorPlanningKey];
                var rowHeight = state.GanttRowHeightPx;
                return new GanttDependencyLineModel(
                    x.PredecessorPlanningKey,
                    x.SuccessorPlanningKey,
                    Percent(state, predecessor.operation.EndUtc),
                    (predecessor.rowIndex + .5d) * rowHeight,
                    Percent(state, successor.operation.StartUtc),
                    (successor.rowIndex + .5d) * rowHeight,
                    x.Type,
                    x.Category,
                    x.MinimumLagMinutes,
                    x.CurrentLagMinutes,
                    x.MinimumLagMinutes is { } minimum ? x.CurrentLagMinutes - minimum : null);
            })
            .ToArray();
    }

    private sealed record GanttGroupDescriptor(
        string Key,
        string? ParentKey,
        GanttResourceGroupLevel Level,
        string Code,
        string Label);

    private sealed record GanttLaneLayout(int SceneIndex, ScheduleResourceLaneView Lane);

    private sealed record GanttHierarchyLayout(
        IReadOnlyList<GanttLaneLayout> Lanes,
        IReadOnlyList<GanttResourceGroupModel> Groups,
        int TotalRowCount);

    private static IReadOnlyList<GanttAxisTickModel> BuildTicks(PlanningWorkbenchState state)
    {
        var duration = state.VisibleEndUtc - state.VisibleStartUtc;
        var minor = duration.TotalHours switch
        {
            <= 1 => TimeSpan.FromMinutes(5),
            <= 12 => TimeSpan.FromMinutes(30),
            <= 36 => TimeSpan.FromHours(2),
            <= 96 => TimeSpan.FromHours(6),
            <= 24 * 14 => TimeSpan.FromDays(1),
            _ => TimeSpan.FromDays(2)
        };
        var ticks = new List<GanttAxisTickModel>();
        ticks.Add(new GanttAxisTickModel(
            state.VisibleStartUtc,
            0d,
            state.VisibleStartUtc.ToString("ddd dd MMM"),
            string.Empty,
            true));
        var firstTicks = (long)Math.Floor((double)state.VisibleStartUtc.Ticks / minor.Ticks) * minor.Ticks;
        var first = new DateTime(firstTicks, DateTimeKind.Utc);
        if (first < state.VisibleStartUtc) first += minor;
        for (var tick = first; tick <= state.VisibleEndUtc; tick += minor)
        {
            var major = duration.TotalDays <= 2 ? tick.Hour == 0 && tick.Minute == 0 : tick.Day == 1;
            ticks.Add(new GanttAxisTickModel(
                tick,
                Percent(state, tick),
                duration.TotalDays <= 2 ? tick.ToString("ddd dd MMM") : tick.ToString("MMM yyyy"),
                duration.TotalHours <= 36 ? tick.ToString("HH:mm") : tick.ToString("dd MMM"),
                major));
        }
        return ticks;
    }

    private static bool Matches(
        ScheduledProcessOperationView operation,
        IReadOnlyDictionary<string, PlanningOperationWorkbenchDetail> details,
        string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        details.TryGetValue(operation.PlanningKey, out var detail);
        return Contains(operation.ResourceCode, query) ||
               Contains(operation.ResourceName, query) ||
               Contains(operation.GradeCode, query) ||
               Contains(operation.CrossSectionCode, query) ||
               Contains(operation.ProcessOperationType.ToString(), query) ||
               Contains(detail?.CampaignNumber, query) ||
               detail?.ProductionOrderNumbers.Any(x => Contains(x, query)) == true;
    }

    private static string OperationLabel(
        PlanningWorkbenchMode mode,
        ScheduledProcessOperationView operation,
        PlanningOperationWorkbenchDetail? detail) => mode switch
        {
            PlanningWorkbenchMode.Campaigns => $"{detail?.CampaignNumber ?? "Campaign"} · H{detail?.HeatSequenceNumber:00}",
            PlanningWorkbenchMode.Plan => detail?.ProductionOrderNumbers.FirstOrDefault() ?? $"{operation.ProcessOperationType} · {operation.GradeCode}",
            _ => $"{operation.ProcessOperationType} · {operation.GradeCode}"
        };

    private static double RunningProgress(DateTime referenceTimeUtc, ScheduledProcessOperationView operation)
    {
        var total = (operation.EndUtc - operation.StartUtc).TotalMinutes;
        if (total <= 0d) return 0d;
        return Math.Clamp((referenceTimeUtc - operation.StartUtc).TotalMinutes / total * 100d, 0d, 100d);
    }

    private static GanttBaselineChange BaselineChange(
        ScheduledProcessOperationView operation,
        PlanningBaselinePlacementView? baseline)
    {
        if (baseline is null) return GanttBaselineChange.Added;
        if (baseline.ResourceId != operation.ResourceId) return GanttBaselineChange.ResourceChanged;
        return baseline.StartUtc == operation.StartUtc && baseline.EndUtc == operation.EndUtc
            ? GanttBaselineChange.Unchanged
            : GanttBaselineChange.TimeMoved;
    }

    public static double Percent(PlanningWorkbenchState state, DateTime value)
    {
        var total = Math.Max(1d, (state.VisibleEndUtc - state.VisibleStartUtc).TotalMinutes);
        return Math.Clamp((value - state.VisibleStartUtc).TotalMinutes / total * 100d, 0d, 100d);
    }

    public static double WidthPercent(PlanningWorkbenchState state, DateTime start, DateTime end)
    {
        var clippedStart = start < state.VisibleStartUtc ? state.VisibleStartUtc : start;
        var clippedEnd = end > state.VisibleEndUtc ? state.VisibleEndUtc : end;
        var total = Math.Max(1d, (state.VisibleEndUtc - state.VisibleStartUtc).TotalMinutes);
        return Math.Max(0d, (clippedEnd - clippedStart).TotalMinutes / total * 100d);
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
}
