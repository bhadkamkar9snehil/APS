using APS.UI.State;

namespace APS.UI.Tests;

public sealed class PlannerCockpitStateTests
{
    [Fact]
    public void Drawers_are_mutually_exclusive_and_close_without_changing_analysis()
    {
        var state = new PlannerCockpitState();
        state.OpenAnalysis(PlannerAnalysisView.Capacity);

        state.ToggleQueue();
        Assert.True(state.QueueOpen);
        Assert.False(state.InspectorOpen);

        state.ToggleInspector();
        Assert.False(state.QueueOpen);
        Assert.True(state.InspectorOpen);

        state.CloseTransientPanels();
        Assert.Equal(PlannerCockpitDrawer.None, state.OpenDrawer);
        Assert.True(state.AnalysisDockOpen);
        Assert.Equal(PlannerAnalysisView.Capacity, state.AnalysisView);
    }

    [Fact]
    public void Analysis_selection_is_single_and_explicit()
    {
        var state = new PlannerCockpitState();
        state.OpenAnalysis(PlannerAnalysisView.Execution);
        Assert.Equal(PlannerAnalysisView.Execution, state.AnalysisView);
        Assert.True(state.AnalysisDockOpen);
    }
}
