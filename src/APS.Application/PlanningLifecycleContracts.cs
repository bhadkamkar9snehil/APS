using APS.Domain;

namespace APS.Application;

/// <summary>
/// Production-facing replanning command. Plant master data, current inventory and committed
/// in-process supply are resolved by the canonical planning lifecycle and are not caller-owned.
/// </summary>
public sealed record PlanningRecalculationRequest(
    PlanningCalculationRequest Planning,
    PlanningTimeFencePolicy TimeFencePolicy,
    DateTime? ReferenceTimeUtc = null,
    PlanTriggerType Trigger = PlanTriggerType.ExecutionFeedback,
    string? Reason = null,
    IReadOnlyCollection<OperationResourceOverride>? ResourceOverrides = null,
    IReadOnlyCollection<OperationScheduleOverride>? ScheduleOverrides = null,
    RepairScopePolicy? RepairScope = null,
    /// <summary>
    /// True for workbench/operational replans so a child plan keeps the planning policy captured on
    /// its baseline instead of silently reverting to caller UI defaults. Set false when the planner
    /// intentionally changed the control profile and wants those new controls applied to the child.
    /// </summary>
    bool UseBaselinePlanningControls = true);

public sealed record PersistedPlanningRunResult(
    PlanningRunResult Plan,
    PlanVersionSnapshot Version,
    ReplanningActualState? ExecutionState = null);

public sealed class PlanningConfigurationException : InvalidOperationException
{
    public PlanningConfigurationException(params string[] issues)
        : base(string.Join(" ", issues))
    {
        Issues = issues;
    }

    public IReadOnlyCollection<string> Issues { get; }
}

/// <summary>
/// Authoritative production lifecycle for calculate/replan. It owns authoritative master/inventory
/// resolution and Plan Version persistence. IPlanningEngine remains the reusable calculation kernel.
/// </summary>
public interface IPlanningLifecycleService
{
    Task<PersistedPlanningRunResult> CalculateAsync(
        PlanningCalculationRequest request,
        CancellationToken cancellationToken = default);

    Task<PersistedPlanningRunResult> ReplanAsync(
        Guid baselinePlanVersionId,
        PlanningRecalculationRequest request,
        CancellationToken cancellationToken = default);
}
