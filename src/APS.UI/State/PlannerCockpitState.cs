namespace APS.UI.State;

public enum PlannerCockpitDrawer { None, Queue, Inspector }

public enum PlannerAnalysisView
{
    ControlOverview, Exceptions, Capacity, Delivery, Material,
    ScenarioComparison, Execution, Traceability
}

public sealed class PlannerCockpitState
{
    public PlannerCockpitDrawer OpenDrawer { get; private set; }
    public bool AnalysisDockOpen { get; private set; }
    public PlannerAnalysisView AnalysisView { get; private set; } = PlannerAnalysisView.ControlOverview;
    public bool QueueOpen => OpenDrawer == PlannerCockpitDrawer.Queue;
    public bool InspectorOpen => OpenDrawer == PlannerCockpitDrawer.Inspector;

    public event Action? Changed;

    public void ToggleQueue() => SetDrawer(QueueOpen ? PlannerCockpitDrawer.None : PlannerCockpitDrawer.Queue);
    public void ToggleInspector() => SetDrawer(InspectorOpen ? PlannerCockpitDrawer.None : PlannerCockpitDrawer.Inspector);
    public void OpenInspector() => SetDrawer(PlannerCockpitDrawer.Inspector);
    public void CloseTransientPanels() => SetDrawer(PlannerCockpitDrawer.None);

    public void OpenAnalysis(PlannerAnalysisView view)
    {
        AnalysisView = view;
        AnalysisDockOpen = true;
        NotifyChanged();
    }

    public void ToggleAnalysis()
    {
        AnalysisDockOpen = !AnalysisDockOpen;
        NotifyChanged();
    }

    private void SetDrawer(PlannerCockpitDrawer drawer)
    {
        if (OpenDrawer == drawer) return;
        OpenDrawer = drawer;
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke();
}
