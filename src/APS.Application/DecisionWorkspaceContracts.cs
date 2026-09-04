using APS.Domain;

namespace APS.Application;

public sealed record PlanOperationChangeView(
    string PlanningKey,
    FiniteScheduleTaskType TaskType,
    PlanOperationChangeType ChangeType,
    string? BaselineResourceCode,
    string? NewResourceCode,
    DateTime? BaselineStartUtc,
    DateTime? NewStartUtc,
    DateTime? BaselineEndUtc,
    DateTime? NewEndUtc,
    int StartMovementMinutes);

/// <summary>A planner-visible change in the assumptions or controls that produced two Plan Versions.</summary>
public sealed record PlanAssumptionChangeView(
    string Area,
    string Setting,
    string BaselineValue,
    string NewValue);

/// <summary>Compact schedule footprint for one side of a what-if comparison.</summary>
public sealed record PlanScenarioSummaryView(
    int ScheduledOperations,
    int ResourceCount,
    double ScheduledHours,
    DateTime? FirstStartUtc,
    DateTime? LastEndUtc,
    double SpanHours,
    long? ObjectiveValue);

/// <summary>
/// Persisted demand/supply and production-structure footprint for one Plan Version. These values are
/// copied from immutable plan snapshots; the comparison UI does not recalculate demand or sourcing.
/// </summary>
public sealed record PlanScenarioDemandSummaryView(
    int ProductionOrders,
    int MakeToOrderCount,
    int MakeToStockCount,
    decimal RemainingDemandMt,
    decimal FinishedGoodsAllocatedMt,
    decimal ExistingIntermediateAllocatedMt,
    decimal ExternalIntermediateAllocatedMt,
    decimal FreshSteelRequirementMt,
    int CampaignCount,
    int HeatCount)
{
    public decimal IntermediateAllocatedMt => ExistingIntermediateAllocatedMt + ExternalIntermediateAllocatedMt;
}

/// <summary>One order's immutable service policy and derived manufacturing window in a Plan Version.</summary>
public sealed record PlanOrderServiceView(
    Guid SalesOrderId,
    string SalesOrderNumber,
    string SalesOrderItemNumber,
    string? CustomerCode,
    DateTime CustomerRequiredDate,
    DateTime? ConfirmedDeliveryDate,
    DateTime ProductionRequiredByDate,
    ServiceCommitmentClass ServiceCommitment,
    DateTime? EarliestAcceptableDeliveryDate,
    DateTime? LatestAcceptableDeliveryDate,
    DateTime? ProductionEarliestAcceptableDate,
    DateTime? ProductionLatestAcceptableDate,
    int Priority)
{
    public DateTime TargetDeliveryDate => ConfirmedDeliveryDate ?? CustomerRequiredDate;
    public DateTime EffectiveProductionDeadline => ProductionLatestAcceptableDate ?? ProductionRequiredByDate;
}

/// <summary>
/// Side-by-side service evidence for a Sales Order. Either side may be null when demand was added or
/// removed between Plan Versions; no UI inference is required.
/// </summary>
public sealed record PlanOrderServiceComparisonView(
    Guid SalesOrderId,
    PlanOrderServiceView? Baseline,
    PlanOrderServiceView? NewPlan);

/// <summary>One immutable persisted operation projected for side-by-side scenario visualization.</summary>
public sealed record PlanScenarioOperationView(
    string PlanningKey,
    FiniteScheduleTaskType TaskType,
    string ResourceCode,
    DateTime StartUtc,
    DateTime EndUtc,
    PlanOperationChangeType ChangeType);

/// <summary>Per-resource work-content comparison. Occupancy is intentionally not inferred here.</summary>
public sealed record PlanResourceLoadComparisonView(
    string ResourceCode,
    int BaselineOperations,
    int NewOperations,
    double BaselineScheduledHours,
    double NewScheduledHours)
{
    public double ScheduledHoursDelta => NewScheduledHours - BaselineScheduledHours;
}

public sealed record PlanComparisonWorkspaceView(
    PlanContextView Baseline,
    PlanContextView NewPlan,
    int AddedOperations,
    int RemovedOperations,
    int MovedOperations,
    int ResourceChangedOperations,
    int UnchangedOperations,
    int MaximumStartMovementMinutes,
    IReadOnlyCollection<PlanOperationChangeView> Changes,
    IReadOnlyCollection<PlanAssumptionChangeView>? AssumptionChanges = null,
    PlanScenarioSummaryView? BaselineSummary = null,
    PlanScenarioSummaryView? NewPlanSummary = null,
    IReadOnlyCollection<PlanResourceLoadComparisonView>? ResourceLoads = null,
    IReadOnlyCollection<PlanScenarioOperationView>? BaselineOperations = null,
    IReadOnlyCollection<PlanScenarioOperationView>? NewPlanOperations = null,
    PlanScenarioDemandSummaryView? BaselineDemand = null,
    PlanScenarioDemandSummaryView? NewPlanDemand = null,
    IReadOnlyCollection<PlanOrderServiceComparisonView>? OrderService = null);