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
    decimal ExpectedCastingYieldPct = 100m,
    /// <summary>
    /// Relative weights the campaign optimizer trades efficiency against service with (#15). Null
    /// uses <see cref="CampaignObjectiveWeights.Default"/>.
    /// </summary>
    CampaignObjectiveWeights? ObjectiveWeights = null);

/// <summary>
/// Weights for the campaign objective (#15). Service risk sits in its own lexicographic tier, so no
/// combination of efficiency weights can buy a late customer order - efficiency only ever breaks ties
/// between compositions that serve demand equally well.
/// </summary>
public sealed record CampaignObjectiveWeights(
    /// <summary>Cost of a tonne finished one day after it was required. Dominates all efficiency terms.</summary>
    decimal ServiceRiskPerMtDay = 1000m,
    /// <summary>Cost of a tonne produced one day before it was required - inventory nobody asked for yet.</summary>
    decimal EarlyProductionPerMtDay = 1m,
    /// <summary>Cost of an additional campaign: setup, sequence break, lost continuity.</summary>
    decimal CampaignSetupCost = 40m,
    /// <summary>Cost of a tonne of unfilled capacity in the last heat of a campaign.</summary>
    decimal ResidualHeatPerMt = 4m,
    /// <summary>Cost of a campaign that falls short of the configured minimum campaign quantity, per tonne short.</summary>
    decimal BelowMinimumCampaignPerMt = 8m)
{
    public static CampaignObjectiveWeights Default { get; } = new();
}

/// <summary>
/// Why the optimizer chose the composition it did (#15). Each component is reported separately rather
/// than as one opaque number, so a planner can see whether a campaign exists for service reasons or
/// efficiency ones - and so a weight change has a visible, attributable effect.
/// </summary>
public sealed record CampaignObjectiveBreakdown(
    string StrategyCode,
    int CampaignCount,
    decimal ServiceRiskMtDays,
    decimal EarlyProductionMtDays,
    decimal ResidualHeatMt,
    decimal BelowMinimumShortfallMt,
    decimal TotalCost)
{
    /// <summary>
    /// Compositions are compared on service first and cost second, so an efficiency gain can never
    /// offset a service loss however the weights are set.
    /// </summary>
    public (decimal Service, decimal Cost) DominanceKey => (ServiceRiskMtDays, TotalCost);
}

/// <summary>
/// The chosen composition for one compatible group, with the alternatives it beat (#15).
/// </summary>
public sealed record CampaignCompositionDecision(
    string CompatibilityGroupKey,
    CampaignObjectiveBreakdown Selected,
    IReadOnlyCollection<CampaignObjectiveBreakdown> Considered);

public sealed record CampaignPlanningRequest(
    IReadOnlyCollection<ProductionOrder> ProductionOrders,
    IReadOnlyCollection<InventoryPosition> Inventory,
    CampaignPlanningPolicy Policy,
    string CampaignNumberPrefix = "CMP",
    IReadOnlyCollection<Resource>? Resources = null,
    IReadOnlyCollection<ResourceCapability>? ResourceCapabilities = null,
    IReadOnlyCollection<SteelGrade>? SteelGrades = null,
    IReadOnlyCollection<ExternalMaterialSupply>? ExternalMaterialSupplies = null,
    MaterialSupplyPlanningPolicy? MaterialSupplyPolicy = null,
    IReadOnlyCollection<MaterialSourcingRule>? MaterialSourcingRules = null,
    DateTime? PlanningReferenceTimeUtc = null,
    RoutePlanningInput? RoutePlanning = null,
    IReadOnlyCollection<CommittedMaterialSupply>? CommittedMaterialSupplies = null,
    IReadOnlyCollection<PlantFlowLink>? FlowLinks = null,
    IReadOnlyCollection<PrecomputedCampaignMaterialDemand>? PrecomputedMaterialDemand = null);

public enum PlanningInventoryUse
{
    FinishedGoodsFulfilment = 1,
    IntermediateFeed = 2,
    ExternalIntermediateFeed = 3,
    PlannedPurchaseFeed = 4,
    PlannedTransferFeed = 5,
    ManualPlannedFeed = 6,
    CommittedInternalProductionFeed = 7
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

public sealed record PlanningSupplyAllocation(
    Guid ProductionOrderId,
    MaterialSupplyActionType ActionType,
    decimal QuantityMt,
    DateTime RequiredReceiptUtc,
    DateTime? ExpectedReceiptUtc,
    string? SupplyReference = null,
    string? SupplierCode = null,
    string? SourceLocationCode = null,
    string? DestinationLocationCode = null,
    bool IsFirm = false,
    string? RuleCode = null,
    decimal? PlannedReceiptQuantityMt = null,
    decimal ProjectedExcessQuantityMt = 0m,
    int SelectionPenalty = 0);

public sealed record PlanningSupplyAlternative(
    Guid ProductionOrderId,
    MaterialSupplyActionType ActionType,
    bool IsAllowed,
    bool IsFeasible,
    bool IsSelected,
    decimal RequiredQuantityMt,
    decimal PlannedReceiptQuantityMt,
    decimal ProjectedExcessQuantityMt,
    DateTime RequiredReceiptUtc,
    DateTime? ExpectedReceiptUtc,
    int Penalty,
    string? RuleCode,
    string? SupplierCode = null,
    string? SourceLocationCode = null,
    string? DestinationLocationCode = null,
    string? RejectionReason = null);

public sealed record CampaignPlanningResult(
    IReadOnlyCollection<Campaign> Campaigns,
    IReadOnlyCollection<ProductionOrder> FullyCoveredByFinishedGoods,
    IReadOnlyDictionary<Guid, decimal> RollingRequirementsMt,
    IReadOnlyDictionary<Guid, decimal> FreshSteelRequirementsMt,
    IReadOnlyDictionary<Guid, decimal> IntermediateInventoryAllocatedMt,
    IReadOnlyCollection<PlanningInventoryAllocation> InventoryAllocations,
    IReadOnlyDictionary<Guid, decimal>? ExternalIntermediateAllocatedMt = null,
    IReadOnlyCollection<CampaignHeatAllocation>? HeatAllocations = null,
    IReadOnlyCollection<PlanningSupplyAllocation>? PlannedSupplyAllocations = null,
    IReadOnlyDictionary<Guid, decimal>? PlannedPurchaseAllocatedMt = null,
    IReadOnlyDictionary<Guid, decimal>? PlannedTransferAllocatedMt = null,
    IReadOnlyCollection<PlanningSupplyAlternative>? SourcingAlternatives = null,
    /// <summary>
    /// Objective breakdown for each compatible group's chosen campaign composition, and the
    /// alternatives it was chosen over (#15).
    /// </summary>
    IReadOnlyCollection<CampaignCompositionDecision>? CompositionDecisions = null);

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
    int AssignmentPenalty = 0,
    string? EligibilityBasisCode = null,
    /// <summary>
    /// How much of a <see cref="ResourceSchedulingMode.Cumulative"/> resource's capacity this task
    /// occupies while it runs, in that resource's <see cref="ResourceCapacityBasis"/>. Null lets the
    /// optimizer derive it from the resource's basis and the task quantity, which is what every
    /// projector relies on today; supply it explicitly only when the projector knows better.
    /// Ignored for disjunctive resources, which are occupied wholly or not at all.
    /// </summary>
    decimal? CapacityDemand = null);

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

/// <summary>
/// Quantity/date service obligation attached to a physical scheduling task. Multiple POs may share
/// one heat/rolling/finishing task without collapsing their individual required dates into one date.
/// </summary>
public sealed record FiniteScheduleServiceObligation(
    Guid TaskId,
    Guid ProductionOrderId,
    decimal QuantityMt,
    DateTime DueUtc,
    int Priority);

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
    IReadOnlyCollection<FiniteScheduleStabilityConstraint>? StabilityConstraints = null,
    IReadOnlyCollection<SteelGrade>? SteelGrades = null,
    IReadOnlyCollection<ScheduledMaterialEvent>? MaterialEvents = null,
    IReadOnlyCollection<FiniteScheduleServiceObligation>? ServiceObligations = null,
    /// <summary>
    /// Groups of tasks that must all resolve to the same physical resource (#16) - e.g. every heat in one
    /// cast sequence, since continuous casting requires them to share one physical CCM even though the
    /// solver is free to choose which one. Each group is an ordered list of TaskIds sharing a common
    /// eligible-resource set; the solver links their per-resource presence variables together.
    /// </summary>
    IReadOnlyCollection<IReadOnlyCollection<Guid>>? LinkedResourceTaskGroups = null);

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
