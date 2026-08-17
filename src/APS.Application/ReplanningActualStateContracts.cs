namespace APS.Application;

public sealed record ReplanningActualState(
    IReadOnlyCollection<BaselinePlanOperation> BaselineOperations,
    IReadOnlyCollection<InventoryPosition> Inventory,
    IReadOnlyCollection<string> CompletedPlanningKeys,
    IReadOnlyCollection<string> RunningPlanningKeys);

public interface IReplanningActualStateProvider
{
    Task<ReplanningActualState> GetAsync(
        Guid baselinePlanVersionId,
        DateTime referenceTimeUtc,
        IReadOnlyCollection<BaselinePlanOperation> baselineOperations,
        CancellationToken cancellationToken = default);
}
