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

        Assert.Equal(2, scene.TotalRowCount);
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

    private static PlanningWorkbenchView Workbench(
        IReadOnlyCollection<ScheduleResourceLaneView> lanes,
        IReadOnlyCollection<PlanningBaselinePlacementView>? baselines = null,
        IReadOnlyCollection<PlanningOperationWorkbenchDetail>? details = null,
        IReadOnlyCollection<PlanningDependencyLinkView>? dependencies = null)
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
            Array.Empty<PlanningWorkbenchException>(),
            details ?? Array.Empty<PlanningOperationWorkbenchDetail>(),
            dependencies ?? Array.Empty<PlanningDependencyLinkView>(),
            Array.Empty<PlanningResourceCalendarIntervalView>(),
            baselines ?? Array.Empty<PlanningBaselinePlacementView>(),
            Array.Empty<PlanningCapacityBucketView>());
    }
}
