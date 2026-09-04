using APS.Domain;

namespace APS.Application;

/// <summary>
/// Planner-facing service policy for one Sales Order item. Priority/rush is intentionally absent:
/// urgency and due-date flexibility are independent planning dimensions.
/// </summary>
public sealed record OrderServicePolicy(
    Guid SalesOrderId,
    string SalesOrderNumber,
    string ItemNumber,
    string? CustomerCode,
    DateTime CustomerRequiredDate,
    DateTime? ConfirmedDeliveryDate,
    int Priority,
    string? ExternalStatus,
    ServiceCommitmentClass ServiceCommitment,
    DateTime? EarliestAcceptableDeliveryDate,
    DateTime? LatestAcceptableDeliveryDate)
{
    public DateTime TargetDeliveryDate => ConfirmedDeliveryDate ?? CustomerRequiredDate;
}

public sealed record UpdateOrderServicePolicyRequest(
    Guid SalesOrderId,
    ServiceCommitmentClass ServiceCommitment,
    DateTime? EarliestAcceptableDeliveryDate = null,
    DateTime? LatestAcceptableDeliveryDate = null);

public interface IOrderServicePolicyService
{
    Task<IReadOnlyCollection<OrderServicePolicy>> GetAsync(
        IReadOnlyCollection<Guid>? salesOrderIds = null,
        CancellationToken cancellationToken = default);

    Task<OrderServicePolicy> UpdateAsync(
        UpdateOrderServicePolicyRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One small projection owns the translation from commercial delivery flexibility to manufacturing
/// timing. Requested/confirmed delivery remains the optimization target; the latest acceptable date is
/// a separate release boundary. This avoids quietly turning tolerance into the new target.
/// </summary>
public static class OrderServiceWindow
{
    public static DemandOrchestrationResult Apply(
        DemandOrchestrationResult demand,
        IReadOnlyCollection<OrderServicePolicy> policies)
    {
        var policyBySalesOrder = policies.ToDictionary(x => x.SalesOrderId);
        var items = demand.MakeToOrderDemand
            .Select(item => Apply(item, policyBySalesOrder.GetValueOrDefault(item.SalesOrderId)))
            .ToArray();

        // ProductionOrder.RequiredDate remains the preferred production target. Tolerance is immutable
        // Plan Version evidence and is enforced at release, so the campaign and finite-schedule objective
        // still tries to meet the customer's requested/confirmed target instead of deliberately drifting
        // work to the edge of the allowed window.
        return demand with { MakeToOrderDemand = items };
    }

    public static DemandOrchestrationItem Apply(
        DemandOrchestrationItem item,
        OrderServicePolicy? policy)
    {
        var targetDelivery = item.ConfirmedDeliveryDate ?? item.CustomerRequiredDate;
        var commitment = policy?.ServiceCommitment ?? ServiceCommitmentClass.Standard;
        var earliestDelivery = policy?.EarliestAcceptableDeliveryDate;
        var latestDelivery = EffectiveLatestDelivery(targetDelivery, commitment, policy?.LatestAcceptableDeliveryDate);

        // ProductionRequiredByDate already contains this planning run's QA/packing/dispatch offset.
        // Reuse that exact offset for the acceptable boundaries rather than reimplementing lead-time
        // policy here.
        var postProductionLead = targetDelivery - item.ProductionRequiredByDate;
        var productionEarliest = earliestDelivery?.Subtract(postProductionLead);
        var productionLatest = latestDelivery.Subtract(postProductionLead);

        return item with
        {
            ServiceCommitment = commitment,
            EarliestAcceptableDeliveryDate = earliestDelivery,
            LatestAcceptableDeliveryDate = latestDelivery,
            ProductionEarliestAcceptableDate = productionEarliest,
            ProductionLatestAcceptableDate = productionLatest
        };
    }

    public static DateTime EffectiveLatestDelivery(
        DateTime targetDelivery,
        ServiceCommitmentClass commitment,
        DateTime? configuredLatest) =>
        commitment == ServiceCommitmentClass.Hard
            ? targetDelivery
            : configuredLatest ?? targetDelivery;
}
