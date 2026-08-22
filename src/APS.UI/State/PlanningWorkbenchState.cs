using APS.Application;

namespace APS.UI.State;

public enum PlanningWorkbenchMode
{
    Plan,
    Campaigns,
    Execution,
    Recovery
}

public enum PlanningScenarioIntent
{
    Existing,
    New,
    Clone,
    Recovery
}

public enum PlanningWorkbenchZoom
{
    Shift,
    Day,
    ThreeDays,
    Week,
    Fit
}

public enum PlanningWorkbenchQueueContent
{
    Demand,
    Campaigns,
    Exceptions
}

public sealed class PlanningWorkbenchState
{
    public PlanningWorkbenchMode Mode { get; private set; } = PlanningWorkbenchMode.Plan;
    public PlanningScenarioIntent ScenarioIntent { get; private set; } = PlanningScenarioIntent.Existing;
    public PlanningWorkbenchZoom Zoom { get; private set; } = PlanningWorkbenchZoom.Fit;
    public PlanningWorkbenchQueueContent QueueContent => Mode switch
    {
        PlanningWorkbenchMode.Campaigns => PlanningWorkbenchQueueContent.Campaigns,
        PlanningWorkbenchMode.Execution or PlanningWorkbenchMode.Recovery => PlanningWorkbenchQueueContent.Exceptions,
        _ => PlanningWorkbenchQueueContent.Demand
    };
    public DateTime PlanStartUtc { get; private set; }
    public DateTime PlanEndUtc { get; private set; }
    public DateTime ContentStartUtc { get; private set; }
    public DateTime ContentEndUtc { get; private set; }
    public DateTime VisibleStartUtc { get; private set; }
    public DateTime VisibleEndUtc { get; private set; }
    public string SearchText { get; private set; } = string.Empty;
    public string? SelectedPlanningKey { get; private set; }
    public PlanningMoveProposal? StagedMove { get; private set; }
    public PlanningProposalImpact? Impact { get; private set; }
    public bool ShowBaseline { get; private set; } = true;
    public bool ShowDependencies { get; private set; }
    public bool ShowCriticalPath { get; private set; }
    public bool IsReleasedPlan { get; private set; }
    public bool CanEditSchedule => !IsReleasedPlan || ScenarioIntent is PlanningScenarioIntent.New or PlanningScenarioIntent.Clone or PlanningScenarioIntent.Recovery;
    public bool CanStartRecovery => IsReleasedPlan && ScenarioIntent != PlanningScenarioIntent.Recovery;

    public event Action? Changed;

    public void SetPlanWindow(
        DateTime startUtc,
        DateTime endUtc,
        DateTime? contentStartUtc = null,
        DateTime? contentEndUtc = null)
    {
        PlanStartUtc = startUtc;
        PlanEndUtc = endUtc > startUtc ? endUtc : startUtc.AddHours(1);
        ContentStartUtc = Clamp(contentStartUtc ?? PlanStartUtc, PlanStartUtc, PlanEndUtc);
        ContentEndUtc = Clamp(contentEndUtc ?? PlanEndUtc, PlanStartUtc, PlanEndUtc);
        if (ContentEndUtc <= ContentStartUtc)
        {
            ContentStartUtc = PlanStartUtc;
            ContentEndUtc = PlanEndUtc;
        }
        ApplyZoom();
    }

    public void SetSearch(string? value) { SearchText = value?.Trim() ?? string.Empty; Notify(); }
    public void SelectOperation(string? planningKey) { SelectedPlanningKey = planningKey; ClearMove(false); Notify(); }
    public void ClearFocus()
    {
        SearchText = string.Empty;
        SelectedPlanningKey = null;
        ClearMove(false);
        Notify();
    }
    public void SetZoom(PlanningWorkbenchZoom zoom) { Zoom = zoom; ApplyZoom(); }

    public void Pan(double viewportFraction)
    {
        var duration = VisibleEndUtc - VisibleStartUtc;
        if (duration <= TimeSpan.Zero) return;
        var shift = TimeSpan.FromTicks((long)(duration.Ticks * viewportFraction));
        var start = VisibleStartUtc + shift;
        var end = VisibleEndUtc + shift;
        if (start < PlanStartUtc) { start = PlanStartUtc; end = start + duration; }
        if (end > PlanEndUtc) { end = PlanEndUtc; start = end - duration; }
        VisibleStartUtc = start < PlanStartUtc ? PlanStartUtc : start;
        VisibleEndUtc = end > PlanEndUtc ? PlanEndUtc : end;
        Notify();
    }

    public void ToggleBaseline() { ShowBaseline = !ShowBaseline; Notify(); }
    public void ToggleDependencies() { ShowDependencies = !ShowDependencies; Notify(); }
    public void ToggleCriticalPath() { ShowCriticalPath = !ShowCriticalPath; Notify(); }

    public void StageMove(PlanningMoveProposal proposal)
    {
        StagedMove = proposal;
        Impact = null;
        SelectedPlanningKey = proposal.PlanningKey;
        Notify();
    }

    public void SetImpact(PlanningProposalImpact impact) { Impact = impact; Notify(); }
    public void ClearMove() { ClearMove(true); }

    private void ApplyZoom()
    {
        var full = PlanEndUtc - PlanStartUtc;
        if (Zoom == PlanningWorkbenchZoom.Fit)
        {
            FitToContent();
            Notify();
            return;
        }

        var requested = Zoom switch
        {
            PlanningWorkbenchZoom.Shift => TimeSpan.FromHours(8),
            PlanningWorkbenchZoom.Day => TimeSpan.FromDays(1),
            PlanningWorkbenchZoom.ThreeDays => TimeSpan.FromDays(3),
            PlanningWorkbenchZoom.Week => TimeSpan.FromDays(7),
            _ => full
        };
        var duration = requested < full ? requested : full;
        var center = VisibleEndUtc > VisibleStartUtc
            ? VisibleStartUtc + TimeSpan.FromTicks((VisibleEndUtc - VisibleStartUtc).Ticks / 2)
            : PlanStartUtc + TimeSpan.FromTicks(full.Ticks / 2);
        var start = center - TimeSpan.FromTicks(duration.Ticks / 2);
        var end = start + duration;
        if (start < PlanStartUtc) { start = PlanStartUtc; end = start + duration; }
        if (end > PlanEndUtc) { end = PlanEndUtc; start = end - duration; }
        VisibleStartUtc = start;
        VisibleEndUtc = end;
        Notify();
    }

    private void FitToContent()
    {
        var contentDuration = ContentEndUtc - ContentStartUtc;
        var paddingTicks = Math.Clamp(
            (long)(contentDuration.Ticks * 0.05d),
            TimeSpan.FromMinutes(30).Ticks,
            TimeSpan.FromHours(4).Ticks);
        var padding = TimeSpan.FromTicks(paddingTicks);
        var start = ContentStartUtc - padding;
        var end = ContentEndUtc + padding;

        if (start < PlanStartUtc) start = PlanStartUtc;
        if (end > PlanEndUtc) end = PlanEndUtc;
        if (end <= start) end = start.AddHours(1) <= PlanEndUtc ? start.AddHours(1) : PlanEndUtc;

        VisibleStartUtc = start;
        VisibleEndUtc = end;
    }

    private static DateTime Clamp(DateTime value, DateTime min, DateTime max) =>
        value < min ? min : value > max ? max : value;

    private void ClearMove(bool notify)
    {
        StagedMove = null;
        Impact = null;
        if (notify) Notify();
    }

    public void SetMode(PlanningWorkbenchMode mode)
    {
        Mode = mode;
        Notify();
    }
    public void SetReleasedPlan(bool released)
    {
        IsReleasedPlan = released;
        if (released && ScenarioIntent != PlanningScenarioIntent.Recovery)
        {
            Mode = PlanningWorkbenchMode.Execution;
        }
        Notify();
    }

    public void StartRecovery()
    {
        if (!CanStartRecovery) return;
        ScenarioIntent = PlanningScenarioIntent.Recovery;
        Mode = PlanningWorkbenchMode.Recovery;
        Notify();
    }

    public void StartPlanningScenario()
    {
        ScenarioIntent = PlanningScenarioIntent.Clone;
        Mode = PlanningWorkbenchMode.Plan;
        Notify();
    }

    private void Notify() => Changed?.Invoke();
}
