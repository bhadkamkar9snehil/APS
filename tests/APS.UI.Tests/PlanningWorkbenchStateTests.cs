using APS.UI.State;

namespace APS.UI.Tests;

public sealed class PlanningWorkbenchStateTests
{
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
    public void Applied_plan_history_supports_undo_and_redo()
    {
        var state = new PlanningWorkbenchState();
        var baseline = Guid.NewGuid();
        var child = Guid.NewGuid();

        state.RecordAppliedPlan(baseline, child);

        Assert.Equal(baseline, state.UndoPlan());
        Assert.Equal(child, state.RedoPlan());
    }

    [Fact]
    public void Inspector_stays_out_of_the_way_until_an_operation_is_selected()
    {
        var state = new PlanningWorkbenchState();

        Assert.False(state.InspectorOpen);

        state.SelectOperation("EAF:HEAT-01");

        Assert.True(state.InspectorOpen);
    }
}
