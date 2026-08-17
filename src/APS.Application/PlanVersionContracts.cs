using APS.Domain;

namespace APS.Application;

public sealed record PersistPlanningRunRequest(
    PlanningRunRequest PlanningRequest,
    PlanningRunResult PlanningResult,
    PlanTriggerType Trigger,
    DateTime ReferenceTimeUtc,
    string? Reason = null);

public sealed record PlanVersionSnapshot(
    Guid PlanVersionId,
    string VersionNumber,
    Guid? ParentPlanVersionId,
    PlanVersionStatus Status,
    PlanTriggerType Trigger,
    DateTime CreatedOnUtc,
    DateTime ReferenceTimeUtc,
    DateTime HorizonStartUtc,
    DateTime HorizonEndUtc,
    string? SolverStatus,
    long? ObjectiveValue,
    bool IsActive,
    IReadOnlyCollection<BaselinePlanOperation> Operations);

public interface IPlanVersionRepository
{
    Task<PlanVersionSnapshot> SaveAsync(
        PersistPlanningRunRequest request,
        CancellationToken cancellationToken = default);

    Task<PlanVersionSnapshot?> GetAsync(
        Guid planVersionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<BaselinePlanOperation>> GetBaselineOperationsAsync(
        Guid planVersionId,
        CancellationToken cancellationToken = default);
}
