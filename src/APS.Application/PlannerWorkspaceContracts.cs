using APS.Domain;

namespace APS.Application;

public enum PlannerEntityType
{
    SalesOrder = 1,
    ProductionOrder = 2,
    Campaign = 3,
    Heat = 4,
    CastSequence = 5,
    Operation = 6,
    Resource = 7,
    MaterialSupply = 8,
    MaterialLot = 9,
    WorkOrder = 10,
    Diagnostic = 11,
    PlanVersion = 12
}

public sealed record PlannerEntityRef(
    PlannerEntityType EntityType,
    Guid EntityId,
    string DisplayCode);

public sealed record PlanContextView(
    Guid PlanVersionId,
    string VersionNumber,
    Guid? ParentPlanVersionId,
    PlanVersionStatus Status,
    PlanTriggerType Trigger,
    DateTime CreatedOnUtc,
    DateTime ReferenceTimeUtc,
    DateTime HorizonStartUtc,
    DateTime HorizonEndUtc,
    string? SolverStatus,
    long? ObjectiveValue,
    bool IsActive,
    bool IsReleased,
    string? Reason);

public sealed record PlanVersionListItemView(
    Guid PlanVersionId,
    string VersionNumber,
    Guid? ParentPlanVersionId,
    PlanVersionStatus Status,
    PlanTriggerType Trigger,
    DateTime CreatedOnUtc,
    DateTime ReferenceTimeUtc,
    DateTime HorizonStartUtc,
    DateTime HorizonEndUtc,
    string? SolverStatus,
    bool IsActive,
    bool IsReleased,
    string? Reason);

public sealed record PlanFootprintView(
    int ScheduledOperationCount,
    int PhysicalResourceCount,
    DateTime? FirstOperationStartUtc,
    DateTime? LastOperationEndUtc,
    int InventoryAllocationCount,
    decimal InventoryAllocatedQuantityMt,
    int PlannedMaterialUnitCount,
    decimal PlannedMaterialUnitQuantityMt,
    int WorkOrderCount,
    int ReleasedWorkOrderCount,
    int RunningWorkOrderCount,
    int HeldWorkOrderCount,
    int CompletedWorkOrderCount);

public sealed record ResourcePressureView(
    Guid ResourceId,
    string ResourceCode,
    string ResourceName,
    ProcessUnitType ProcessUnitType,
    ResourceOperatingState OperatingState,
    int ScheduledOperationCount,
    double ScheduledHours,
    DateTime? FirstStartUtc,
    DateTime? LastEndUtc);

public sealed record PlanMaterialSummaryView(
    InventoryStage Stage,
    decimal AllocatedQuantityMt,
    int AllocationCount);

public sealed record ControlTowerView(
    PlanContextView Plan,
    PlanFootprintView Footprint,
    IReadOnlyCollection<ResourcePressureView> Resources,
    IReadOnlyCollection<PlanMaterialSummaryView> MaterialAllocations,
    DateTime GeneratedOnUtc);

public interface IPlannerWorkspaceQueryService
{
    Task<PlanContextView?> GetCurrentPlanAsync(CancellationToken cancellationToken = default);

    Task<PlanContextView?> GetPlanContextAsync(
        Guid planVersionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PlanVersionListItemView>> GetRecentPlanVersionsAsync(
        int take = 20,
        CancellationToken cancellationToken = default);

    Task<ControlTowerView?> GetControlTowerAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default);
}
