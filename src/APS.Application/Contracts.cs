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
    int Priority = 0,
    string? GradeSequenceClassCode = null);

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
    decimal MaximumCampaignQuantityMt,
    bool AllowMtoMtsMixing = true,
    bool AllowMixedGradesWithinSequenceClass = true);

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

public interface ITraceabilityService
{
    Task<WorkOrderTrace?> GetWorkOrderTraceAsync(Guid workOrderId, CancellationToken cancellationToken = default);
    Task<MaterialLotTrace?> GetMaterialLotTraceAsync(Guid materialLotId, CancellationToken cancellationToken = default);
}

public sealed record WorkOrderTrace(
    Guid WorkOrderId,
    string WorkOrderNumber,
    WorkOrderType WorkOrderType,
    Guid? CampaignId,
    decimal PlannedQuantityMt,
    decimal ActualQuantityMt,
    IReadOnlyCollection<ProductionOrderTrace> ProductionOrders,
    IReadOnlyCollection<ProducedLotTrace> ProducedLots);

public sealed record ProductionOrderTrace(
    Guid ProductionOrderId,
    string ProductionOrderNumber,
    DemandSourceType DemandSource,
    decimal AllocatedQuantityMt,
    string? SalesOrderNumber,
    string? SalesOrderItem,
    Guid? SalesOrderId);

public sealed record ProducedLotTrace(
    Guid MaterialLotId,
    string LotNumber,
    decimal QuantityMt,
    string GradeCode,
    string CrossSectionCode);

public sealed record MaterialLotTrace(
    Guid MaterialLotId,
    string LotNumber,
    string MaterialCode,
    string GradeCode,
    string CrossSectionCode,
    decimal QuantityMt,
    Guid? ProducedByWorkOrderId,
    IReadOnlyCollection<ProductionOrderTrace> AllocatedProductionOrders,
    IReadOnlyCollection<MaterialLotParentTrace> ParentLots,
    IReadOnlyCollection<MaterialLotChildTrace> ChildLots);

public sealed record MaterialLotParentTrace(Guid MaterialLotId, string LotNumber, decimal QuantityMt);
public sealed record MaterialLotChildTrace(Guid MaterialLotId, string LotNumber, decimal QuantityMt);
