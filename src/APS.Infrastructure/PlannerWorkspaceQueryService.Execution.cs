using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed partial class PlannerWorkspaceQueryService
{
    public async Task<WorkOrdersWorkspaceView?> GetWorkOrdersAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default)
    {
        var plan = await ResolvePlanAsync(planVersionId, cancellationToken);
        if (plan is null) return null;

        var scheduled = await db.ScheduledOperations.AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .ToListAsync(cancellationToken);
        var workOrderIds = scheduled.Select(x => x.WorkOrderId).Distinct().ToArray();
        if (workOrderIds.Length == 0)
        {
            return new WorkOrdersWorkspaceView(plan, 0, 0, 0, 0, 0, Array.Empty<WorkOrderView>());
        }

        var workOrders = await db.WorkOrders.AsNoTracking()
            .Where(x => workOrderIds.Contains(x.Id))
            .OrderBy(x => x.WorkOrderNumber)
            .ToListAsync(cancellationToken);
        var allocations = await db.WorkOrderAllocations.AsNoTracking()
            .Where(x => workOrderIds.Contains(x.WorkOrderId))
            .ToListAsync(cancellationToken);
        var productionOrders = await db.PlanProductionOrderSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .ToListAsync(cancellationToken);
        var poById = productionOrders.ToDictionary(x => x.ProductionOrderId);

        var planningKeys = scheduled.Select(x => x.PlanningKey).Distinct().ToArray();
        var planOperations = planningKeys.Length == 0
            ? new List<PlanOperationSnapshot>()
            : await db.PlanOperationSnapshots.AsNoTracking()
                .Where(x => x.PlanVersionId == plan.PlanVersionId && planningKeys.Contains(x.PlanningKey))
                .OrderBy(x => x.StartUtc)
                .ToListAsync(cancellationToken);
        var planOperationByKey = planOperations.ToDictionary(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase);

        var resourceIds = planOperations.Select(x => x.ResourceId).Distinct().ToArray();
        var resourceCodes = resourceIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.Resources.AsNoTracking()
                .Where(x => resourceIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);

        var scheduledByWo = scheduled.GroupBy(x => x.WorkOrderId).ToDictionary(x => x.Key, x => x.ToArray());
        var allocationsByWo = allocations.GroupBy(x => x.WorkOrderId).ToDictionary(x => x.Key, x => x.ToArray());

        var views = workOrders.Select(wo =>
        {
            scheduledByWo.TryGetValue(wo.Id, out var scheduledForWo);
            scheduledForWo ??= Array.Empty<ScheduledOperation>();

            var operations = scheduledForWo
                .Where(x => x.PlanningKey is not null)
                .Select(x => planOperationByKey.TryGetValue(x.PlanningKey!, out var op) ? op : null)
                .Where(x => x is not null)
                .Cast<PlanOperationSnapshot>()
                .OrderBy(x => x.StartUtc)
                .Select(op => new WorkOrderOperationView(
                    op.Id,
                    op.PlanningKey,
                    op.ProcessOperationType,
                    resourceCodes.TryGetValue(op.ResourceId, out var resourceCode) ? resourceCode : op.ResourceId.ToString("N")[..8],
                    op.StartUtc,
                    op.EndUtc,
                    op.QuantityMt,
                    op.GradeCode,
                    op.CrossSectionCode))
                .ToArray();

            allocationsByWo.TryGetValue(wo.Id, out var demandAllocations);
            demandAllocations ??= Array.Empty<WorkOrderAllocation>();
            var demandRefs = demandAllocations
                .Select(x =>
                {
                    poById.TryGetValue(x.ProductionOrderId, out var po);
                    return new WorkOrderDemandRefView(
                        x.ProductionOrderId,
                        po?.ProductionOrderNumber ?? x.ProductionOrderId.ToString("N")[..8],
                        po?.SalesOrderNumber,
                        po?.SalesOrderItemNumber,
                        po?.DemandSource ?? DemandSourceType.MakeToOrder);
                })
                .DistinctBy(x => x.ProductionOrderId)
                .ToArray();

            return new WorkOrderView(
                wo.Id,
                wo.WorkOrderNumber,
                wo.WorkOrderType,
                wo.Status,
                wo.ExternalExecutionId,
                operations.Length == 0 ? null : operations.Min(x => x.PlannedStartUtc),
                operations.Length == 0 ? null : operations.Max(x => x.PlannedEndUtc),
                operations.Length == 0 ? 0m : operations.Max(x => x.QuantityMt),
                demandRefs,
                operations);
        }).ToArray();

        return new WorkOrdersWorkspaceView(
            plan,
            views.Length,
            views.Count(x => x.Status == WorkOrderStatus.Released),
            views.Count(x => x.Status == WorkOrderStatus.Running),
            views.Count(x => x.Status == WorkOrderStatus.Held),
            views.Count(x => x.Status == WorkOrderStatus.Completed),
            views);
    }
}
