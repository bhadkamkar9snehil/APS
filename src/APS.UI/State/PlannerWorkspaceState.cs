using APS.Application;

namespace APS.UI.State;

public sealed class PlannerWorkspaceState
{
    public Guid? CurrentPlanVersionId { get; private set; }
    public Guid? BaselinePlanVersionId { get; private set; }

    /// <summary>
    /// Planner-owned controls for the next Calculate/Replan. Keeping these with workspace state means
    /// the browser host isolates them per Blazor circuit while the single-window desktop host naturally
    /// keeps one profile for the planner's session.
    /// </summary>
    public PlannerConstraintState Constraints { get; } = new();

    public event Action? Changed;

    public void SetPlan(Guid? planVersionId, Guid? baselinePlanVersionId = null)
    {
        if (CurrentPlanVersionId == planVersionId && BaselinePlanVersionId == baselinePlanVersionId) return;
        CurrentPlanVersionId = planVersionId;
        BaselinePlanVersionId = baselinePlanVersionId;
        Changed?.Invoke();
    }

    // Temporary compatibility shims for FiniteSchedule. The workbench owns viewport and focused-entity
    // state locally; these can disappear together when the remaining legacy calls are removed there.
    public void SetWindow(DateTime? _, DateTime? __) { }
    public void Select(PlannerEntityRef _) { }
    public void ClearSelection() { }
}
