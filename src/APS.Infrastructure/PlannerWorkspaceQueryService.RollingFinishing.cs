using System.Text.Json;
using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed partial class PlannerWorkspaceQueryService
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ProcessOperationType[] RollingFeedProcessTypes =
    {
        ProcessOperationType.Reheat,
        ProcessOperationType.HotRoll
    };

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

        var feedOperationRows = await db.PlanOperationSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId && RollingFeedProcessTypes.Contains(x.ProcessOperationType))
            .OrderBy(x => x.StartUtc)
            .ToListAsync(cancellationToken);
        var routeOperationIds = routeOperations.Select(x => x.RouteOperationPlanId).ToArray();
        var downstreamOperationRows = routeOperationIds.Length == 0
            ? new List<PlanOperationSnapshot>()
            : await db.PlanOperationSnapshots.AsNoTracking()
                .Where(x => x.PlanVersionId == plan.PlanVersionId && routeOperationIds.Contains(x.SourceEntityId))
                .ToListAsync(cancellationToken);

        var feedOperationViews = await BuildOperationViewsAsync(feedOperationRows, cancellationToken);
        var downstreamOperationViews = await BuildOperationViewsAsync(downstreamOperationRows, cancellationToken);
        var downstreamOpBySource = downstreamOperationViews.ToDictionary(x => x.SourceEntityId);

        var millResourceIds = rollingPlans
            .Where(x => x.RollingMillResourceId.HasValue)
            .Select(x => x.RollingMillResourceId!.Value)
            .Distinct()
            .ToArray();
        var millResources = millResourceIds.Length == 0
            ? new Dictionary<Guid, Resource>()
            : await db.Resources.AsNoTracking()
                .Where(x => millResourceIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var allocationsByPlan = allocations.GroupBy(x => x.RollingPlanId).ToDictionary(x => x.Key, x => x.ToArray());
        var feedOpsByPlan = feedOperationViews.GroupBy(x => x.SourceEntityId).ToDictionary(x => x.Key, x => x.ToArray());
        var routeOpsByUpstream = routeOperations.GroupBy(x => x.UpstreamPlanId).ToDictionary(x => x.Key, x => x.ToArray());

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

            feedOpsByPlan.TryGetValue(rp.RollingPlanId, out var feedOps);
            feedOps ??= Array.Empty<ScheduledProcessOperationView>();
            var requiresReheat = feedOps.Any(x => x.ProcessOperationType == ProcessOperationType.Reheat);

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
                        BuildSupplyTrace(a.ProductionOrderId, rp.InputCrossSectionCode, requiresReheat, requirementsByPo, reservationsByPo));
                })
                .ToArray();

            var downstream = WalkDownstreamChain(rp.RollingPlanId, routeOpsByUpstream)
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
                    downstreamOpBySource.TryGetValue(route.RouteOperationPlanId, out var scheduledOp) ? scheduledOp : null))
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

            var millCode = rp.RollingMillResourceId.HasValue && millResources.TryGetValue(rp.RollingMillResourceId.Value, out var mill)
                ? mill.Code
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
                rp.RollingMillResourceId,
                millCode,
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
        if ((requirements is null || requirements.Length == 0) && (reservations is null || reservations.Length == 0))
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
            requirement?.LateSupplyQuantity ?? 0m,
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
    /// Downstream route operations chain off each other (cold rolling -> TMT -> cooling -> cutting
    /// -> bundling/coiling), each step's UpstreamPlanId pointing at the previous step's
    /// RouteOperationPlanId rather than always back at the originating rolling plan. Walk the chain
    /// rather than assuming a single level.
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

            foreach (var step in steps)
            {
                if (!visited.Add(step.RouteOperationPlanId)) continue;
                yield return step;
                frontier.Enqueue(step.RouteOperationPlanId);
            }
        }
    }
}
