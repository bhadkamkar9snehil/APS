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
