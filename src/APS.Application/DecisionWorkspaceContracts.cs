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

public sealed record PlanComparisonWorkspaceView(
    PlanContextView Baseline,
    PlanContextView NewPlan,
    int AddedOperations,
    int RemovedOperations,
    int MovedOperations,
    int ResourceChangedOperations,
    int UnchangedOperations,
    int MaximumStartMovementMinutes,
    IReadOnlyCollection<PlanOperationChangeView> Changes);
