using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

internal static class PersistedPlanServiceReadiness
{
    private const decimal QuantityTolerance = 0.000001m;

    public static async Task<IReadOnlyCollection<PlanReleaseReadinessFinding>> EvaluateAsync(
        ApsDbContext db,
        Guid planVersionId,
        CancellationToken cancellationToken)
    {
        var orders = await db.PlanProductionOrderSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId &&
                        x.DemandSource == DemandSourceType.MakeToOrder &&
                        x.RemainingQuantityMt > 0m)
            .Select(x => new ServiceOrder(
                x.ProductionOrderId,
                x.ProductionOrderNumber,
                x.RequiredDate,
                x.RemainingQuantityMt,
                x.FinishedGoodsAllocatedMt))
            .ToArrayAsync(cancellationToken);
        if (orders.Length == 0) return Array.Empty<PlanReleaseReadinessFinding>();

        var orderIds = orders.Select(x => x.ProductionOrderId).ToHashSet();
        var serviceDeadlines = await LoadServiceDeadlinesAsync(db, planVersionId, orderIds, cancellationToken);
        var sourcesByOrder = orders.ToDictionary(
            x => x.ProductionOrderId,
            _ => new HashSet<Guid>());

        var heatAllocations = await db.PlanHeatAllocationSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId &&
                        orderIds.Contains(x.ProductionOrderId) &&
                        x.PlannedOutputQuantityMt > 0m)
            .Select(x => new { x.ProductionOrderId, SourceEntityId = x.CampaignHeatId })
            .ToArrayAsync(cancellationToken);
        foreach (var allocation in heatAllocations)
            sourcesByOrder[allocation.ProductionOrderId].Add(allocation.SourceEntityId);

        var rollingAllocations = await db.PlanRollingPlanAllocationSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId &&
                        orderIds.Contains(x.ProductionOrderId) &&
                        x.PlannedQuantityMt > 0m)
            .Select(x => new { x.ProductionOrderId, SourceEntityId = x.RollingPlanId })
            .ToArrayAsync(cancellationToken);
        foreach (var allocation in rollingAllocations)
            sourcesByOrder[allocation.ProductionOrderId].Add(allocation.SourceEntityId);

        var routeAllocations = await db.PlanRouteOperationAllocationSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId &&
                        orderIds.Contains(x.ProductionOrderId) &&
                        x.PlannedQuantityMt > 0m)
            .Select(x => new { x.ProductionOrderId, SourceEntityId = x.RouteOperationPlanId })
            .ToArrayAsync(cancellationToken);
        foreach (var allocation in routeAllocations)
            sourcesByOrder[allocation.ProductionOrderId].Add(allocation.SourceEntityId);

        var sourceIds = sourcesByOrder.Values.SelectMany(x => x).Distinct().ToArray();
        var operationEnds = sourceIds.Length == 0
            ? Array.Empty<SourceOperationEnd>()
            : await db.PlanOperationSnapshots.AsNoTracking()
                .Where(x => x.PlanVersionId == planVersionId && sourceIds.Contains(x.SourceEntityId))
                .Select(x => new SourceOperationEnd(x.SourceEntityId, x.EndUtc))
                .ToArrayAsync(cancellationToken);
        var completionBySource = operationEnds
            .GroupBy(x => x.SourceEntityId)
            .ToDictionary(x => x.Key, x => x.Max(y => y.EndUtc));

        var findings = new List<PlanReleaseReadinessFinding>();
        foreach (var order in orders)
        {
            var manufacturingQuantity = Math.Max(0m, order.RemainingQuantityMt - order.FinishedGoodsAllocatedMt);
            if (manufacturingQuantity <= QuantityTolerance) continue;

            var sources = sourcesByOrder[order.ProductionOrderId];
            if (sources.Count == 0)
            {
                findings.Add(new PlanReleaseReadinessFinding(
                    "SERVICE_COMPLETION_MISSING",
                    $"{order.ProductionOrderNumber} still requires {manufacturingQuantity:0.####} MT of manufacturing but the Plan Version has no persisted production allocation serving it."));
                continue;
            }

            var missingSources = sources.Where(x => !completionBySource.ContainsKey(x)).ToArray();
            if (missingSources.Length > 0)
            {
                findings.Add(new PlanReleaseReadinessFinding(
                    "SERVICE_SCHEDULE_INCOMPLETE",
                    $"{order.ProductionOrderNumber} has {missingSources.Length} persisted production allocation(s) without scheduled operations, so its completion date cannot be proven."));
                continue;
            }

            var plannedCompletionUtc = sources.Max(x => completionBySource[x]);
            var deadline = ResolveDeadline(order, serviceDeadlines);
            if (plannedCompletionUtc <= deadline.LatestAcceptableProductionDateUtc) continue;

            findings.Add(new PlanReleaseReadinessFinding(
                "SERVICE_LATE",
                $"{order.ProductionOrderNumber} is planned to complete at {plannedCompletionUtc:O}, after its latest acceptable production deadline {deadline.LatestAcceptableProductionDateUtc:O}. Preferred production target was {deadline.TargetProductionDateUtc:O}."));
        }

        return findings;
    }

    private static async Task<IReadOnlyDictionary<Guid, ServiceDeadline>> LoadServiceDeadlinesAsync(
        ApsDbContext db,
        Guid planVersionId,
        IReadOnlySet<Guid> productionOrderIds,
        CancellationToken cancellationToken)
    {
        var rows = await db.PlanDemandSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId &&
                        x.ProductionOrderId.HasValue &&
                        productionOrderIds.Contains(x.ProductionOrderId.Value))
            .Select(x => new
            {
                ProductionOrderId = x.ProductionOrderId!.Value,
                Target = x.ProductionRequiredByDate,
                Latest = x.ProductionLatestAcceptableDate
            })
            .ToArrayAsync(cancellationToken);

        return rows
            .GroupBy(x => x.ProductionOrderId)
            .ToDictionary(
                x => x.Key,
                x => new ServiceDeadline(
                    x.Min(y => y.Target),
                    x.Min(y => y.Latest ?? y.Target)));
    }

    private static ServiceDeadline ResolveDeadline(
        ServiceOrder order,
        IReadOnlyDictionary<Guid, ServiceDeadline> serviceDeadlines) =>
        serviceDeadlines.TryGetValue(order.ProductionOrderId, out var deadline)
            ? deadline
            : new ServiceDeadline(order.RequiredDate, order.RequiredDate);

    private sealed record ServiceOrder(
        Guid ProductionOrderId,
        string ProductionOrderNumber,
        DateTime RequiredDate,
        decimal RemainingQuantityMt,
        decimal FinishedGoodsAllocatedMt);

    private sealed record ServiceDeadline(
        DateTime TargetProductionDateUtc,
        DateTime LatestAcceptableProductionDateUtc);

    private sealed record SourceOperationEnd(Guid SourceEntityId, DateTime EndUtc);
}
