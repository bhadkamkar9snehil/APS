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
    IReadOnlyCollection<PlanAssumptionChangeView>? AssumptionChanges = null);
