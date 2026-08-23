using APS.Application;
using APS.Domain;
using APS.UI.Components.PlanningWorkbench.Gantt;
using APS.UI.State;

namespace APS.UI.Tests;

public sealed class GanttSceneTests
{
    private static readonly DateTime Start = new(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Scene_mounts_only_the_visible_row_window_with_overscan_and_clips_operations_by_time()
    {
        var lanes = Enumerable.Range(0, 30)
            .Select(index => Lane(index, Start.AddHours(index == 10 ? 30 : 2)))
            .ToArray();
        var workbench = Workbench(lanes);
        var state = State();
        state.Viewport.SetVisibleRowRange(firstVisibleRow: 10, visibleRowCount: 5, overscan: 2);

        var scene = GanttModels.BuildScene(workbench, state);

        Assert.Equal(30, scene.TotalRowCount);
        Assert.Equal(Enumerable.Range(8, 9), scene.Rows.Select(x => x.SceneIndex));
        Assert.Empty(Assert.Single(scene.Rows, x => x.SceneIndex == 10).Operations);
        Assert.All(scene.Rows.Where(x => x.SceneIndex != 10), row => Assert.Single(row.Operations));
    }

    [Fact]
    public void Authoritative_resource_hierarchy_adds_shared_group_rows_and_collapses_descendants()
    {
        var lanes = new[]
        {
            HierarchicalLane(1, "SMS", "MELT", "EAF"),
            HierarchicalLane(2, "SMS", "MELT", "EAF")
        };
        var state = State();

        var expanded = GanttModels.BuildScene(Workbench(lanes), state);

        Assert.Equal(5, expanded.TotalRowCount);
        Assert.Equal(2, expanded.ResourceCount);
        Assert.Equal([0, 1, 2], expanded.ResourceGroups.Select(x => x.SceneIndex));
        Assert.Equal([3, 4], expanded.Rows.Select(x => x.SceneIndex));
        var processGroup = Assert.Single(expanded.ResourceGroups, x => x.Level == GanttResourceGroupLevel.ProcessStage);
        Assert.Equal("EAF", processGroup.Code);
        Assert.Equal(2, processGroup.ResourceCount);

        state.ToggleResourceGroup(processGroup.Key);
        var collapsed = GanttModels.BuildScene(Workbench(lanes), state);

        Assert.Equal(3, collapsed.TotalRowCount);
        Assert.Equal(2, collapsed.ResourceCount);
        Assert.Empty(collapsed.Rows);
        Assert.True(Assert.Single(collapsed.ResourceGroups, x => x.Key == processGroup.Key).IsCollapsed);
    }

    [Fact]
    public void Resource_group_preferences_replace_and_deduplicate_state()
    {
        var state = State();

        state.SetCollapsedResourceGroups(["plant:one", "PLANT:ONE", "stage:two"]);

        Assert.Equal(2, state.CollapsedResourceGroups.Count);
        Assert.Contains("plant:one", state.CollapsedResourceGroups, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("stage:two", state.CollapsedResourceGroups, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resource_sorting_is_local_to_authoritative_process_groups_and_filter_count_preserves_total()
    {
        var firstGroupLow = HierarchicalLane(1, "SMS", "MELT", "EAF") with { OccupiedHours = 2d };
        var firstGroupHigh = HierarchicalLane(2, "SMS", "MELT", "EAF") with { OccupiedHours = 9d };
        var secondGroup = HierarchicalLane(3, "SMS", "MELT", "LRF") with
        {
            ProcessStageId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            ProcessStageName = "Ladle refining",
            OccupiedHours = 5d
        };
        var state = State();
        state.SetGridSort(GanttGridSortColumn.Busy, descending: true);

        var sorted = GanttModels.BuildScene(Workbench([firstGroupLow, firstGroupHigh, secondGroup]), state);

        Assert.Equal([firstGroupHigh.ResourceCode, firstGroupLow.ResourceCode, secondGroup.ResourceCode], sorted.Rows.Select(x => x.Lane.ResourceCode));
        state.SetSearch(firstGroupLow.ResourceCode);
        var filtered = GanttModels.BuildScene(Workbench([firstGroupLow, firstGroupHigh, secondGroup]), state);
        Assert.Equal(1, filtered.ResourceCount);
        Assert.Equal(3, filtered.TotalResourceCount);
    }

    [Fact]
    public void Filtered_scene_warns_when_a_critical_resource_exception_is_hidden()
    {
        var visible = HierarchicalLane(1, "SMS", "MELT", "EAF");
        var hidden = HierarchicalLane(2, "SMS", "MELT", "EAF");
        var exception = new PlanningWorkbenchException(
            "RESOURCE-DOWN",
            PlanningWorkbenchExceptionKind.ResourceUnavailable,
            PlanningWorkbenchExceptionSeverity.Critical,
            "Resource unavailable",
            "Capacity is lost",
            new PlannerEntityRef(PlannerEntityType.Resource, hidden.ResourceId, hidden.ResourceCode));
        var state = State();
        state.SetSearch(visible.ResourceCode);

        var scene = GanttModels.BuildScene(Workbench([visible, hidden], exceptions: [exception]), state);

        Assert.Equal(1, scene.HiddenCriticalExceptionCount);
    }

    [Fact]
    public void Resource_changed_baseline_stays_on_its_original_readonly_lane()
    {
        var currentLane = Lane(1, Start.AddHours(2));
        var originalResourceId = Guid.NewGuid();
        var baseline = new PlanningBaselinePlacementView(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "HEAT-01:LRF",
            originalResourceId,
            "LRF-01",
            "Original ladle furnace",
            ProcessUnitType.Lrf,
            ResourceOperatingState.Available,
            ResourceSchedulingMode.Disjunctive,
            Start.AddHours(1),
            Start.AddHours(2),
            ProcessOperationType.Lrf,
            "SAE1008",
            "BLT-150",
            Guid.NewGuid(),
            "MELT",
            "Melt Shop",
            Guid.NewGuid(),
            "SMS",
            "Steel Melting Shop",
            Guid.NewGuid(),
            "LRF",
            "Ladle refining",
            20_020_000);

        var scene = GanttModels.BuildScene(Workbench([currentLane], [baseline]), State());

        Assert.Equal(2, scene.ResourceCount);
        Assert.Equal(5, scene.TotalRowCount);
        var originalLane = Assert.Single(scene.Rows, x => x.Lane.ResourceId == originalResourceId);
        Assert.Empty(originalLane.Operations);
        Assert.Single(originalLane.Baselines);
        Assert.Equal("LRF-01", originalLane.Lane.ResourceCode);
    }

    [Fact]
    public void Baseline_comparison_classifies_time_resource_added_and_removed_semantics()
    {
        var unchanged = Lane(1, Start.AddHours(2));
        var moved = Lane(2, Start.AddHours(5));
        var resourceChanged = Lane(3, Start.AddHours(7));
        var added = Lane(4, Start.AddHours(9));
        var removedResourceId = Guid.NewGuid();
        var baselines = new[]
        {
            BaselineFor(unchanged.Operations.Single(), unchanged.ResourceId, Start.AddHours(2)),
            BaselineFor(moved.Operations.Single(), moved.ResourceId, Start.AddHours(4)),
            BaselineFor(resourceChanged.Operations.Single(), Guid.NewGuid(), Start.AddHours(7)),
            BaselineFor(null, removedResourceId, Start.AddHours(11), "REMOVED-OP")
        };

        var scene = GanttModels.BuildScene(Workbench([unchanged, moved, resourceChanged, added], baselines), State());

        Assert.Equal(GanttBaselineChange.Unchanged, FindOperation(scene, unchanged).BaselineChange);
        Assert.Equal(GanttBaselineChange.TimeMoved, FindOperation(scene, moved).BaselineChange);
        Assert.Equal(GanttBaselineChange.ResourceChanged, FindOperation(scene, resourceChanged).BaselineChange);
        Assert.Equal(GanttBaselineChange.Added, FindOperation(scene, added).BaselineChange);
        Assert.Contains(scene.Rows.SelectMany(x => x.Baselines), x => x.Change == GanttBaselineChange.Removed);
    }

    [Fact]
    public void Campaign_spans_are_derived_from_canonical_operation_details()
    {
        var lane = Lane(7, Start.AddHours(3));
        var operation = lane.Operations.Single();
        var detail = new PlanningOperationWorkbenchDetail(
            operation.OperationSnapshotId,
            operation.PlanningKey,
            operation.SourceEntityId,
            OperationAssignmentCommitmentState.Flexible,
            OperationExecutionStatus.Planned,
            null,
            null,
            0m,
            Array.Empty<string>(),
            Array.Empty<PlanningOperationResourceOptionView>(),
            "CMP-STEEL-01",
            3,
            ["PO-1001"]);

        var scene = GanttModels.BuildScene(Workbench([lane], details: [detail]), State());

        var span = Assert.Single(Assert.Single(scene.Rows).CampaignSpans);
        Assert.Equal("CMP-STEEL-01", span.CampaignNumber);
        Assert.Equal(1, span.OperationCount);
    }

    [Fact]
    public void Operation_model_exposes_authoritative_commitment_and_resource_flexibility()
    {
        var lane = Lane(8, Start.AddHours(3));
        var operation = lane.Operations.Single();
        var detail = new PlanningOperationWorkbenchDetail(
            operation.OperationSnapshotId,
            operation.PlanningKey,
            operation.SourceEntityId,
            OperationAssignmentCommitmentState.Firm,
            OperationExecutionStatus.Planned,
            null,
            null,
            0m,
            Array.Empty<string>(),
            [
                new PlanningOperationResourceOptionView(lane.ResourceId, lane.ResourceCode, lane.ResourceName, 60, 0, true, "ROUTE"),
                new PlanningOperationResourceOptionView(Guid.NewGuid(), "EAF-ALT", "Alternate furnace", 60, 5, false, "ROUTE")
            ],
            null,
            null,
            Array.Empty<string>());

        var model = Assert.Single(Assert.Single(GanttModels.BuildScene(Workbench([lane], details: [detail]), State()).Rows).Operations);

        Assert.Equal(OperationAssignmentCommitmentState.Firm, model.CommitmentState);
        Assert.Equal(2, model.EligibleResourceCount);
        Assert.Contains("Firm", model.AccessibleName);
        Assert.Contains("2 eligible resources", model.AccessibleName);
    }

    [Fact]
    public void Operation_model_preserves_returned_actual_geometry_without_inference()
    {
        var lane = Lane(9, Start.AddHours(3));
        var operation = lane.Operations.Single();
        var actualStart = operation.StartUtc.AddMinutes(12);
        var actualEnd = operation.EndUtc.AddMinutes(18);
        var detail = new PlanningOperationWorkbenchDetail(
            operation.OperationSnapshotId,
            operation.PlanningKey,
            operation.SourceEntityId,
            OperationAssignmentCommitmentState.Completed,
            OperationExecutionStatus.Completed,
            actualStart,
            actualEnd,
            operation.QuantityMt,
            Array.Empty<string>(),
            Array.Empty<PlanningOperationResourceOptionView>(),
            null,
            null,
            Array.Empty<string>());

        var model = Assert.Single(Assert.Single(GanttModels.BuildScene(Workbench([lane], details: [detail]), State()).Rows).Operations);

        Assert.Equal(actualStart, model.ActualStartUtc);
        Assert.Equal(actualEnd, model.ActualEndUtc);
    }

    [Fact]
    public void Focused_dependency_geometry_survives_row_virtualization_and_preserves_lag_semantics()
    {
        var lanes = new[] { Lane(0, Start.AddHours(1)), Lane(1, Start.AddHours(2)), Lane(2, Start.AddHours(3)) };
        var link = new PlanningDependencyLinkView(
            lanes[0].Operations.Single().OperationSnapshotId,
            lanes[0].Operations.Single().PlanningKey,
            lanes[2].Operations.Single().OperationSnapshotId,
            lanes[2].Operations.Single().PlanningKey,
            PlanningDependencyType.FinishStart,
            PlanningDependencyCategory.Routing,
            30,
            75);
        var state = State();
        state.SelectOperation(lanes[0].Operations.Single().PlanningKey);
        state.ToggleDependencies();
        state.Viewport.SetVisibleRowRange(1, 1, overscan: 0);

        var scene = GanttModels.BuildScene(Workbench(lanes, dependencies: [link]), state);

        Assert.Single(scene.Rows);
        var edge = Assert.Single(scene.DependencyLines);
        Assert.Equal(PlanningDependencyType.FinishStart, edge.Type);
        Assert.Equal(PlanningDependencyCategory.Routing, edge.Category);
        Assert.Equal(30, edge.MinimumLagMinutes);
        Assert.Equal(75, edge.CurrentLagMinutes);
        Assert.Equal(45, edge.HeadroomMinutes);
        Assert.Equal(.5d * state.GanttRowHeightPx, edge.StartYpx);
        Assert.Equal(2.5d * state.GanttRowHeightPx, edge.EndYpx);
    }

    [Fact]
    public void Focused_dependency_geometry_uses_the_same_hierarchy_adjusted_row_indices()
    {
        var lanes = new[]
        {
            HierarchicalLane(1, "SMS", "MELT", "EAF"),
            HierarchicalLane(2, "SMS", "MELT", "EAF")
        };
        var link = new PlanningDependencyLinkView(
            lanes[0].Operations.Single().OperationSnapshotId,
            lanes[0].Operations.Single().PlanningKey,
            lanes[1].Operations.Single().OperationSnapshotId,
            lanes[1].Operations.Single().PlanningKey,
            PlanningDependencyType.FinishStart,
            PlanningDependencyCategory.Routing,
            0,
            0);
        var state = State();
        state.SelectOperation(lanes[0].Operations.Single().PlanningKey, lanes[0].ResourceId);
        state.ToggleDependencies();

        var scene = GanttModels.BuildScene(Workbench(lanes, dependencies: [link]), state);

        var edge = Assert.Single(scene.DependencyLines);
        Assert.Equal(3.5d * state.GanttRowHeightPx, edge.StartYpx);
        Assert.Equal(4.5d * state.GanttRowHeightPx, edge.EndYpx);
    }

    private static GanttOperationModel FindOperation(GanttScene scene, ScheduleResourceLaneView lane) =>
        scene.Rows.SelectMany(x => x.Operations).Single(x => x.Operation.PlanningKey == lane.Operations.Single().PlanningKey);

    private static PlanningBaselinePlacementView BaselineFor(
        ScheduledProcessOperationView? operation,
        Guid resourceId,
        DateTime start,
        string? planningKey = null) => new(
        Guid.NewGuid(),
        operation?.OperationSnapshotId ?? Guid.NewGuid(),
        planningKey ?? operation!.PlanningKey,
        resourceId,
        "BASE-01",
        "Baseline resource",
        ProcessUnitType.Eaf,
        ResourceOperatingState.Available,
        ResourceSchedulingMode.Disjunctive,
        start,
        start.AddHours(1),
        ProcessOperationType.Eaf,
        "SAE1008",
        "BLT-150",
        null, null, null, null, null, null, null, null, null, 1);

    private static PlanningWorkbenchState State()
    {
        var state = new PlanningWorkbenchState();
        state.SetPlanWindow(Start, Start.AddDays(2), Start, Start.AddDays(1));
        return state;
    }

    private static ScheduleResourceLaneView Lane(int index, DateTime operationStart)
    {
        var resourceId = Guid.NewGuid();
        var operation = new ScheduledProcessOperationView(
            Guid.NewGuid(),
            $"OP-{index:000}",
            Guid.NewGuid(),
            ProcessOperationType.Eaf,
            resourceId,
            $"EAF-{index:00}",
            $"Electric furnace {index:00}",
            ProcessUnitType.Eaf,
            ResourceOperatingState.Available,
            operationStart,
            operationStart.AddHours(1),
            70m,
            "SAE1008",
            "BLT-150");
        return new ScheduleResourceLaneView(
            resourceId,
            operation.ResourceCode,
            operation.ResourceName,
            operation.ProcessUnitType,
            operation.ResourceOperatingState,
            1d,
            [operation],
            DisplayOrder: index);
    }

    private static ScheduleResourceLaneView HierarchicalLane(int index, string plantCode, string areaCode, string processStageCode) =>
        Lane(index, Start.AddHours(index)) with
        {
            PlantId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PlantCode = plantCode,
            PlantName = "Steel plant",
            AreaId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            AreaCode = areaCode,
            AreaName = "Melt shop",
            ProcessStageId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ProcessStageCode = processStageCode,
            ProcessStageName = "Electric arc furnace"
        };

    private static PlanningWorkbenchView Workbench(
        IReadOnlyCollection<ScheduleResourceLaneView> lanes,
        IReadOnlyCollection<PlanningBaselinePlacementView>? baselines = null,
        IReadOnlyCollection<PlanningOperationWorkbenchDetail>? details = null,
        IReadOnlyCollection<PlanningDependencyLinkView>? dependencies = null,
        IReadOnlyCollection<PlanningWorkbenchException>? exceptions = null)
    {
        var plan = new PlanContextView(
            Guid.NewGuid(),
            "PLAN-TEST",
            null,
            PlanVersionStatus.Feasible,
            PlanTriggerType.Manual,
            Start,
            Start,
            Start,
            Start.AddDays(2),
            "Optimal",
            0,
            true,
            false,
            null);
        var demand = new DemandSupplyView(
            plan, 0m, 0m, 0m, 0m, 0m, 0m, 0, 0, Array.Empty<DemandSupplyRowView>());
        var campaigns = new CampaignStudioView(
            plan, 0, 0, 0m, 0m, 0m, Array.Empty<CampaignView>());
        var schedule = new FiniteScheduleWorkspaceView(
            plan,
            Start,
            Start.AddDays(2),
            lanes.Sum(x => x.Operations.Count),
            lanes.Count,
            lanes);
        var material = new MaterialFlowWorkspaceView(
            plan,
            Array.Empty<MaterialFlowPoolView>(),
            Array.Empty<MaterialFlowReservationView>());
        return new PlanningWorkbenchView(
            plan,
            null,
            demand,
            campaigns,
            schedule,
            material,
            null,
            new PlanningQueueView(0, 0, 0, 0, 0, 0, 0),
            exceptions ?? Array.Empty<PlanningWorkbenchException>(),
            details ?? Array.Empty<PlanningOperationWorkbenchDetail>(),
            dependencies ?? Array.Empty<PlanningDependencyLinkView>(),
            Array.Empty<PlanningResourceCalendarIntervalView>(),
            baselines ?? Array.Empty<PlanningBaselinePlacementView>(),
            Array.Empty<PlanningCapacityBucketView>());
    }
}
