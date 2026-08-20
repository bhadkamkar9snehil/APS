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
    /// <summary>
    /// Sum of the resource's operation durations. This is work content, not occupancy: on a
    /// cumulative resource concurrent blocks are counted once each, so it can exceed the time the
    /// unit was actually busy. Use <see cref="OccupiedHours"/> to express utilization (#35).
    /// </summary>
    double ScheduledHours,
    DateTime? FirstStartUtc,
    DateTime? LastEndUtc,
    ResourceSchedulingMode SchedulingMode = ResourceSchedulingMode.Disjunctive,
    /// <summary>Wall-clock hours the resource was running anything, counting overlap once.</summary>
    double OccupiedHours = 0d,
    /// <summary>Most operations held at once - compare against <see cref="NominalConcurrentCapacity"/>.</summary>
    int PeakConcurrentOperations = 0,
    ResourceCapacityBasis CapacityBasis = ResourceCapacityBasis.NotApplicable,
    decimal? NominalConcurrentCapacity = null);

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

public sealed record SalesOrderDemandCoverageView(
    string MaterialCode,
    string GradeCode,
    string CrossSectionCode,
    string? LocationCode,
    DateTime? AvailableFromUtc,
    MaterialQualityStatus QualityStatus,
    decimal QuantityMt);

public sealed record SalesOrderDemandRowView(
    Guid SalesOrderId,
    string SalesOrderNumber,
    string SalesOrderItemNumber,
    string? CustomerCode,
    string? CustomerGroupCode,
    string MaterialCode,
    string GradeCode,
    string FinalCrossSectionCode,
    decimal OpenDemandQuantityMt,
    decimal FinishedGoodsCoveredQuantityMt,
    decimal ManufacturingRequirementQuantityMt,
    Guid? ProductionOrderId,
    string? ProductionOrderNumber,
    DateTime CustomerRequiredDate,
    DateTime? ConfirmedDeliveryDate,
    DateTime ProductionRequiredByDate,
    int Priority,
    DemandReconciliationDisposition Disposition,
    bool PlannerAttentionRequired,
    string? ReasonCode,
    IReadOnlyCollection<SalesOrderDemandCoverageView> FinishedGoodsCoverage);

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
    IReadOnlyCollection<DemandSupplyRowView> Rows,
    IReadOnlyCollection<SalesOrderDemandRowView>? SalesOrders = null,
    decimal TotalSalesOrderOpenDemandMt = 0m,
    decimal SalesOrderFinishedGoodsCoveredMt = 0m,
    decimal SalesOrderManufacturingRequirementMt = 0m,
    int PlannerAttentionCount = 0);

public sealed record CampaignAllocationView(
    Guid ProductionOrderId,
    string ProductionOrderNumber,
    DemandSourceType DemandSource,
    string? SalesOrderNumber,
    decimal PlannedQuantityMt,
    decimal ExistingIntermediateInventoryMt,
    decimal FreshSteelQuantityMt,
    DateTime? RequiredDate = null,
    int Priority = 0,
    string? SalesOrderItemNumber = null);

public sealed record CampaignGradeSequenceItemView(
    int SequenceNumber,
    string GradeCode,
    decimal PlannedQuantityMt);

public sealed record HeatAllocationView(
    Guid ProductionOrderId,
    string ProductionOrderNumber,
    string? SalesOrderNumber,
    decimal PlannedOutputQuantityMt,
    decimal PlannedInputQuantityMt,
    DateTime? RequiredDate = null,
    int Priority = 0,
    string? SalesOrderItemNumber = null);

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

    Task<SteelmakingCastingWorkspaceView?> GetSteelmakingCastingAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default);

    Task<FiniteScheduleWorkspaceView?> GetFiniteScheduleAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default);

    Task<RollingFinishingWorkspaceView?> GetRollingFinishingAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default);

    Task<WorkOrdersWorkspaceView?> GetWorkOrdersAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default);

    Task<MaterialFlowWorkspaceView?> GetMaterialFlowAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default);

    Task<PlanComparisonWorkspaceView?> GetPlanComparisonAsync(
        Guid baselinePlanVersionId,
        Guid newPlanVersionId,
        CancellationToken cancellationToken = default);
}
