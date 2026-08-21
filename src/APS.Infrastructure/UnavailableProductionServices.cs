using APS.Application;
using APS.Domain;

namespace APS.Infrastructure;

/// <summary>
/// Every DB-backed production service registered by AddApsInfrastructure needs an "unavailable"
/// counterpart when no APS SQL database is configured - otherwise Blazor's DI container throws
/// InvalidOperationException at component-construction time (before any page code ever runs) for
/// any page that injects one of these directly, which crashes the whole app rather than showing
/// the page's own "workspace unavailable" state. This mirrors UnavailablePlannerWorkspaceQueryService.
/// </summary>
internal static class ProductionUnavailable
{
    public static InvalidOperationException Exception() => new(
        "This feature requires the APS SQL database, which is not configured. " +
        "Configure the production database connection string to use it.");
}

public sealed class UnavailableMasterDataAdminService : IMasterDataAdminService
{
    public Task<T> CreateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : Entity =>
        throw ProductionUnavailable.Exception();

    public Task<T> UpdateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : Entity =>
        throw ProductionUnavailable.Exception();

    public Task DeleteAsync<T>(Guid id, CancellationToken cancellationToken = default) where T : Entity =>
        throw ProductionUnavailable.Exception();
}

public sealed class UnavailableTraceabilityService : ITraceabilityService
{
    public Task<WorkOrderTrace?> GetWorkOrderTraceAsync(Guid workOrderId, CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();

    public Task<MaterialLotTrace?> GetMaterialLotTraceAsync(Guid materialLotId, CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();
}

public sealed class UnavailableWorkOrderExecutionService : IWorkOrderExecutionService
{
    public Task<WorkOrderExecutionSnapshot> ApplyAsync(WorkOrderExecutionUpdate update, CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();
}

public sealed class UnavailableHeatExecutionService : IHeatExecutionService
{
    public Task<HeatExecutionSnapshot> ApplyAsync(HeatExecutionUpdate update, CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();
}

public sealed class UnavailableOperationExecutionService : IOperationExecutionService
{
    public Task<OperationExecutionSnapshot> ApplyAsync(OperationExecutionUpdate update, CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();

    public Task<IReadOnlyCollection<OperationExecutionSnapshot>> RefreshCommitmentsAsync(
        Guid planVersionId, DateTime referenceTimeUtc, CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();
}

public sealed class UnavailableInventorySnapshotProvider : IInventorySnapshotProvider
{
    public Task<IReadOnlyCollection<InventoryPosition>> GetInventoryAsync(CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();
}

public sealed class UnavailableReplanningActualStateProvider : IReplanningActualStateProvider
{
    public Task<ReplanningActualState> GetAsync(
        Guid baselinePlanVersionId, DateTime referenceTimeUtc,
        IReadOnlyCollection<BaselinePlanOperation> baselineOperations, CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();
}

public sealed class UnavailablePlanVersionRepository : IPlanVersionRepository
{
    public Task<PlanVersionSnapshot> SaveAsync(PersistPlanningRunRequest request, CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();

    public Task<PlanVersionSnapshot?> GetAsync(Guid planVersionId, CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();

    public Task<IReadOnlyCollection<BaselinePlanOperation>> GetBaselineOperationsAsync(Guid planVersionId, CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();

    public Task<IReadOnlyCollection<BaselineCampaignAllocation>> GetBaselineCampaignAllocationsAsync(
        Guid planVersionId,
        CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();
}

public sealed class UnavailablePlanReleaseRepository : IPlanReleaseRepository
{
    public Task<PlanRelease> PersistAsync(PlanRelease release, CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();
}

public sealed class UnavailablePersistedPlanReleaseService : IPersistedPlanReleaseService
{
    public Task<PlanRelease> ReleaseAsync(Guid planVersionId, CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();
}

public sealed class UnavailablePlanComparisonService : IPlanComparisonService
{
    public Task<PlanVersionDifference> CompareAsync(Guid baselinePlanVersionId, Guid newPlanVersionId, CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();
}

public sealed class UnavailablePlanningMasterDataProvider : IPlanningMasterDataProvider
{
    public Task<PlanningMasterDataSnapshot> GetAsync(CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();
}

public sealed class UnavailableProductionDemandOrchestrationService : IProductionDemandOrchestrationService
{
    public Task<SalesOrderReconciliationResult> ReconcileSalesOrdersAsync(
        IReadOnlyCollection<SalesOrderDemandInput> salesOrders, CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();

    public Task<DemandOrchestrationResult> PrepareAsync(
        PlanningDemandSelection selection, IReadOnlyCollection<InventoryPosition> inventory,
        PlanningMasterDataSnapshot masters, DateTime referenceTimeUtc, DateTime horizonEndUtc,
        CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();

    public Task<IReadOnlyCollection<DemandOrchestrationItem>> GetCurrentMtoDemandAsync(CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();
}

public sealed class UnavailablePlanningLifecycleService : IPlanningLifecycleService
{
    public Task<PersistedPlanningRunResult> CalculateAsync(PlanningCalculationRequest request, CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();

    public Task<PersistedPlanningRunResult> ReplanAsync(
        Guid baselinePlanVersionId, PlanningRecalculationRequest request, CancellationToken cancellationToken = default) =>
        throw ProductionUnavailable.Exception();
}
