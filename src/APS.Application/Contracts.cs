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
    IReadOnlyCollection<ProductionOrder> FullyCoveredByFinishedGoods,
    IReadOnlyDictionary<Guid, decimal> RollingRequirementsMt,
    IReadOnlyDictionary<Guid, decimal> FreshSteelRequirementsMt,
    IReadOnlyDictionary<Guid, decimal> IntermediateInventoryAllocatedMt);

public interface IMtsProductionOrderService
{
    MtsProductionOrderProposal Propose(StockPolicy policy, InventoryPosition inventory, decimal alreadyFirmedSupplyMt = 0m);
}

public interface ICampaignPlanningService
{
    CampaignPlanningResult FormCampaigns(CampaignPlanningRequest request);
}

public sealed record ProductionStructurePlanningPolicy(
    int MaximumHeatsPerCastSequence = 8,
    int DefaultCastingMinutesPerHeat = 55,
    int SequenceBreakPenalty = 500,
    decimal CastingYieldPct = 100m,
    int DefaultRollingMinutesPer100Mt = 120,
    bool AllowCrossCampaignCastSequences = true,
    bool AllowCrossCampaignRollingPlans = true);

public sealed record ProductionStructurePlanningRequest(
    IReadOnlyCollection<Campaign> Campaigns,
    IReadOnlyCollection<Resource> Resources,
    IReadOnlyCollection<ResourceCapability> Capabilities,
    IReadOnlyCollection<TransitionRule> TransitionRules,
    IReadOnlyCollection<PlantFlowLink> FlowLinks,
    ProductionStructurePlanningPolicy Policy);

public sealed record PlannedBilletSupply(
    Guid CampaignId,
    Guid CampaignHeatId,
    Guid CastSequenceId,
    Guid CasterResourceId,
    string GradeCode,
    string CrossSectionCode,
    decimal QuantityMt);

public enum PlanningIssueSeverity { Warning = 1, Error = 2 }

public sealed record PlanningIssue(
    PlanningIssueSeverity Severity,
    string Code,
    string Message,
    Guid? SourceId = null);

public sealed record ProductionStructurePlanningResult(
    IReadOnlyCollection<CastSequence> CastSequences,
    IReadOnlyCollection<RollingPlan> RollingPlans,
    IReadOnlyCollection<PlannedBilletSupply> PlannedBilletSupplies,
    IReadOnlyCollection<FiniteScheduleTask> SchedulingTasks,
    IReadOnlyCollection<PlanningIssue> Issues);

public interface IProductionStructurePlanningService
{
    ProductionStructurePlanningResult Build(ProductionStructurePlanningRequest request);
}

public enum FiniteScheduleTaskType { Casting = 1, HotRolling = 2, ColdRolling = 3, Finishing = 4 }

public sealed record FiniteScheduleResourceOption(
    Guid ResourceId,
    int DurationMinutes,
    int AssignmentPenalty = 0);

public sealed record FiniteScheduleDependency(
    Guid PredecessorTaskId,
    int MinimumLagMinutes = 0,
    int? MaximumLagMinutes = null);

public sealed record FiniteScheduleTask(
    Guid TaskId,
    Guid SourceEntityId,
    FiniteScheduleTaskType TaskType,
    string Name,
    string GradeCode,
    string CrossSectionCode,
    decimal QuantityMt,
    DateTime? EarliestStartUtc,
    DateTime? DueUtc,
    int Priority,
    IReadOnlyCollection<FiniteScheduleResourceOption> ResourceOptions,
    IReadOnlyCollection<FiniteScheduleDependency> Dependencies);

public sealed record FiniteScheduleRequest(
    DateTime HorizonStartUtc,
    DateTime HorizonEndUtc,
    IReadOnlyCollection<FiniteScheduleTask> Tasks,
    IReadOnlyCollection<Resource> Resources,
    IReadOnlyCollection<ResourceCalendar> ResourceCalendars,
    IReadOnlyCollection<TransitionRule> TransitionRules,
    int MaxSolverSeconds = 20);

public sealed record FiniteScheduleAssignment(
    Guid TaskId,
    Guid SourceEntityId,
    Guid ResourceId,
    DateTime StartUtc,
    DateTime EndUtc);

public sealed record FiniteScheduleResult(
    string SolverStatus,
    bool IsFeasible,
    long ObjectiveValue,
    IReadOnlyCollection<FiniteScheduleAssignment> Assignments,
    IReadOnlyCollection<PlanningIssue> Issues);

public interface IFiniteScheduleOptimizer
{
    FiniteScheduleResult Solve(FiniteScheduleRequest request);
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
