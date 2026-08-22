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
        IReadOnlyCollection<PlanningBaselinePlacementView>? baselines = null)
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
            Array.Empty<PlanningOperationWorkbenchDetail>(),
            Array.Empty<PlanningDependencyLinkView>(),
            Array.Empty<PlanningResourceCalendarIntervalView>(),
            baselines ?? Array.Empty<PlanningBaselinePlacementView>(),
            Array.Empty<PlanningCapacityBucketView>());
    }
}
