using System.Diagnostics;
using APS.Application;
using APS.Domain;
using APS.UI.Components.PlanningWorkbench.Gantt;
using APS.UI.State;
using Xunit.Abstractions;

namespace APS.UI.Tests;

public sealed class GanttPerformanceBudgetTests(ITestOutputHelper output)
{
    private static readonly DateTime Start = new(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Ten_thousand_operation_scene_stays_inside_mount_and_construction_budgets()
    {
        var lanes = Enumerable.Range(0, 100).Select(ResourceLane).ToArray();
        var workbench = Workbench(lanes);
        var state = new PlanningWorkbenchState();
        state.SetPlanWindow(Start, Start.AddDays(8), Start, Start.AddDays(5));
        state.Viewport.FocusRange(Start, Start.AddDays(1));
        state.Viewport.SetVisibleRowRange(firstVisibleRow: 20, visibleRowCount: 12, overscan: 3);

        _ = GanttModels.BuildScene(workbench, state); // JIT warm-up is not part of interactive rendering.
        var stopwatch = Stopwatch.StartNew();
        var scene = GanttModels.BuildScene(workbench, state);
        stopwatch.Stop();

        output.WriteLine($"10k scene: {stopwatch.Elapsed.TotalMilliseconds:0.0} ms, {scene.VisibleOperationCount} mounted operation models, {scene.Rows.Count}/{scene.TotalRowCount} mounted rows");
        Assert.Equal(10_000, workbench.Schedule.OperationCount);
        Assert.True(scene.VisibleOperationCount < 500, $"Mounted {scene.VisibleOperationCount} operation models; budget is <500.");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1.5), $"Scene construction took {stopwatch.Elapsed.TotalMilliseconds:0.0} ms; budget is <1500 ms after data is available.");
    }

    private static ScheduleResourceLaneView ResourceLane(int resourceIndex)
    {
        var resourceId = Guid.NewGuid();
        var operations = Enumerable.Range(0, 100)
            .Select(operationIndex => new ScheduledProcessOperationView(
                Guid.NewGuid(),
                $"PERF-{resourceIndex:000}-{operationIndex:000}",
                Guid.NewGuid(),
                ProcessOperationType.Eaf,
                resourceId,
                $"EAF-{resourceIndex:000}",
                $"Performance resource {resourceIndex:000}",
                ProcessUnitType.Eaf,
                ResourceOperatingState.Available,
                Start.AddHours(operationIndex),
                Start.AddHours(operationIndex).AddMinutes(50),
                70m,
                "SAE1008",
                "BLT-150"))
            .ToArray();
        return new ScheduleResourceLaneView(
            resourceId,
            $"EAF-{resourceIndex:000}",
            $"Performance resource {resourceIndex:000}",
            ProcessUnitType.Eaf,
            ResourceOperatingState.Available,
            1d,
            operations,
            PlantId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PlantCode: "PERF",
            PlantName: "Performance plant",
            AreaId: DeterministicGuid(resourceIndex / 20 + 1),
            AreaCode: $"AREA-{resourceIndex / 20 + 1:00}",
            AreaName: $"Performance area {resourceIndex / 20 + 1:00}",
            ProcessStageId: DeterministicGuid(100 + resourceIndex / 5 + 1),
            ProcessStageCode: $"STAGE-{resourceIndex / 5 + 1:00}",
            ProcessStageName: $"Performance stage {resourceIndex / 5 + 1:00}",
            DisplayOrder: resourceIndex);
    }

    private static Guid DeterministicGuid(int value) => new(value, 0, 0, new byte[8]);

    private static PlanningWorkbenchView Workbench(IReadOnlyCollection<ScheduleResourceLaneView> lanes)
    {
        var plan = new PlanContextView(
            Guid.NewGuid(), "PERF-10K", null, PlanVersionStatus.Feasible, PlanTriggerType.Manual,
            Start, Start, Start, Start.AddDays(8), "Performance", 0, true, false, null);
        return new PlanningWorkbenchView(
            plan,
            null,
            new DemandSupplyView(plan, 0m, 0m, 0m, 0m, 0m, 0m, 0, 0, Array.Empty<DemandSupplyRowView>()),
            new CampaignStudioView(plan, 0, 0, 0m, 0m, 0m, Array.Empty<CampaignView>()),
            new FiniteScheduleWorkspaceView(plan, Start, Start.AddDays(8), 10_000, lanes.Count, lanes),
            new MaterialFlowWorkspaceView(plan, Array.Empty<MaterialFlowPoolView>(), Array.Empty<MaterialFlowReservationView>()),
            null,
            new PlanningQueueView(0, 0, 0, 0, 0, 0, 0),
            Array.Empty<PlanningWorkbenchException>(),
            Array.Empty<PlanningOperationWorkbenchDetail>(),
            Array.Empty<PlanningDependencyLinkView>(),
            Array.Empty<PlanningResourceCalendarIntervalView>(),
            Array.Empty<PlanningBaselinePlacementView>(),
            Array.Empty<PlanningCapacityBucketView>());
    }
}
