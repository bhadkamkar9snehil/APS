namespace APS.Application;

public enum PlanOperationChangeType
{
    Added = 1,
    Removed = 2,
    Moved = 3,
    ResourceChanged = 4,
    MovedAndResourceChanged = 5,
    Unchanged = 6
}

public sealed record PlanOperationDifference(
    string PlanningKey,
    FiniteScheduleTaskType TaskType,
    PlanOperationChangeType ChangeType,
    Guid? BaselineResourceId,
    Guid? NewResourceId,
    DateTime? BaselineStartUtc,
    DateTime? NewStartUtc,
    DateTime? BaselineEndUtc,
    DateTime? NewEndUtc,
    int StartMovementMinutes);

public sealed record PlanVersionDifference(
    Guid BaselinePlanVersionId,
    Guid NewPlanVersionId,
    int AddedCount,
    int RemovedCount,
    int MovedCount,
    int ResourceChangedCount,
    int UnchangedCount,
    int MaximumStartMovementMinutes,
    IReadOnlyCollection<PlanOperationDifference> Operations);

public interface IPlanComparisonService
{
    Task<PlanVersionDifference> CompareAsync(
        Guid baselinePlanVersionId,
        Guid newPlanVersionId,
        CancellationToken cancellationToken = default);
}
