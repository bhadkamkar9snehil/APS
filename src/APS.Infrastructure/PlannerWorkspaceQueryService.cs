using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class PlannerWorkspaceQueryService(ApsDbContext db) : IPlannerWorkspaceQueryService
{
    public async Task<PlanContextView?> GetCurrentPlanAsync(CancellationToken cancellationToken = default)
    {
        var state = await db.PlanVersionStates
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.ReferenceTimeUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return state is null
            ? null
            : await BuildPlanContextAsync(state.PlanVersionId, cancellationToken);
    }

    public Task<PlanContextView?> GetPlanContextAsync(
        Guid planVersionId,
        CancellationToken cancellationToken = default) =>
        BuildPlanContextAsync(planVersionId, cancellationToken);

    public async Task<IReadOnlyCollection<PlanVersionListItemView>> GetRecentPlanVersionsAsync(
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        var count = Math.Clamp(take, 1, 100);
        var rows = await (
                from version in db.PlanVersions.AsNoTracking()
                join state in db.PlanVersionStates.AsNoTracking()
                    on version.Id equals state.PlanVersionId
                orderby version.CreatedOnUtc descending
                select new PlanVersionListItemView(
                    version.Id,
                    version.VersionNumber,
                    state.ParentPlanVersionId,
                    state.Status,
                    state.Trigger,
                    version.CreatedOnUtc,
                    state.ReferenceTimeUtc,
                    state.HorizonStartUtc,
                    state.HorizonEndUtc,
                    state.SolverStatus,
                    state.IsActive,
                    version.IsReleased,
                    version.Reason))
            .Take(count)
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<ControlTowerView?> GetControlTowerAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default)
    {
        var plan = planVersionId.HasValue
            ? await BuildPlanContextAsync(planVersionId.Value, cancellationToken)
            : await GetCurrentPlanAsync(cancellationToken);

        if (plan is null) return null;

        var operations = await db.PlanOperationSnapshots
            .AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .OrderBy(x => x.StartUtc)
            .ToListAsync(cancellationToken);

        var inventory = await db.PlanInventoryAllocationSnapshots
            .AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .ToListAsync(cancellationToken);

        var materialUnits = await db.PlanMaterialUnitSnapshots
            .AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .ToListAsync(cancellationToken);

        var scheduledOperations = await db.ScheduledOperations
            .AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .ToListAsync(cancellationToken);
        var workOrderIds = scheduledOperations
            .Select(x => x.WorkOrderId)
            .Distinct()
            .ToArray();

        var workOrders = workOrderIds.Length == 0
            ? new List<WorkOrder>()
            : await db.WorkOrders
                .AsNoTracking()
                .Where(x => workOrderIds.Contains(x.Id))
                .ToListAsync(cancellationToken);

        var resourceIds = operations
            .Select(x => x.ResourceId)
            .Distinct()
            .ToArray();
        var resources = resourceIds.Length == 0
            ? new List<Resource>()
            : await db.Resources
                .AsNoTracking()
                .Where(x => resourceIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
        var resourceById = resources.ToDictionary(x => x.Id);

        var resourcePressure = operations
            .GroupBy(x => x.ResourceId)
            .Select(group =>
            {
                resourceById.TryGetValue(group.Key, out var resource);
                var ordered = group.OrderBy(x => x.StartUtc).ToArray();
                return new ResourcePressureView(
                    group.Key,
                    resource?.Code ?? group.Key.ToString("N")[..8],
                    resource?.Name ?? "Unknown resource",
                    resource?.ProcessUnitType ?? ProcessUnitType.Unknown,
                    resource?.OperatingState ?? ResourceOperatingState.Disabled,
                    ordered.Length,
                    Math.Round(ordered.Sum(x => Math.Max(0d, (x.EndUtc - x.StartUtc).TotalHours)), 2),
                    ordered.FirstOrDefault()?.StartUtc,
                    ordered.LastOrDefault()?.EndUtc);
            })
            .OrderByDescending(x => x.ScheduledHours)
            .ThenBy(x => x.ResourceCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var materialSummary = inventory
            .GroupBy(x => x.Stage)
            .Select(group => new PlanMaterialSummaryView(
                group.Key,
                decimal.Round(group.Sum(x => x.QuantityMt), 4),
                group.Count()))
            .OrderBy(x => x.Stage)
            .ToArray();

        var footprint = new PlanFootprintView(
            operations.Count,
            resourceIds.Length,
            operations.Count == 0 ? null : operations.Min(x => x.StartUtc),
            operations.Count == 0 ? null : operations.Max(x => x.EndUtc),
            inventory.Count,
            decimal.Round(inventory.Sum(x => x.QuantityMt), 4),
            materialUnits.Count,
            decimal.Round(materialUnits.Sum(x => x.QuantityMt), 4),
            workOrders.Count,
            workOrders.Count(x => x.Status == WorkOrderStatus.Released),
            workOrders.Count(x => x.Status == WorkOrderStatus.Running),
            workOrders.Count(x => x.Status == WorkOrderStatus.Held),
            workOrders.Count(x => x.Status == WorkOrderStatus.Completed));

        return new ControlTowerView(
            plan,
            footprint,
            resourcePressure,
            materialSummary,
            DateTime.UtcNow);
    }

    private async Task<PlanContextView?> BuildPlanContextAsync(
        Guid planVersionId,
        CancellationToken cancellationToken)
    {
        var version = await db.PlanVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == planVersionId, cancellationToken);
        if (version is null) return null;

        var state = await db.PlanVersionStates
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.PlanVersionId == planVersionId, cancellationToken);
        if (state is null) return null;

        return new PlanContextView(
            version.Id,
            version.VersionNumber,
            state.ParentPlanVersionId,
            state.Status,
            state.Trigger,
            version.CreatedOnUtc,
            state.ReferenceTimeUtc,
            state.HorizonStartUtc,
            state.HorizonEndUtc,
            state.SolverStatus,
            state.ObjectiveValue,
            state.IsActive,
            version.IsReleased,
            version.Reason);
    }
}
