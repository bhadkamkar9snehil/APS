using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

/// <summary>
/// Planner-owned service-window edits live on the canonical SalesOrderDemandState. ERP reconciliation
/// keeps owning requested/confirmed dates and priority; this service owns only the additional movement
/// tolerance, so synchronization cannot accidentally erase planner policy.
/// </summary>
public sealed class OrderServicePolicyService(ApsDbContext db) : IOrderServicePolicyService
{
    public async Task<IReadOnlyCollection<OrderServicePolicy>> GetAsync(
        IReadOnlyCollection<Guid>? salesOrderIds = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.SalesOrderDemandStates
            .AsNoTracking()
            .Include(x => x.SalesOrder)
            .AsQueryable();

        if (salesOrderIds is { Count: > 0 })
        {
            var ids = salesOrderIds.ToHashSet();
            query = query.Where(x => ids.Contains(x.SalesOrderId));
        }

        var states = await query
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.ConfirmedDeliveryDate ?? x.CustomerRequiredDate)
            .ToArrayAsync(cancellationToken);

        return states.Select(ToPolicy).ToArray();
    }

    public async Task<OrderServicePolicy> UpdateAsync(
        UpdateOrderServicePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var state = await db.SalesOrderDemandStates
            .Include(x => x.SalesOrder)
            .SingleOrDefaultAsync(x => x.SalesOrderId == request.SalesOrderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sales Order {request.SalesOrderId} has no demand state. Reconcile the order before editing its service policy.");

        var target = state.ConfirmedDeliveryDate ?? state.CustomerRequiredDate;
        Validate(request, target);

        state.ServiceCommitment = request.ServiceCommitment;
        state.EarliestAcceptableDeliveryDate = request.EarliestAcceptableDeliveryDate;
        state.LatestAcceptableDeliveryDate = request.ServiceCommitment == ServiceCommitmentClass.Hard
            ? null
            : request.LatestAcceptableDeliveryDate;

        await db.SaveChangesAsync(cancellationToken);
        return ToPolicy(state);
    }

    private static void Validate(UpdateOrderServicePolicyRequest request, DateTime target)
    {
        if (request.EarliestAcceptableDeliveryDate > target)
            throw new ArgumentException("Earliest acceptable delivery must be on or before the requested/confirmed target date.");

        if (request.LatestAcceptableDeliveryDate < target)
            throw new ArgumentException("Latest acceptable delivery must be on or after the requested/confirmed target date.");

        if (request.ServiceCommitment == ServiceCommitmentClass.Hard &&
            request.LatestAcceptableDeliveryDate.HasValue &&
            request.LatestAcceptableDeliveryDate.Value != target)
        {
            throw new ArgumentException("Hard commitments cannot move later than the requested/confirmed target date.");
        }

        if (request.ServiceCommitment == ServiceCommitmentClass.Flexible &&
            request.EarliestAcceptableDeliveryDate is null &&
            request.LatestAcceptableDeliveryDate is null)
        {
            throw new ArgumentException("Flexible commitments require at least one acceptable delivery boundary.");
        }
    }

    private static OrderServicePolicy ToPolicy(SalesOrderDemandState state)
    {
        var order = state.SalesOrder;
        return new OrderServicePolicy(
            state.SalesOrderId,
            order?.SalesOrderNumber ?? state.SalesOrderId.ToString("N"),
            order?.ItemNumber ?? "—",
            order?.CustomerCode,
            state.CustomerRequiredDate,
            state.ConfirmedDeliveryDate,
            state.Priority,
            order?.ExternalStatus,
            state.ServiceCommitment,
            state.EarliestAcceptableDeliveryDate,
            state.LatestAcceptableDeliveryDate);
    }
}
