using APS.Application;

namespace APS.UI.State;

public sealed class PlannerWorkspaceState
{
    public Guid? CurrentPlanVersionId { get; private set; }
    public Guid? BaselinePlanVersionId { get; private set; }
    public PlannerEntityRef? SelectedEntity { get; private set; }
    public DateTime? WindowStartUtc { get; private set; }
    public DateTime? WindowEndUtc { get; private set; }

    public event Action? Changed;

    public void SetPlan(Guid? planVersionId, Guid? baselinePlanVersionId = null)
    {
        if (CurrentPlanVersionId == planVersionId && BaselinePlanVersionId == baselinePlanVersionId) return;
        CurrentPlanVersionId = planVersionId;
        BaselinePlanVersionId = baselinePlanVersionId;
        NotifyChanged();
    }

    public void SetBaseline(Guid? baselinePlanVersionId)
    {
        if (BaselinePlanVersionId == baselinePlanVersionId) return;
        BaselinePlanVersionId = baselinePlanVersionId;
        NotifyChanged();
    }

    public void Select(PlannerEntityRef entity)
    {
        if (SelectedEntity == entity) return;
        SelectedEntity = entity;
        NotifyChanged();
    }

    public void ClearSelection()
    {
        if (SelectedEntity is null) return;
        SelectedEntity = null;
        NotifyChanged();
    }

    public void SetWindow(DateTime? startUtc, DateTime? endUtc)
    {
        if (WindowStartUtc == startUtc && WindowEndUtc == endUtc) return;
        WindowStartUtc = startUtc;
        WindowEndUtc = endUtc;
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke();
}
