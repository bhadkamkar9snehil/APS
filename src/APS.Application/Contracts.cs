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
    bool AllowMixedGradesWithinSequenceClass = true,
    decimal ExpectedCastingYieldPct = 100m);

public sealed record CampaignPlanningRequest(
    IReadOnlyCollection<ProductionOrder> ProductionOrders,
    IReadOnlyCollection<InventoryPosition> Inventory,
    CampaignPlanningPolicy Policy,
    string CampaignNumberPrefix = "CMP",
    IReadOnlyCollection<Resource>? Resources = null,
    IReadOnlyCollection<ResourceCapability>? ResourceCapabilities = null,
    IReadOnlyCollection<SteelGrade>? SteelGrades = null,
    IReadOnlyCollection<ExternalMaterialSupply>? ExternalMaterialSupplies = null);

public enum PlanningInventoryUse
{
    FinishedGoodsFulfilment = 1,
    IntermediateFeed = 2,
    ExternalIntermediateFeed = 3
}

public sealed record PlanningInventoryAllocation(
    Guid ProductionOrderId,
    InventoryStage Stage,
    string MaterialCode,
    string GradeCode,
    string CrossSectionCode,
    string? LocationCode,
    decimal QuantityMt,
    PlanningInventoryUse Use,
    string? SourceReference = null,
    DateTime? AvailableFromUtc = null);

public sealed record CampaignPlanningResult(
    IReadOnlyCollection<Campaign> Campaigns,
    IReadOnlyCollection<ProductionOrder> FullyCoveredByFinishedGoods,
    IReadOnlyDictionary<Guid, decimal> RollingRequirementsMt,
    IReadOnlyDictionary<Guid, decimal> FreshSteelRequirementsMt,
    IReadOnlyDictionary<Guid, decimal> IntermediateInventoryAllocatedMt,
    IReadOnlyCollection<PlanningInventoryAllocation> InventoryAllocations,
    IReadOnlyDictionary<Guid, decimal>? ExternalIntermediateAllocatedMt = null,
    IReadOnlyCollection<CampaignHeatAllocation>? HeatAllocations = null);

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
    ProductionStructurePlanningPolicy Policy,
    RoutePlanningInput? RoutePlanning = null,
    IReadOnlyCollection<SteelGrade>? SteelGrades = null,
    IReadOnlyCollection<MaterialSpecification>? MaterialSpecifications = null,
    IReadOnlyCollection<ExternalMaterialSupply>? ExternalMaterialSupplies = null);

public sealed record PlannedBilletSupply(
    Guid CampaignId,
    Guid CampaignHeatId,
    Guid CastSequenceId,
    Guid CasterResourceId,
    string GradeCode,
    string CrossSectionCode,
    decimal QuantityMt);

public sealed record PlannedStrandMaterialUnit(
    string PlanningKey,
    Guid CampaignId,
    Guid CampaignHeatId,
    Guid CastSequenceId,
    Guid CasterResourceId,
    int StrandNumber,
    int UnitSequence,
    string GradeCode,
    string CrossSectionCode,
    decimal QuantityMt,
    Guid AvailabilityTaskId);

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
    IReadOnlyCollection<PlanningIssue> Issues,
    IReadOnlyCollection<PlannedStrandMaterialUnit>? PlannedStrandMaterialUnits = null,
    IReadOnlyCollection<RouteOperationPlan>? RouteOperationPlans = null);

public interface IProductionStructurePlanningService
{
    ProductionStructurePlanningResult Build(ProductionStructurePlanningRequest request);
}

public enum FiniteScheduleTaskType
{
    Casting = 1,
    HotRolling = 2,
    ColdRolling = 3,
    Finishing = 4,
    Eaf = 5,
    Lrf = 6,
    Vd = 7,
    Reheating = 8,
    Tmt = 9,
    Cooling = 10,
    Cutting = 11,
    Bundling = 12,
    Coiling = 13
}

public enum TimeFenceZone { Frozen = 1, Slushy = 2, Liquid = 3 }

public sealed record FiniteScheduleResourceOption(
    Guid ResourceId,
    int DurationMinutes,
    int AssignmentPenalty = 0);

public sealed record FiniteScheduleDependencyResourcePair(
    Guid PredecessorResourceId,
    Guid SuccessorResourceId,
    int MinimumLagMinutes = 0,
    int? MaximumLagMinutes = null);

public sealed record FiniteScheduleDependency(
    Guid PredecessorTaskId,
    int MinimumLagMinutes = 0,
    int? MaximumLagMinutes = null,
    IReadOnlyCollection<FiniteScheduleDependencyResourcePair>? AllowedResourcePairs = null);

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
    IReadOnlyCollection<FiniteScheduleDependency> Dependencies,
    ProcessOperationType ProcessOperationType = ProcessOperationType.Unknown);

public sealed record FiniteScheduleStabilityConstraint(
    Guid TaskId,
    TimeFenceZone Zone,
    Guid BaselineResourceId,
    DateTime BaselineStartUtc,
    int MovementPenaltyPerMinute = 50,
    int ResourceChangePenalty = 5000);

public sealed record FiniteScheduleRequest(
    DateTime HorizonStartUtc,
    DateTime HorizonEndUtc,
    IReadOnlyCollection<FiniteScheduleTask> Tasks,
    IReadOnlyCollection<Resource> Resources,
    IReadOnlyCollection<ResourceCalendar> ResourceCalendars,
    IReadOnlyCollection<TransitionRule> TransitionRules,
    int MaxSolverSeconds = 20,
    IReadOnlyCollection<FiniteScheduleStabilityConstraint>? StabilityConstraints = null);

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
