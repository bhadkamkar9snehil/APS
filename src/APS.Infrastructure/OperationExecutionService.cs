using System.Text.Json;
using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class OperationExecutionService(ApsDbContext db) : IOperationExecutionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<OperationExecutionSnapshot> ApplyAsync(
        OperationExecutionUpdate update,
        CancellationToken cancellationToken = default)
    {
        Validate(update);

        var operation = await db.PlanOperationSnapshots.SingleOrDefaultAsync(x =>
            x.PlanVersionId == update.PlanVersionId && x.PlanningKey == update.PlanningKey,
            cancellationToken)
            ?? throw new KeyNotFoundException("No planned process operation matches the supplied Plan Version and planning key.");

        var history = DeserializeHistory(operation.ExecutionHistoryJson);
        if (!string.IsNullOrWhiteSpace(update.ExternalEventId))
        {
            var duplicate = history.Any(x =>
                x.Source == update.Source &&
                string.Equals(x.ExternalEventId, update.ExternalEventId, StringComparison.OrdinalIgnoreCase));
            if (duplicate) return Snapshot(operation);
        }

        var previous = operation.ExecutionStatus;
        if (!update.IsCorrection && !CanTransition(previous, update.Status))
        {
            throw new InvalidOperationException(
                $"Operation {update.PlanningKey} cannot move from {previous} to {update.Status} without an explicit correction.");
        }

        var resource = update.ActualResourceId ?? operation.ActualResourceId ?? operation.CommittedResourceId ?? operation.ResourceId;
        operation.ExecutionStatus = update.Status;
        operation.LastExecutionChangedOnUtc = update.ChangedOnUtc;
        operation.ActualStartUtc = update.ActualStartUtc ?? operation.ActualStartUtc ??
                                   (update.Status == OperationExecutionStatus.Running ? update.ChangedOnUtc : null);
        operation.ActualEndUtc = update.ActualEndUtc ??
                                 (update.Status == OperationExecutionStatus.Completed ? update.ChangedOnUtc : operation.ActualEndUtc);
        operation.ActualQuantityMt = update.ActualQuantityMt ?? operation.ActualQuantityMt;

        if (operation.ActualStartUtc.HasValue && operation.ActualEndUtc.HasValue && operation.ActualEndUtc < operation.ActualStartUtc)
            throw new InvalidOperationException("Operation actual end cannot be before actual start.");
        if (operation.ActualQuantityMt < 0m)
            throw new InvalidOperationException("Operation actual quantity cannot be negative.");

        switch (update.Status)
        {
            case OperationExecutionStatus.Ready:
                if (update.ActualResourceId.HasValue)
                {
                    operation.CommittedResourceId = resource;
                    operation.AssignmentCommitmentState = OperationAssignmentCommitmentState.Committed;
                }
                break;
            case OperationExecutionStatus.Running:
                operation.CommittedResourceId = resource;
                operation.ActualResourceId = resource;
                operation.AssignmentCommitmentState = OperationAssignmentCommitmentState.Running;
                break;
            case OperationExecutionStatus.Completed:
                operation.CommittedResourceId = resource;
                operation.ActualResourceId = resource;
                operation.AssignmentCommitmentState = OperationAssignmentCommitmentState.Completed;
                break;
            case OperationExecutionStatus.Held:
                if (operation.ActualResourceId.HasValue)
                    operation.AssignmentCommitmentState = OperationAssignmentCommitmentState.Running;
                break;
        }

        history.Add(new OperationExecutionEventSnapshot(
            previous,
            update.Status,
            update.ActualResourceId,
            update.ChangedOnUtc,
            update.Source,
            update.ExternalEventId,
            update.Comment));
        operation.ExecutionHistoryJson = JsonSerializer.Serialize(history, JsonOptions);

        await db.SaveChangesAsync(cancellationToken);
        return Snapshot(operation);
    }

    private static List<OperationExecutionEventSnapshot> DeserializeHistory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<OperationExecutionEventSnapshot>();
        return JsonSerializer.Deserialize<List<OperationExecutionEventSnapshot>>(json, JsonOptions)
               ?? new List<OperationExecutionEventSnapshot>();
    }

    private static OperationExecutionSnapshot Snapshot(PlanOperationSnapshot operation) => new(
        operation.PlanVersionId,
        operation.PlanningKey,
        operation.ProcessOperationType,
        operation.ResourceId,
        operation.CommittedResourceId,
        operation.ActualResourceId,
        operation.AssignmentCommitmentState,
        operation.ExecutionStatus,
        operation.StartUtc,
        operation.EndUtc,
        operation.ActualStartUtc,
        operation.ActualEndUtc,
        operation.QuantityMt,
        operation.ActualQuantityMt,
        operation.LastExecutionChangedOnUtc);

    private static void Validate(OperationExecutionUpdate update)
    {
        if (string.IsNullOrWhiteSpace(update.PlanningKey))
            throw new ArgumentException("PlanningKey is required.", nameof(update));
        if (update.Source != ExecutionUpdateSource.Manual && string.IsNullOrWhiteSpace(update.ExternalEventId))
            throw new ArgumentException("ExternalEventId is required for non-manual execution updates.", nameof(update));
        if (update.ActualQuantityMt < 0m)
            throw new ArgumentOutOfRangeException(nameof(update.ActualQuantityMt));
        if (update.ActualStartUtc.HasValue && update.ActualEndUtc.HasValue && update.ActualEndUtc < update.ActualStartUtc)
            throw new ArgumentException("ActualEndUtc cannot be before ActualStartUtc.", nameof(update));
    }

    private static bool CanTransition(OperationExecutionStatus from, OperationExecutionStatus to)
    {
        if (from == to) return true;
        return from switch
        {
            OperationExecutionStatus.Planned => to is OperationExecutionStatus.Ready or OperationExecutionStatus.Running or OperationExecutionStatus.Held or OperationExecutionStatus.Cancelled,
            OperationExecutionStatus.Ready => to is OperationExecutionStatus.Running or OperationExecutionStatus.Held or OperationExecutionStatus.Cancelled,
            OperationExecutionStatus.Running => to is OperationExecutionStatus.Held or OperationExecutionStatus.Completed,
            OperationExecutionStatus.Held => to is OperationExecutionStatus.Ready or OperationExecutionStatus.Running or OperationExecutionStatus.Cancelled,
            OperationExecutionStatus.Completed => false,
            OperationExecutionStatus.Cancelled => false,
            _ => false
        };
    }
}
