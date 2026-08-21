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
    public void Applied_plan_history_supports_undo_and_redo()
    {
        var state = new PlanningWorkbenchState();
        var baseline = Guid.NewGuid();
        var child = Guid.NewGuid();

        state.RecordAppliedPlan(baseline, child);

        Assert.Equal(baseline, state.UndoPlan());
        Assert.Equal(child, state.RedoPlan());
    }
}
