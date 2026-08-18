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
    IReadOnlyCollection<OperationResourceOverride>? ResourceOverrides = null);

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
