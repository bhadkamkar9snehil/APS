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
    Detail,
    Shift,
    Day,
    ThreeDays,
    Week,
    TwoWeeks,
    Month,
    Fit
}

public enum PlanningWorkbenchQueueContent
{
    Demand,
    Campaigns,
    Exceptions
}

public enum GanttBaselineMode
{
    Ghost,
    ChangedOnly
}

public sealed class PlanningWorkbenchState
{
    private readonly Stack<PlanHistoryEntry> undo = new();
    private readonly Stack<PlanHistoryEntry> redo = new();

    public GanttViewportState Viewport { get; } = new();

    public PlanningWorkbenchMode Mode { get; private set; } = PlanningWorkbenchMode.Plan;
    public PlanningScenarioIntent ScenarioIntent { get; private set; } = PlanningScenarioIntent.Existing;
    public PlanningWorkbenchZoom Zoom => Viewport.Zoom;
    public PlanningWorkbenchQueueContent QueueContent => Mode switch
    {
        PlanningWorkbenchMode.Campaigns => PlanningWorkbenchQueueContent.Campaigns,
        PlanningWorkbenchMode.Execution or PlanningWorkbenchMode.Recovery => PlanningWorkbenchQueueContent.Exceptions,
        _ => PlanningWorkbenchQueueContent.Demand
    };
    public DateTime PlanStartUtc => Viewport.PlanStartUtc;
    public DateTime PlanEndUtc => Viewport.PlanEndUtc;
    public DateTime ContentStartUtc => Viewport.ContentStartUtc;
    public DateTime ContentEndUtc => Viewport.ContentEndUtc;
    public DateTime VisibleStartUtc => Viewport.VisibleStartUtc;
    public DateTime VisibleEndUtc => Viewport.VisibleEndUtc;
    public string SearchText { get; private set; } = string.Empty;
    public string? SelectedPlanningKey { get; private set; }
    public PlanningMoveProposal? StagedMove { get; private set; }
    public PlanningProposalImpact? Impact { get; private set; }
    public bool ShowBaseline { get; private set; } = true;
    public GanttBaselineMode BaselineMode { get; private set; } = GanttBaselineMode.Ghost;
    public bool ShowDependencies { get; private set; }
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
        var requestedZoom = Zoom;
        var planEnd = endUtc > startUtc ? endUtc : startUtc.AddHours(1);
        Viewport.Configure(
            startUtc,
            planEnd,
            startUtc,
            planEnd,
            Viewport.TimelineWidthPx,
            contentStartUtc ?? startUtc,
            contentEndUtc ?? planEnd);
        if (requestedZoom == PlanningWorkbenchZoom.Fit) Viewport.FitContent();
        else Viewport.ZoomAt(requestedZoom, Viewport.TimelineWidthPx / 2d);
        Notify();
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
    public void SetZoom(PlanningWorkbenchZoom zoom)
    {
        if (zoom == PlanningWorkbenchZoom.Fit)
        {
            if (Viewport.Zoom == PlanningWorkbenchZoom.Fit && Viewport.ResetFit()) { Notify(); return; }
            Viewport.FitContent();
        }
        else
        {
            Viewport.ZoomAt(zoom, Viewport.TimelineWidthPx / 2d);
        }
        Notify();
    }

    public void SetZoomAt(PlanningWorkbenchZoom zoom, double pointerRatio)
    {
        if (zoom == PlanningWorkbenchZoom.Fit)
        {
            SetZoom(zoom);
            return;
        }
        Viewport.ZoomAt(zoom, Math.Clamp(pointerRatio, 0d, 1d) * Viewport.TimelineWidthPx);
        Notify();
    }

    public void StepZoom(int direction, double pointerRatio)
    {
        var levels = new[]
        {
            (PlanningWorkbenchZoom.Detail, TimeSpan.FromMinutes(30)),
            (PlanningWorkbenchZoom.Shift, TimeSpan.FromHours(8)),
            (PlanningWorkbenchZoom.Day, TimeSpan.FromDays(1)),
            (PlanningWorkbenchZoom.ThreeDays, TimeSpan.FromDays(3)),
            (PlanningWorkbenchZoom.Week, TimeSpan.FromDays(7)),
            (PlanningWorkbenchZoom.TwoWeeks, TimeSpan.FromDays(14)),
            (PlanningWorkbenchZoom.Month, TimeSpan.FromDays(30))
        };
        var current = VisibleEndUtc - VisibleStartUtc;
        var target = direction < 0
            ? levels.LastOrDefault(x => x.Item2 < current)
            : levels.FirstOrDefault(x => x.Item2 > current);
        if (target == default)
        {
            if (direction > 0) SetZoom(PlanningWorkbenchZoom.Fit);
            return;
        }
        SetZoomAt(target.Item1, pointerRatio);
    }

    public void Pan(double viewportFraction)
    {
        Viewport.Pan(viewportFraction);
        Notify();
    }

    public void ToggleBaseline() { ShowBaseline = !ShowBaseline; Notify(); }
    public void SetBaselineMode(GanttBaselineMode mode) { BaselineMode = mode; Notify(); }
    public void ToggleDependencies() { ShowDependencies = !ShowDependencies; Notify(); }
    public void SetLayerVisibility(bool showBaseline, bool showDependencies)
    {
        ShowBaseline = showBaseline;
        ShowDependencies = showDependencies;
        Notify();
    }
    public void StageMove(PlanningMoveProposal proposal)
    {
        StagedMove = proposal;
        Impact = null;
        SelectedPlanningKey = proposal.PlanningKey;
        Notify();
    }

    public void SetImpact(PlanningProposalImpact impact) { Impact = impact; Notify(); }
    public void ClearMove() { ClearMove(true); }

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public string? UndoDescription => undo.TryPeek(out var entry) ? entry.Description : null;
    public string? RedoDescription => redo.TryPeek(out var entry) ? entry.Description : null;

    public void RecordAppliedPlan(Guid previousPlanId, Guid newPlanId, string description = "Apply planning change")
    {
        undo.Push(new PlanHistoryEntry(previousPlanId, newPlanId, description));
        redo.Clear();
        ClearMove(false);
        Notify();
    }

    public Guid? UndoPlan()
    {
        if (!undo.TryPop(out var entry)) return null;
        redo.Push(entry);
        Notify();
        return entry.PreviousPlanId;
    }

    public Guid? RedoPlan()
    {
        if (!redo.TryPop(out var entry)) return null;
        undo.Push(entry);
        Notify();
        return entry.NewPlanId;
    }

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

    public void SetScenarioIntent(PlanningScenarioIntent intent)
    {
        ScenarioIntent = intent;
        Notify();
    }

    private void Notify() => Changed?.Invoke();
    private sealed record PlanHistoryEntry(Guid PreviousPlanId, Guid NewPlanId, string Description);
}
