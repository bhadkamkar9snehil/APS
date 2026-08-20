using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed partial class PlannerWorkspaceQueryService
{
    public async Task<MaterialFlowWorkspaceView?> GetMaterialFlowAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default)
    {
        var plan = await ResolvePlanAsync(planVersionId, cancellationToken);
        if (plan is null) return null;

        var snapshot = await plans.GetAsync(plan.PlanVersionId, cancellationToken);
        var ledger = snapshot?.MaterialLedger ?? Array.Empty<MaterialBalanceEvent>();
        var reservations = snapshot?.MaterialReservations ?? Array.Empty<MaterialSupplyReservation>();

        var poNumbers = await ResolveProductionOrderNumbersAsync(
            reservations.Select(x => x.ProductionOrderId).Distinct(), cancellationToken);

        var pools = ledger
            .GroupBy(x => x.MaterialPoolKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var running = 0m;
                var events = group
                    .OrderBy(x => x.EffectiveAtUtc)
                    .Select(e =>
                    {
                        running += e.QuantityDeltaMt;
                        return new MaterialFlowEventView(e.EventType, e.QuantityDeltaMt, running, e.EffectiveAtUtc, e.SupplyReference, e.Explanation);
                    })
                    .ToArray();
                return new MaterialFlowPoolView(
                    group.Key,
                    first.GradeCode,
                    first.CrossSectionCode,
                    first.MaterialSpecificationCode,
                    first.LocationCode,
                    events.Length == 0 ? 0m : events[^1].RunningBalanceMt,
                    events);
            })
            .OrderBy(x => x.GradeCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.CrossSectionCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var reservationViews = reservations
            .OrderBy(x => x.AvailableFromUtc)
            .Select(r => new MaterialFlowReservationView(
                r.ProductionOrderId,
                poNumbers.GetValueOrDefault(r.ProductionOrderId),
                r.GradeCode,
                r.CrossSectionCode,
                r.InventoryStage,
                r.QuantityMt,
                r.AvailableFromUtc,
                r.Status))
            .ToArray();

        return new MaterialFlowWorkspaceView(plan, pools, reservationViews);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveProductionOrderNumbersAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken)
    {
        var idArray = ids.ToArray();
        if (idArray.Length == 0) return new Dictionary<Guid, string>();
        return await db.ProductionOrders.AsNoTracking()
            .Where(x => idArray.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.ProductionOrderNumber, cancellationToken);
    }
}
