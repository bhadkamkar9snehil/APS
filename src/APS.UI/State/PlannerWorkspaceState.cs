using APS.Application;

namespace APS.UI.State;

public sealed class PlannerWorkspaceState
{
    public Guid? CurrentPlanVersionId { get; private set; }
    public Guid? BaselinePlanVersionId { get; private set; }

    public event Action? Changed;

    public void SetPlan(Guid? planVersionId, Guid? baselinePlanVersionId = null)
    {
        if (CurrentPlanVersionId == planVersionId && BaselinePlanVersionId == baselinePlanVersionId) return;
        CurrentPlanVersionId = planVersionId;
        BaselinePlanVersionId = baselinePlanVersionId;
        Changed?.Invoke();
    }

    // Temporary compatibility shims for the finite-schedule page. They deliberately carry no shared state;
    // schedule viewport and focused entity are page-local concerns.
    public void SetWindow(DateTime? _, DateTime? __) { }
    public void Select(PlannerEntityRef _) { }
}
