using APS.Domain;

namespace APS.Application;

public sealed record WorkOrderExecutionUpdate(
    Guid? WorkOrderId,
    string? ExternalExecutionId,
    WorkOrderStatus Status,
    DateTime? ActualStart,
    DateTime? ActualEnd,
    decimal? ActualQuantityMt,
    DateTime ChangedOnUtc,
    ExecutionUpdateSource Source,
    string? ExternalEventId = null,
    string? Comment = null,
    bool IsCorrection = false);

public sealed record WorkOrderExecutionSnapshot(
    Guid WorkOrderId,
    string WorkOrderNumber,
    string? ExternalExecutionId,
    WorkOrderStatus Status,
    decimal PlannedQuantityMt,
    decimal ActualQuantityMt,
    DateTime? PlannedStart,
    DateTime? PlannedEnd,
    DateTime? ActualStart,
    DateTime? ActualEnd,
    DateTime ChangedOnUtc);

public interface IWorkOrderExecutionService
{
    Task<WorkOrderExecutionSnapshot> ApplyAsync(
        WorkOrderExecutionUpdate update,
        CancellationToken cancellationToken = default);
}
