using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class PlanReleaseRepository(ApsDbContext db) : IPlanReleaseRepository
{
    public async Task<PlanRelease> PersistAsync(
        PlanRelease release,
        CancellationToken cancellationToken = default)
    {
        var version = await db.PlanVersions
            .SingleOrDefaultAsync(x => x.Id == release.PlanVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("Plan version must be persisted before it can be released.");

        if (version.IsReleased)
        {
            return release;
        }

        var state = await db.PlanVersionStates
            .SingleAsync(x => x.PlanVersionId == release.PlanVersionId, cancellationToken);
        if (state.Status != PlanVersionStatus.Approved || !state.IsActive)
        {
            throw new InvalidOperationException(
                $"Plan version {version.VersionNumber} in state {state.Status} cannot be released; an active approved Plan Version is required.");
        }

        foreach (var workOrder in release.WorkOrders)
        {
            workOrder.Status = WorkOrderStatus.Released;
            foreach (var allocation in workOrder.Allocations)
            {
                allocation.ProductionOrder = null;
                allocation.WorkOrder = workOrder;
            }
            db.WorkOrders.Add(workOrder);
        }

        db.ScheduledOperations.AddRange(release.Operations);
        version.IsReleased = true;
        state.Status = PlanVersionStatus.Released;
        state.IsActive = true;

        await db.SaveChangesAsync(cancellationToken);
        return release;
    }
}
