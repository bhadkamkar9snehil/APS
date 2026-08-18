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

public sealed record DemandSupplyRowView(
    Guid ProductionOrderId,
    string ProductionOrderNumber,
    DemandSourceType DemandSource,
    string? SalesOrderNumber,
    string? SalesOrderItemNumber,
    string? CustomerCode,
    string MaterialCode,
    string GradeCode,
    string FinalCrossSectionCode,
    string CasterSectionCode,
    string RouteCode,
    decimal PlannedQuantityMt,
    decimal RemainingQuantityMt,
    DateTime RequiredDate,
    int Priority,
    ProductionOrderStatus Status,
    decimal FinishedGoodsAllocatedMt,
    decimal RollingRequirementMt,
    decimal ExistingIntermediateAllocatedMt,
    decimal ExternalIntermediateAllocatedMt,
    decimal FreshSteelRequirementMt,
    decimal CoveredQuantityMt,
    decimal UncoveredQuantityMt,
    decimal? TargetStockMt,
    decimal? ProjectedAvailableStockMt,
    string? StockPolicyCode,
    string? RequirementFingerprint,
    string? QualityClassCode,
    SegregationPolicy SegregationPolicy,
    RequirementDisposition VdRequirement,
    RequirementDisposition ReheatRequirement,
    RequirementDisposition TmtRequirement,
    bool HotChargeAllowed);

public sealed record DemandSupplyView(
    PlanContextView Plan,
    decimal TotalRemainingQuantityMt,
    decimal FinishedGoodsAllocatedMt,
    decimal ExistingIntermediateAllocatedMt,
    decimal ExternalIntermediateAllocatedMt,
    decimal FreshSteelRequirementMt,
    decimal UncoveredQuantityMt,
    int MakeToOrderCount,
    int MakeToStockCount,
    IReadOnlyCollection<DemandSupplyRowView> Rows);

public sealed record CampaignAllocationView(
    Guid ProductionOrderId,
    string ProductionOrderNumber,
    DemandSourceType DemandSource,
    string? SalesOrderNumber,
    decimal PlannedQuantityMt,
    decimal ExistingIntermediateInventoryMt,
    decimal FreshSteelQuantityMt);

public sealed record CampaignGradeSequenceItemView(
    int SequenceNumber,
    string GradeCode,
    decimal PlannedQuantityMt);

public sealed record HeatAllocationView(
    Guid ProductionOrderId,
    string ProductionOrderNumber,
    string? SalesOrderNumber,
    decimal PlannedOutputQuantityMt,
    decimal PlannedInputQuantityMt);

public sealed record CampaignHeatView(
    Guid CampaignHeatId,
    int SequenceNumber,
    string GradeCode,
    decimal PlannedQuantityMt,
    decimal? MinimumFeasibleQuantityMt,
    decimal? TargetQuantityMt,
    decimal? MaximumFeasibleQuantityMt,
    Guid? PreferredSteelmakingResourceId,
    Guid? PreferredCasterResourceId,
    IReadOnlyCollection<HeatAllocationView> Allocations);

public sealed record CampaignView(
    Guid CampaignId,
    string CampaignNumber,
    string GradeSequenceClassCode,
    string CasterSectionCode,
    string RouteCode,
    decimal PlannedQuantityMt,
    decimal FreshSteelRequirementMt,
    decimal ExistingIntermediateInventoryMt,
    DateTime RequiredDate,
    CampaignStatus Status,
    IReadOnlyCollection<CampaignAllocationView> Allocations,
    IReadOnlyCollection<CampaignGradeSequenceItemView> GradeSequence,
    IReadOnlyCollection<CampaignHeatView> Heats);

public sealed record CampaignStudioView(
    PlanContextView Plan,
    int CampaignCount,
    int HeatCount,
    decimal PlannedRollingQuantityMt,
    decimal FreshSteelRequirementMt,
    decimal ExistingIntermediateQuantityMt,
    IReadOnlyCollection<CampaignView> Campaigns);

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

    Task<DemandSupplyView?> GetDemandSupplyAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default);

    Task<CampaignStudioView?> GetCampaignStudioAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default);
}
