using APS.Application;
using APS.UI.State;

namespace APS.UI.Tests;

public sealed class PlanningWorkbenchStateTests
{
    [Fact]
    public void Workbench_opens_in_plan_mode_with_visual_noise_disabled()
    {
        var state = new PlanningWorkbenchState();

        Assert.Equal(PlanningWorkbenchMode.Plan, state.Mode);
        Assert.Equal(PlanningScenarioIntent.Existing, state.ScenarioIntent);
        Assert.False(state.ShowDependencies);
    }

    [Fact]
    public void Shortcut_panel_is_transient_workbench_state()
    {
        var state = new PlanningWorkbenchState();

        state.ToggleShortcutPanel();
        Assert.True(state.ShortcutPanelOpen);

        state.SetShortcutPanel(false);
        Assert.False(state.ShortcutPanelOpen);
    }

    [Fact]
    public void Grid_columns_sort_and_width_are_view_state_only_and_safely_bounded()
    {
        var state = new PlanningWorkbenchState();

        state.ToggleGridColumn(GanttGridColumn.Exceptions);
        state.ToggleGridColumn(GanttGridColumn.Resource);
        state.SetGridColumnWidth(GanttGridColumn.Resource, 10_000);
        state.SetGridSort(GanttGridSortColumn.Load);
        state.SetGridSort(GanttGridSortColumn.Load);

        Assert.Contains(GanttGridColumn.Resource, state.VisibleGridColumns);
        Assert.Contains(GanttGridColumn.Exceptions, state.VisibleGridColumns);
        Assert.Equal(240, state.GridColumnWidths[GanttGridColumn.Resource]);
        Assert.Equal(GanttGridSortColumn.Load, state.GridSortColumn);
        Assert.True(state.GridSortDescending);
    }

    [Fact]
    public void Released_plan_is_read_only_until_recovery_is_started()
    {
        var state = new PlanningWorkbenchState();

        state.SetReleasedPlan(true);

        Assert.False(state.CanEditSchedule);
        Assert.True(state.CanStartRecovery);

        state.StartRecovery();

        Assert.Equal(PlanningWorkbenchMode.Recovery, state.Mode);
        Assert.Equal(PlanningScenarioIntent.Recovery, state.ScenarioIntent);
        Assert.True(state.CanEditSchedule);
        Assert.False(state.CanStartRecovery);
    }

    [Fact]
    public void Released_baseline_can_preview_recovery_mode_without_starting_a_recovery_scenario()
    {
        var state = new PlanningWorkbenchState();
        state.SetReleasedPlan(true);

        state.SetMode(PlanningWorkbenchMode.Recovery);

        Assert.Equal(PlanningWorkbenchMode.Recovery, state.Mode);
        Assert.Equal(PlanningScenarioIntent.Existing, state.ScenarioIntent);
        Assert.False(state.CanEditSchedule);
        Assert.True(state.CanStartRecovery);
    }

    [Fact]
    public void Released_plan_can_be_cloned_into_an_editable_planning_scenario()
    {
        var state = new PlanningWorkbenchState();
        state.SetReleasedPlan(true);

        state.StartPlanningScenario();

        Assert.Equal(PlanningWorkbenchMode.Plan, state.Mode);
        Assert.Equal(PlanningScenarioIntent.Clone, state.ScenarioIntent);
        Assert.True(state.CanEditSchedule);
    }

    [Fact]
    public void Workflow_stage_selects_one_contextual_queue_and_preserves_the_current_operation()
    {
        var state = new PlanningWorkbenchState();
        state.SelectOperation("EAF:HEAT-01");

        state.SetMode(PlanningWorkbenchMode.Campaigns);

        Assert.Equal("EAF:HEAT-01", state.SelectedPlanningKey);
        Assert.Equal(PlanningWorkbenchQueueContent.Campaigns, state.QueueContent);

        state.SetMode(PlanningWorkbenchMode.Execution);
        Assert.Equal(PlanningWorkbenchQueueContent.Exceptions, state.QueueContent);

        state.SetMode(PlanningWorkbenchMode.Plan);
        Assert.Equal(PlanningWorkbenchQueueContent.Demand, state.QueueContent);
    }

    [Fact]
    public void Zoom_and_pan_keep_a_valid_visible_window()
    {
        var state = new PlanningWorkbenchState();
        var start = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc);
        state.SetPlanWindow(start, start.AddDays(10));

        state.SetZoom(PlanningWorkbenchZoom.Day);
        Assert.Equal(TimeSpan.FromDays(1), state.VisibleEndUtc - state.VisibleStartUtc);

        var before = state.VisibleStartUtc;
        state.Pan(0.5);
        Assert.True(state.VisibleStartUtc > before);
        Assert.True(state.VisibleEndUtc <= state.PlanEndUtc);
    }

    [Fact]
    public void Capacity_focus_owns_resource_time_context_and_clear_focus_removes_it()
    {
        var state = new PlanningWorkbenchState();
        var start = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc);
        var resourceId = Guid.NewGuid();
        state.SetPlanWindow(start, start.AddDays(3));

        state.FocusCapacity(resourceId, start.AddHours(4), start.AddHours(8));

        Assert.Equal(resourceId, state.CapacityFocus?.ResourceId);
        Assert.Equal(resourceId, state.SelectedResourceId);
        Assert.Equal(start.AddHours(4), state.VisibleStartUtc);
        Assert.Equal(start.AddHours(8), state.VisibleEndUtc);

        state.ClearFocus();
        Assert.Null(state.CapacityFocus);
        Assert.Null(state.SelectedResourceId);
    }

    [Fact]
    public void Resource_and_operation_selection_have_one_explicit_focus_owner()
    {
        var state = new PlanningWorkbenchState();
        var firstResource = Guid.NewGuid();
        var secondResource = Guid.NewGuid();

        state.SelectResource(firstResource);
        Assert.Equal(firstResource, state.SelectedResourceId);
        Assert.Null(state.SelectedPlanningKey);

        state.SelectOperation("EAF:HEAT-01", secondResource);
        Assert.Equal(secondResource, state.SelectedResourceId);
        Assert.Equal("EAF:HEAT-01", state.SelectedPlanningKey);

        state.SelectResource(firstResource);
        Assert.Equal(firstResource, state.SelectedResourceId);
        Assert.Null(state.SelectedPlanningKey);
    }

    [Fact]
    public void Ctrl_style_selection_toggles_operations_without_losing_the_primary_focus()
    {
        var state = new PlanningWorkbenchState();
        var firstResource = Guid.NewGuid();
        var secondResource = Guid.NewGuid();
        state.SelectOperation("OP-1", firstResource);

        state.ToggleOperationSelection("OP-2", secondResource);

        Assert.Equal(2, state.SelectedPlanningKeys.Count);
        Assert.Equal("OP-2", state.SelectedPlanningKey);
        Assert.Equal(2, state.SelectedResourceIds.Count);

        state.ToggleOperationSelection("OP-2", secondResource);

        Assert.Single(state.SelectedPlanningKeys);
        Assert.Equal("OP-1", state.SelectedPlanningKey);
        Assert.Equal(firstResource, state.SelectedResourceId);
    }

    [Fact]
    public void Contiguous_selection_replaces_the_set_and_bulk_proposal_is_one_staged_unit()
    {
        var state = new PlanningWorkbenchState();
        var resource = Guid.NewGuid();
        state.SelectOperationRange(
            [("OP-1", resource), ("OP-2", resource), ("OP-3", resource)],
            "OP-3");
        var proposal = new PlanningBulkMoveProposal(
            Guid.NewGuid(),
            [
                new PlanningBulkMoveItem("OP-1", resource, DateTime.UtcNow),
                new PlanningBulkMoveItem("OP-2", resource, DateTime.UtcNow.AddHours(1)),
                new PlanningBulkMoveItem("OP-3", resource, DateTime.UtcNow.AddHours(2))
            ],
            "TEST");

        state.StageBulkMove(proposal);

        Assert.Equal(3, state.SelectedPlanningKeys.Count);
        Assert.Equal("OP-3", state.SelectedPlanningKey);
        Assert.Same(proposal, state.StagedBulkMove);
        Assert.Null(state.StagedMove);
    }

    [Fact]
    public void Compare_subrow_expands_the_shared_row_height_without_changing_density()
    {
        var state = new PlanningWorkbenchState();
        var normal = state.GanttRowHeightPx;

        state.SetBaselineMode(GanttBaselineMode.CompareSubrow);

        Assert.Equal(GanttDensity.Standard, state.Viewport.Density);
        Assert.Equal(normal + 20, state.GanttRowHeightPx);
    }

    [Fact]
    public void Staged_proposal_survives_zoom_and_row_virtualization_with_unchanged_domain_geometry()
    {
        var state = new PlanningWorkbenchState();
        var start = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc);
        state.SetPlanWindow(start, start.AddDays(10));
        var proposal = new APS.Application.PlanningMoveProposal(Guid.NewGuid(), "OP-100", Guid.NewGuid(), start.AddHours(9), "TEST");
        state.StageMove(proposal);

        state.SetZoom(PlanningWorkbenchZoom.Day);
        state.Viewport.SetVisibleRowRange(50, 10);

        var staged = Assert.IsType<APS.Application.PlanningMoveProposal>(state.StagedMove);
        Assert.Same(proposal, staged);
        Assert.Equal(start.AddHours(9), staged.TargetStartUtc);
        Assert.Equal("OP-100", state.SelectedPlanningKey);
    }

    [Fact]
    public void Fit_frames_the_scheduled_operations_instead_of_the_full_plan_horizon()
    {
        var state = new PlanningWorkbenchState();
        var planStart = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var planEnd = planStart.AddDays(11);
        var scheduleStart = planStart;
        var scheduleEnd = planStart.AddHours(27);

        state.SetPlanWindow(planStart, planEnd, scheduleStart, scheduleEnd);

        Assert.Equal(PlanningWorkbenchZoom.Fit, state.Zoom);
        Assert.True(state.VisibleStartUtc <= scheduleStart);
        Assert.True(state.VisibleEndUtc >= scheduleEnd);
        Assert.True(state.VisibleEndUtc < planEnd);
        Assert.True(state.VisibleEndUtc - state.VisibleStartUtc <= TimeSpan.FromHours(31));
    }

    [Fact]
    public void Applied_plan_history_supports_semantic_undo_and_redo()
    {
        var state = new PlanningWorkbenchState();
        var baseline = Guid.NewGuid();
        var child = Guid.NewGuid();

        state.RecordAppliedPlan(baseline, child, "Move HEAT-104 · LRF-01 → LRF-02 · +45 min");

        Assert.True(state.CanUndo);
        Assert.Equal("Move HEAT-104 · LRF-01 → LRF-02 · +45 min", state.UndoDescription);
        Assert.Equal(baseline, state.UndoPlan());
        Assert.True(state.CanRedo);
        Assert.Equal("Move HEAT-104 · LRF-01 → LRF-02 · +45 min", state.RedoDescription);
        Assert.Equal(new PlanRedoResult(child, baseline), state.RedoPlan());
    }

    [Fact]
    public void Recalculation_history_uses_the_same_owner_as_move_history()
    {
        var state = new PlanningWorkbenchState();
        var baseline = Guid.NewGuid();
        var optimized = Guid.NewGuid();

        state.RecordAppliedPlan(baseline, optimized, "Optimize flexible horizon");

        Assert.True(state.CanUndo);
        Assert.Equal("Optimize flexible horizon", state.UndoDescription);
        Assert.Equal(baseline, state.UndoPlan());
    }

    [Fact]
    public void Clear_focus_restores_the_unfiltered_schedule()
    {
        var state = new PlanningWorkbenchState();
        state.SetSearch("MTO-SO-1001-10");
        state.SelectOperation("EAF:HEAT-01");

        state.ClearFocus();

        Assert.Equal(string.Empty, state.SearchText);
        Assert.Null(state.SelectedPlanningKey);
    }
}
