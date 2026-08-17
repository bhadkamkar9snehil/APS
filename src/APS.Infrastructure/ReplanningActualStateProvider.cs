using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class ReplanningActualStateProvider(
    ApsDbContext db,
    IInventorySnapshotProvider inventoryProvider) : IReplanningActualStateProvider
{
    public async Task<ReplanningActualState> GetAsync(
        Guid baselinePlanVersionId,
        DateTime referenceTimeUtc,
        IReadOnlyCollection<BaselinePlanOperation> baselineOperations,
        CancellationToken cancellationToken = default)
    {
        var baseline = baselineOperations.ToDictionary(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase);
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var heatEvents = await db.HeatExecutionActuals
            .AsNoTracking()
            .Where(x => x.PlanVersionId == baselinePlanVersionId)
            .OrderBy(x => x.ChangedOnUtc)
            .ToListAsync(cancellationToken);
        var latestHeatEvents = heatEvents
            .GroupBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(y => y.ChangedOnUtc).First())
            .ToArray();

        foreach (var actual in latestHeatEvents)
        {
            if (actual.Status == HeatExecutionStatus.Completed)
            {
                baseline.Remove(actual.PlanningKey);
                completed.Add(actual.PlanningKey);
                continue;
            }

            if (actual.Status != HeatExecutionStatus.Running ||
                !baseline.TryGetValue(actual.PlanningKey, out var planned))
            {
                continue;
            }

            var actualStart = actual.ActualStartUtc ?? planned.StartUtc;
            var duration = planned.EndUtc - planned.StartUtc;
            var expectedEnd = actual.ActualEndUtc ?? actualStart.Add(duration);
            baseline[actual.PlanningKey] = planned with
            {
                ResourceId = actual.CasterResourceId ?? planned.ResourceId,
                StartUtc = actualStart,
                EndUtc = expectedEnd < referenceTimeUtc ? referenceTimeUtc : expectedEnd
            };
            running.Add(actual.PlanningKey);
        }

        var releasedOperations = await db.ScheduledOperations
            .AsNoTracking()
            .Where(x => x.PlanVersionId == baselinePlanVersionId && x.PlanningKey != null)
            .OrderBy(x => x.Start)
            .ToListAsync(cancellationToken);
        var workOrderIds = releasedOperations.Select(x => x.WorkOrderId).Distinct().ToArray();
        var workOrders = await db.WorkOrders
            .AsNoTracking()
            .Where(x => workOrderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var operationGroup in releasedOperations.GroupBy(x => x.WorkOrderId))
        {
            if (!workOrders.TryGetValue(operationGroup.Key, out var workOrder)) continue;
            var operationKeys = operationGroup
                .Where(x => !string.IsNullOrWhiteSpace(x.PlanningKey))
                .Select(x => x.PlanningKey!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (workOrder.Status == WorkOrderStatus.Completed)
            {
                foreach (var key in operationKeys)
                {
                    baseline.Remove(key);
                    completed.Add(key);
                }
                continue;
            }

            if (workOrder.Status != WorkOrderStatus.Running || !workOrder.ActualStart.HasValue)
            {
                continue;
            }

            var activeOperation = operationGroup
                .Where(x => !string.IsNullOrWhiteSpace(x.PlanningKey))
                .OrderBy(x => Math.Abs((x.Start - workOrder.ActualStart.Value).TotalMinutes))
                .FirstOrDefault();
            if (activeOperation?.PlanningKey is null ||
                !baseline.TryGetValue(activeOperation.PlanningKey, out var planned))
            {
                continue;
            }

            var duration = planned.EndUtc - planned.StartUtc;
            var expectedEnd = workOrder.ActualEnd ?? workOrder.ActualStart.Value.Add(duration);
            baseline[activeOperation.PlanningKey] = planned with
            {
                ResourceId = workOrder.ResourceId ?? activeOperation.ResourceId,
                StartUtc = workOrder.ActualStart.Value,
                EndUtc = expectedEnd < referenceTimeUtc ? referenceTimeUtc : expectedEnd
            };
            running.Add(activeOperation.PlanningKey);
        }

        var inventory = await inventoryProvider.GetInventoryAsync(cancellationToken);
        return new ReplanningActualState(
            baseline.Values.OrderBy(x => x.StartUtc).ToArray(),
            inventory,
            completed.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            running.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
