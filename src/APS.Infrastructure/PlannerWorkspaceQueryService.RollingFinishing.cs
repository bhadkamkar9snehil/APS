using System.Text.Json;
using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed partial class PlannerWorkspaceQueryService
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RollingFinishingWorkspaceView?> GetRollingFinishingAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default)
    {
        var plan = await ResolvePlanAsync(planVersionId, cancellationToken);
        if (plan is null) return null;

        var rollingPlans = await db.PlanRollingPlanSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .OrderBy(x => x.SequenceNumber)
            .ToListAsync(cancellationToken);
        var allocations = await db.PlanRollingPlanAllocationSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .ToListAsync(cancellationToken);
        var productionOrders = await db.PlanProductionOrderSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .ToListAsync(cancellationToken);
        var poById = productionOrders.ToDictionary(x => x.ProductionOrderId);

        var routeOperations = await db.PlanRouteOperationSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .OrderBy(x => x.SequenceNumber)
            .ToListAsync(cancellationToken);
        var packagingUnits = await db.PlanPackagingUnitSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .OrderBy(x => x.SequenceNumber)
            .ToListAsync(cancellationToken);

        // Configured downstream operations, including first HotRoll/Reheat, are persisted as
        // RouteOperationPlans. One route operation can have several scheduled blocks (for example one
        // per supplying heat), so preserve that one-to-many relationship in the read model.
        var routeOperationIds = routeOperations.Select(x => x.RouteOperationPlanId).ToArray();
        var operationRows = routeOperationIds.Length == 0
            ? new List<PlanOperationSnapshot>()
            : await db.PlanOperationSnapshots.AsNoTracking()
                .Where(x => x.PlanVersionId == plan.PlanVersionId && routeOperationIds.Contains(x.SourceEntityId))
                .OrderBy(x => x.StartUtc)
                .ToListAsync(cancellationToken);
        var operationViews = await BuildOperationViewsAsync(operationRows, cancellationToken);
        var operationsBySource = operationViews
            .GroupBy(x => x.SourceEntityId)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.StartUtc).ToArray());

        var allocationsByPlan = allocations
            .GroupBy(x => x.RollingPlanId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var routeOpsByUpstream = routeOperations
            .GroupBy(x => x.UpstreamPlanId)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.SequenceNumber).ToArray());

        var state = await db.PlanVersionStates.AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .Select(x => new { x.MaterialRequirementsJson, x.MaterialReservationsJson })
            .SingleOrDefaultAsync(cancellationToken);
        var requirementsByPo = DeserializeSnapshot<MaterialRequirement>(state?.MaterialRequirementsJson)
            .Where(x => x.ProductionOrderId.HasValue)
            .GroupBy(x => x.ProductionOrderId!.Value)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var reservationsByPo = DeserializeSnapshot<MaterialSupplyReservation>(state?.MaterialReservationsJson)
            .GroupBy(x => x.ProductionOrderId)
            .ToDictionary(x => x.Key, x => x.ToArray());

        var views = rollingPlans.Select(rp =>
        {
            allocationsByPlan.TryGetValue(rp.RollingPlanId, out var planAllocations);
            planAllocations ??= Array.Empty<PlanRollingPlanAllocationSnapshot>();

            var routeChain = WalkDownstreamChain(rp.RollingPlanId, routeOpsByUpstream).ToArray();
            var feedRoute = FeedPreparationChain(routeChain).ToArray();
            var feedOps = feedRoute
                .SelectMany(x => operationsBySource.TryGetValue(x.RouteOperationPlanId, out var values)
                    ? values
                    : Array.Empty<ScheduledProcessOperationView>())
                .OrderBy(x => x.StartUtc)
                .ToArray();
            var requiresReheat = feedRoute.Any(x => x.ProcessOperationType == ProcessOperationType.Reheat);

            var allocationViews = planAllocations
                .Select(a =>
                {
                    poById.TryGetValue(a.ProductionOrderId, out var po);
                    return new RollingAllocationView(
                        a.ProductionOrderId,
                        po?.ProductionOrderNumber ?? a.ProductionOrderId.ToString("N")[..8],
                        po?.SalesOrderNumber,
                        po?.DemandSource ?? DemandSourceType.MakeToOrder,
                        a.PlannedQuantityMt,
                        a.ExistingIntermediateInventoryMt,
                        a.FreshSteelQuantityMt,
                        BuildSupplyTrace(
                            a.ProductionOrderId,
                            rp.InputCrossSectionCode,
                            requiresReheat,
                            requirementsByPo,
                            reservationsByPo));
                })
                .ToArray();

            var downstream = routeChain
                .Select(route => new DownstreamRouteOperationView(
                    route.RouteOperationPlanId,
                    route.RouteCode,
                    route.ProcessOperationType,
                    route.SequenceNumber,
                    route.GradeCode,
                    route.InputCrossSectionCode,
                    route.OutputCrossSectionCode,
                    route.PlannedQuantityMt,
                    route.MinimumQueueTime,
                    route.MaximumQueueTime,
                    route.IsInventoryDecouplingPoint,
                    operationsBySource.TryGetValue(route.RouteOperationPlanId, out var scheduled)
                        ? scheduled
                        : Array.Empty<ScheduledProcessOperationView>()))
                .ToArray();

            var poIds = allocationViews.Select(a => a.ProductionOrderId).ToHashSet();
            var packaging = packagingUnits
                .Where(u => poIds.Contains(u.ProductionOrderId))
                .Select(u => new PlannedPackagingUnitView(
                    u.PlannedPackagingUnitId,
                    u.PackagingUnitType,
                    u.SequenceNumber,
                    u.PlannedWeightMt,
                    u.PlannedPieceCount,
                    u.CutLengthM,
                    u.PlannedIdentifier))
                .ToArray();

            // A RollingPlan is a demand/allocation anchor, not a committed mill assignment. Surface the
            // actual selected first-HotRoll resource only when every feed block landed on the same mill.
            var firstHotRoll = feedRoute.FirstOrDefault(x => x.ProcessOperationType == ProcessOperationType.HotRoll);
            var firstHotRollOps = firstHotRoll is not null &&
                                  operationsBySource.TryGetValue(firstHotRoll.RouteOperationPlanId, out var hotOps)
                ? hotOps
                : Array.Empty<ScheduledProcessOperationView>();
            var selectedMillIds = firstHotRollOps.Select(x => x.ResourceId).Distinct().ToArray();
            var selectedMillId = selectedMillIds.Length == 1 ? selectedMillIds[0] : (Guid?)null;
            var selectedMillCode = selectedMillId.HasValue
                ? firstHotRollOps.First(x => x.ResourceId == selectedMillId.Value).ResourceCode
                : null;

            return new RollingPlanView(
                rp.RollingPlanId,
                rp.SequenceNumber,
                rp.GradeCode,
                rp.RouteCode,
                rp.InputCrossSectionCode,
                rp.OutputCrossSectionCode,
                rp.PlannedQuantityMt,
                rp.ExistingIntermediateInventoryMt,
                rp.FreshSteelQuantityMt,
                selectedMillId,
                selectedMillCode,
                feedOps,
                downstream,
                allocationViews,
                packaging);
        }).ToArray();

        return new RollingFinishingWorkspaceView(
            plan,
            views.Length,
            rollingPlans.Sum(x => x.PlannedQuantityMt),
            rollingPlans.Sum(x => x.ExistingIntermediateInventoryMt),
            rollingPlans.Sum(x => x.FreshSteelQuantityMt),
            packagingUnits.Count(x => x.PackagingUnitType == PackagingUnitType.Bundle),
            packagingUnits.Count(x => x.PackagingUnitType == PackagingUnitType.Coil),
            views);
    }

    private static BilletSupplyTraceView? BuildSupplyTrace(
        Guid productionOrderId,
        string billetCrossSectionCode,
        bool requiresReheat,
        IReadOnlyDictionary<Guid, MaterialRequirement[]> requirementsByPo,
        IReadOnlyDictionary<Guid, MaterialSupplyReservation[]> reservationsByPo)
    {
        requirementsByPo.TryGetValue(productionOrderId, out var requirements);
        reservationsByPo.TryGetValue(productionOrderId, out var reservations);
        if ((requirements is null || requirements.Length == 0) &&
            (reservations is null || reservations.Length == 0))
            return null;

        var requirement = requirements?
            .Where(x => string.Equals(x.CrossSectionCode, billetCrossSectionCode, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.ShortfallQuantityMt)
            .FirstOrDefault();

        var sources = (reservations ?? Array.Empty<MaterialSupplyReservation>())
            .Where(x => string.Equals(x.CrossSectionCode, billetCrossSectionCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.AvailableFromUtc)
            .Select(x => new BilletSupplyAllocationView(
                x.SupplyReference,
                x.InventoryStage,
                x.ExternalSourceType,
                x.QuantityMt,
                x.AvailableFromUtc,
                x.LocationCode,
                x.Status))
            .ToArray();

        return new BilletSupplyTraceView(
            requirement?.Status,
            requirement?.ShortfallQuantityMt ?? 0m,
            requirement?.LateSupplyQuantityMt ?? 0m,
            requirement?.Explanation,
            requiresReheat,
            sources);
    }

    private static IReadOnlyCollection<T> DeserializeSnapshot<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<T>();
        return JsonSerializer.Deserialize<T[]>(json, SnapshotJsonOptions) ?? Array.Empty<T>();
    }

    /// <summary>
    /// Feed preparation is the configured route from RollingPlan to the first HotRoll, inclusive.
    /// Reheat may or may not exist in that chain; arbitrary required pre-roll operations remain visible
    /// in DownstreamOperations but do not get mislabeled as rolling-feed heating.
    /// </summary>
    private static IEnumerable<PlanRouteOperationSnapshot> FeedPreparationChain(
        IEnumerable<PlanRouteOperationSnapshot> routeChain)
    {
        foreach (var step in routeChain)
        {
            if (step.ProcessOperationType is ProcessOperationType.Reheat or ProcessOperationType.HotRoll)
                yield return step;
            if (step.ProcessOperationType == ProcessOperationType.HotRoll)
                yield break;
        }
    }

    /// <summary>
    /// Route operations chain off each other. Walk from the RollingPlan anchor through the persisted
    /// RouteOperationPlan links instead of assuming a one-level or first-HotRoll split.
    /// </summary>
    private static IEnumerable<PlanRouteOperationSnapshot> WalkDownstreamChain(
        Guid rollingPlanId,
        IReadOnlyDictionary<Guid, PlanRouteOperationSnapshot[]> routeOpsByUpstream)
    {
        var frontier = new Queue<Guid>();
        frontier.Enqueue(rollingPlanId);
        var visited = new HashSet<Guid>();

        while (frontier.Count > 0)
        {
            var upstreamId = frontier.Dequeue();
            if (!routeOpsByUpstream.TryGetValue(upstreamId, out var steps)) continue;

            foreach (var step in steps.OrderBy(x => x.SequenceNumber))
            {
                if (!visited.Add(step.RouteOperationPlanId)) continue;
                yield return step;
                frontier.Enqueue(step.RouteOperationPlanId);
            }
        }
    }
}