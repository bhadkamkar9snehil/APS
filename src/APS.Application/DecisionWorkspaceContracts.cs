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
    IReadOnlyCollection<PlanScenarioOperationView>? NewPlanOperations = null);