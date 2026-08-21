using APS.Application;

namespace APS.UI.State;

public enum PlanningWorkbenchLens
{
    Resources,
    Campaigns,
    Orders,
    Materials,
    Exceptions
}

public enum PlanningWorkbenchZoom
{
    Shift,
    Day,
    ThreeDays,
    Week,
    Fit
}

public enum PlanningWorkbenchQueueTab
{
    Demand,
    Campaigns,
    Materials,
    Exceptions
}

public sealed class PlanningWorkbenchState
{
    private readonly Stack<PlanHistoryEntry> undo = new();
    private readonly Stack<PlanHistoryEntry> redo = new();

    public PlanningWorkbenchLens Lens { get; private set; } = PlanningWorkbenchLens.Resources;
    public PlanningWorkbenchZoom Zoom { get; private set; } = PlanningWorkbenchZoom.Fit;
    public PlanningWorkbenchQueueTab QueueTab { get; private set; } = PlanningWorkbenchQueueTab.Demand;
    public DateTime PlanStartUtc { get; private set; }
    public DateTime PlanEndUtc { get; private set; }
    public DateTime VisibleStartUtc { get; private set; }
    public DateTime VisibleEndUtc { get; private set; }
    public string SearchText { get; private set; } = string.Empty;
    public string? SelectedPlanningKey { get; private set; }
    public PlanningMoveProposal? StagedMove { get; private set; }
    public PlanningProposalImpact? Impact { get; private set; }
    public bool ShowBaseline { get; private set; } = true;
    public bool ShowDependencies { get; private set; } = true;
    public bool ShowCriticalPath { get; private set; }
    public bool QueueOpen { get; private set; } = true;
    public bool InspectorOpen { get; private set; } = true;

    public event Action? Changed;

    public void SetPlanWindow(DateTime startUtc, DateTime endUtc)
    {
        PlanStartUtc = startUtc;
        PlanEndUtc = endUtc > startUtc ? endUtc : startUtc.AddHours(1);
        ApplyZoom();
    }

    public void SetLens(PlanningWorkbenchLens lens) { Lens = lens; Notify(); }
    public void SetQueueTab(PlanningWorkbenchQueueTab tab) { QueueTab = tab; Notify(); }
    public void SetSearch(string? value) { SearchText = value?.Trim() ?? string.Empty; Notify(); }
    public void SelectOperation(string? planningKey) { SelectedPlanningKey = planningKey; ClearMove(false); Notify(); }
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
    public void ToggleQueue() { QueueOpen = !QueueOpen; Notify(); }
    public void ToggleInspector() { InspectorOpen = !InspectorOpen; Notify(); }

    public void StageMove(PlanningMoveProposal proposal)
    {
        StagedMove = proposal;
        Impact = null;
        SelectedPlanningKey = proposal.PlanningKey;
        Notify();
    }

    public void SetImpact(PlanningProposalImpact impact) { Impact = impact; Notify(); }
    public void ClearMove() { ClearMove(true); }

    public void RecordAppliedPlan(Guid previousPlanId, Guid newPlanId)
    {
        undo.Push(new PlanHistoryEntry(previousPlanId, newPlanId));
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

    private void ApplyZoom()
    {
        var full = PlanEndUtc - PlanStartUtc;
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

    private void ClearMove(bool notify)
    {
        StagedMove = null;
        Impact = null;
        if (notify) Notify();
    }

    private void Notify() => Changed?.Invoke();
    private sealed record PlanHistoryEntry(Guid PreviousPlanId, Guid NewPlanId);
}
