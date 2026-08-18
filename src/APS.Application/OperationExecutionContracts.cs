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
    DateTime? LastChangedOnUtc);

public interface IOperationExecutionService
{
    Task<OperationExecutionSnapshot> ApplyAsync(
        OperationExecutionUpdate update,
        CancellationToken cancellationToken = default);
}
