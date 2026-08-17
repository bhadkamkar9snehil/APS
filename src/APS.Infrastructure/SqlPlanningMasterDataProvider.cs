using APS.Application;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class SqlPlanningMasterDataProvider(ApsDbContext db) : IPlanningMasterDataProvider
{
    public async Task<PlanningMasterDataSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var routes = await db.ManufacturingRoutes
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.RouteCode)
            .ToListAsync(cancellationToken);
        var routeCodes = routes.Select(x => x.RouteCode).ToArray();

        return new PlanningMasterDataSnapshot(
            await db.Plants.AsNoTracking().OrderBy(x => x.Code).ToListAsync(cancellationToken),
            await db.ProcessStages.AsNoTracking().OrderBy(x => x.SequenceNumber).ThenBy(x => x.Code).ToListAsync(cancellationToken),
            await db.Resources.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync(cancellationToken),
            await db.ResourceCapabilities.AsNoTracking().ToListAsync(cancellationToken),
            await db.ResourceCalendars.AsNoTracking().OrderBy(x => x.Start).ToListAsync(cancellationToken),
            await db.PlantFlowLinks.AsNoTracking().Where(x => x.IsEnabled).ToListAsync(cancellationToken),
            await db.TransitionRules.AsNoTracking().ToListAsync(cancellationToken),
            routes,
            await db.ManufacturingRouteOperations.AsNoTracking()
                .Where(x => routeCodes.Contains(x.RouteCode))
                .OrderBy(x => x.RouteCode)
                .ThenBy(x => x.SequenceNumber)
                .ToListAsync(cancellationToken),
            await db.RouteResourceCapabilities.AsNoTracking()
                .Where(x => routeCodes.Contains(x.RouteCode))
                .ToListAsync(cancellationToken));
    }
}
