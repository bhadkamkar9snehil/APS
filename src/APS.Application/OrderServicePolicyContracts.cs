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
/// timing. This prevents campaign, scheduling, release-readiness and UI layers from each inventing a
/// different meaning for the same order window.
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

        var effectiveDueByProductionOrder = items
            .Where(x => x.ProductionOrderId.HasValue && x.ProductionLatestAcceptableDate.HasValue)
            .ToDictionary(x => x.ProductionOrderId!.Value, x => x.ProductionLatestAcceptableDate!.Value);

        // Planning receives detached projections so a flexible service window cannot overwrite the
        // canonical ProductionOrder.RequiredDate in the tracked application database. The effective
        // latest date exists only in this planning run and its immutable Plan Version evidence.
        var productionOrders = demand.ProductionOrders
            .Select(order => CloneForPlanning(
                order,
                effectiveDueByProductionOrder.GetValueOrDefault(order.Id, order.RequiredDate)))
            .ToArray();
        var cloneById = productionOrders.ToDictionary(x => x.Id);
        var mts = demand.MakeToStockProductionOrders
            .Select(order => cloneById.GetValueOrDefault(order.Id) ?? CloneForPlanning(order, order.RequiredDate))
            .ToArray();

        return demand with
        {
            ProductionOrders = productionOrders,
            MakeToOrderDemand = items,
            MakeToStockProductionOrders = mts
        };
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

    private static ProductionOrder CloneForPlanning(ProductionOrder source, DateTime effectiveRequiredDate) => new()
    {
        Id = source.Id,
        ProductionOrderNumber = source.ProductionOrderNumber,
        DemandSource = source.DemandSource,
        MaterialCode = source.MaterialCode,
        GradeCode = source.GradeCode,
        SteelGradeId = source.SteelGradeId,
        SteelGrade = source.SteelGrade,
        GradeFamilyCode = source.GradeFamilyCode,
        GradeSequenceClassCode = source.GradeSequenceClassCode,
        FinalCrossSectionCode = source.FinalCrossSectionCode,
        CasterSectionCode = source.CasterSectionCode,
        RouteCode = source.RouteCode,
        ProductFamilyCode = source.ProductFamilyCode,
        PlannedQuantityMt = source.PlannedQuantityMt,
        RemainingQuantityMt = source.RemainingQuantityMt,
        RequiredDate = effectiveRequiredDate,
        Priority = source.Priority,
        Status = source.Status,
        SalesOrderId = source.SalesOrderId,
        SalesOrder = source.SalesOrder,
        Requirement = source.Requirement,
        TargetStockMt = source.TargetStockMt,
        ProjectedAvailableStockMt = source.ProjectedAvailableStockMt,
        StockPolicyCode = source.StockPolicyCode
    };
}
