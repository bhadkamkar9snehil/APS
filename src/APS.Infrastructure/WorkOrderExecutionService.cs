using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class WorkOrderExecutionService(ApsDbContext db) : IWorkOrderExecutionService
{
    public async Task<WorkOrderExecutionSnapshot> ApplyAsync(
        WorkOrderExecutionUpdate update,
        CancellationToken cancellationToken = default)
    {
        Validate(update);

        if (!string.IsNullOrWhiteSpace(update.ExternalEventId))
        {
            var prior = await db.WorkOrderStatusHistory
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Source == update.Source && x.ExternalEventId == update.ExternalEventId,
                    cancellationToken);
            if (prior is not null)
            {
                var existing = await db.WorkOrders
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == prior.WorkOrderId, cancellationToken);
                return Snapshot(existing, prior.ChangedOnUtc);
            }
        }

        var workOrder = await ResolveWorkOrderAsync(update, cancellationToken);
        var previousStatus = workOrder.Status;

        if (!update.IsCorrection && !CanTransition(previousStatus, update.Status))
        {
            throw new InvalidOperationException(
                $"Work Order {workOrder.WorkOrderNumber} cannot move from {previousStatus} to {update.Status} without an explicit correction.");
        }

        if (!string.IsNullOrWhiteSpace(update.ExternalExecutionId))
        {
            if (!string.IsNullOrWhiteSpace(workOrder.ExternalExecutionId) &&
                !string.Equals(workOrder.ExternalExecutionId, update.ExternalExecutionId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Work Order {workOrder.WorkOrderNumber} is already linked to external execution ID {workOrder.ExternalExecutionId}.");
            }

            workOrder.ExternalExecutionId ??= update.ExternalExecutionId;
        }

        if (update.ActualStart.HasValue)
        {
            workOrder.ActualStart = update.ActualStart;
        }
        else if (update.Status == WorkOrderStatus.Running && !workOrder.ActualStart.HasValue)
        {
            workOrder.ActualStart = update.ChangedOnUtc;
        }

        if (update.ActualEnd.HasValue)
        {
            workOrder.ActualEnd = update.ActualEnd;
        }
        else if (update.Status == WorkOrderStatus.Completed && !workOrder.ActualEnd.HasValue)
        {
            workOrder.ActualEnd = update.ChangedOnUtc;
        }

        if (update.ActualQuantityMt.HasValue)
        {
            workOrder.ActualQuantityMt = update.ActualQuantityMt.Value;
        }

        if (workOrder.ActualStart.HasValue && workOrder.ActualEnd.HasValue && workOrder.ActualEnd < workOrder.ActualStart)
        {
            throw new InvalidOperationException("Actual end cannot be before actual start.");
        }

        workOrder.Status = update.Status;

        if (previousStatus != update.Status ||
            update.IsCorrection ||
            !string.IsNullOrWhiteSpace(update.ExternalEventId))
        {
            db.WorkOrderStatusHistory.Add(new WorkOrderStatusHistory
            {
                WorkOrderId = workOrder.Id,
                WorkOrder = workOrder,
                PreviousStatus = previousStatus,
                NewStatus = update.Status,
                ChangedOnUtc = update.ChangedOnUtc,
                Source = update.Source,
                ExternalEventId = update.ExternalEventId,
                Comment = update.Comment
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return Snapshot(workOrder, update.ChangedOnUtc);
    }

    private async Task<WorkOrder> ResolveWorkOrderAsync(
        WorkOrderExecutionUpdate update,
        CancellationToken cancellationToken)
    {
        WorkOrder? workOrder = null;

        if (update.WorkOrderId.HasValue)
        {
            workOrder = await db.WorkOrders.SingleOrDefaultAsync(x => x.Id == update.WorkOrderId.Value, cancellationToken);
        }

        if (workOrder is null && !string.IsNullOrWhiteSpace(update.ExternalExecutionId))
        {
            workOrder = await db.WorkOrders.SingleOrDefaultAsync(
                x => x.ExternalExecutionId == update.ExternalExecutionId,
                cancellationToken);
        }

        return workOrder ?? throw new KeyNotFoundException("No Work Order matches the supplied APS or external execution identifier.");
    }

    private static void Validate(WorkOrderExecutionUpdate update)
    {
        if (!update.WorkOrderId.HasValue && string.IsNullOrWhiteSpace(update.ExternalExecutionId))
        {
            throw new ArgumentException("Either WorkOrderId or ExternalExecutionId is required.");
        }

        if (update.ActualQuantityMt < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(update.ActualQuantityMt));
        }

        if (update.ActualStart.HasValue && update.ActualEnd.HasValue && update.ActualEnd < update.ActualStart)
        {
            throw new ArgumentException("ActualEnd cannot be before ActualStart.");
        }
    }

    private static bool CanTransition(WorkOrderStatus from, WorkOrderStatus to)
    {
        if (from == to) return true;

        return from switch
        {
            WorkOrderStatus.Planned => to is WorkOrderStatus.Released or WorkOrderStatus.Ready or WorkOrderStatus.Running or WorkOrderStatus.Held or WorkOrderStatus.Cancelled,
            WorkOrderStatus.Released => to is WorkOrderStatus.Ready or WorkOrderStatus.Running or WorkOrderStatus.Held or WorkOrderStatus.Cancelled,
            WorkOrderStatus.Ready => to is WorkOrderStatus.Running or WorkOrderStatus.Held or WorkOrderStatus.Cancelled,
            WorkOrderStatus.Running => to is WorkOrderStatus.Held or WorkOrderStatus.Completed,
            WorkOrderStatus.Held => to is WorkOrderStatus.Ready or WorkOrderStatus.Running or WorkOrderStatus.Cancelled,
            WorkOrderStatus.Completed => false,
            WorkOrderStatus.Cancelled => false,
            _ => false
        };
    }

    private static WorkOrderExecutionSnapshot Snapshot(WorkOrder workOrder, DateTime changedOnUtc) => new(
        workOrder.Id,
        workOrder.WorkOrderNumber,
        workOrder.ExternalExecutionId,
        workOrder.Status,
        workOrder.PlannedQuantityMt,
        workOrder.ActualQuantityMt,
        workOrder.PlannedStart,
        workOrder.PlannedEnd,
        workOrder.ActualStart,
        workOrder.ActualEnd,
        changedOnUtc);
}
