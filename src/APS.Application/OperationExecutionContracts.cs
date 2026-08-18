using APS.Domain;

namespace APS.Application;

public sealed record OperationExecutionUpdate(
    Guid PlanVersionId,
    string PlanningKey,
    OperationExecutionStatus Status,
    DateTime ChangedOnUtc,
    ExecutionUpdateSource Source,
    Guid? ActualResourceId = null,
    DateTime? ActualStartUtc = null,
    DateTime? ActualEndUtc = null,
    decimal? ActualQuantityMt = null,
    string? ExternalEventId = null,
    string? Comment = null,
    bool IsCorrection = false);

public sealed record OperationExecutionSnapshot(
    Guid PlanVersionId,
    string PlanningKey,
    ProcessOperationType ProcessOperationType,
    Guid PlannedResourceId,
    Guid? CommittedResourceId,
    Guid? ActualResourceId,
    OperationAssignmentCommitmentState AssignmentCommitmentState,
    OperationExecutionStatus Status,
    DateTime PlannedStartUtc,
    DateTime PlannedEndUtc,
    DateTime? ActualStartUtc,
    DateTime? ActualEndUtc,
    decimal PlannedQuantityMt,
    decimal ActualQuantityMt,
    DateTime? LastChangedOnUtc,
    bool IsOffPlanActualResource,
    string? OffPlanActualReasonCode);

public interface IOperationExecutionService
{
    Task<OperationExecutionSnapshot> ApplyAsync(
        OperationExecutionUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-evaluates flexible/firm/committed resource assignments against the immutable Plan Version
    /// policy snapshot, current clock and actual predecessor progress. This is safe to run periodically.
    /// Running/completed assignments are never downgraded.
    /// </summary>
    Task<IReadOnlyCollection<OperationExecutionSnapshot>> RefreshCommitmentsAsync(
        Guid planVersionId,
        DateTime referenceTimeUtc,
        CancellationToken cancellationToken = default);
}
