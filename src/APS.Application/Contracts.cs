using APS.Domain;

namespace APS.Application;

public sealed record StockPolicy(
    string PolicyCode,
    string MaterialCode,
    string GradeCode,
    string FinalCrossSectionCode,
    string CasterSectionCode,
    string RouteCode,
    decimal TargetStockMt,
    decimal MinimumReplenishmentMt,
    decimal MaximumReplenishmentMt,
    DateTime RequiredDate,
    int Priority = 0);

public sealed record MtsProductionOrderProposal(
    ProductionOrder? ProductionOrder,
    decimal ProjectedAvailableStockMt,
    decimal CalculatedReplenishmentMt,
    string Reason);

public sealed record CampaignPlanningPolicy(
    decimal NominalHeatSizeMt,
    decimal MinimumHeatSizeMt,
    decimal MaximumHeatSizeMt,
    decimal TargetCampaignQuantityMt,
    decimal MaximumCampaignQuantityMt);

public sealed record CampaignPlanningRequest(
    IReadOnlyCollection<ProductionOrder> ProductionOrders,
    IReadOnlyCollection<InventoryPosition> Inventory,
    CampaignPlanningPolicy Policy,
    string CampaignNumberPrefix = "CMP");

public sealed record CampaignPlanningResult(
    IReadOnlyCollection<Campaign> Campaigns,
    IReadOnlyCollection<ProductionOrder> FullyCoveredByInventory,
    IReadOnlyDictionary<Guid, decimal> NettedRequirementsMt);

public interface IMtsProductionOrderService
{
    MtsProductionOrderProposal Propose(StockPolicy policy, InventoryPosition inventory, decimal alreadyFirmedSupplyMt = 0m);
}

public interface ICampaignPlanningService
{
    CampaignPlanningResult FormCampaigns(CampaignPlanningRequest request);
}

public interface IInventorySnapshotProvider
{
    Task<IReadOnlyCollection<InventoryPosition>> GetInventoryAsync(CancellationToken cancellationToken = default);
}

public interface IExecutionActualProvider
{
    Task<IReadOnlyCollection<ExecutionActual>> GetActualsAsync(DateTime changedSinceUtc, CancellationToken cancellationToken = default);
}

public sealed record ExecutionActual(
    string ExternalWorkOrderId,
    WorkOrderStatus Status,
    DateTime? ActualStart,
    DateTime? ActualEnd,
    decimal ActualQuantityMt,
    string? MaterialCode,
    string? GradeCode,
    string? CrossSectionCode,
    DateTime ChangedOnUtc);

public interface IPlanPublisher
{
    Task PublishAsync(PlanRelease release, CancellationToken cancellationToken = default);
}

public sealed record PlanRelease(
    Guid PlanVersionId,
    IReadOnlyCollection<WorkOrder> WorkOrders,
    IReadOnlyCollection<ScheduledOperation> Operations);
