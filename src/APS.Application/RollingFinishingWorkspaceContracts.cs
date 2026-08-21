using APS.Domain;

namespace APS.Application;

public sealed record DownstreamRouteOperationView(
    Guid RouteOperationPlanId,
    string RouteCode,
    ProcessOperationType ProcessOperationType,
    int SequenceNumber,
    string GradeCode,
    string InputCrossSectionCode,
    string OutputCrossSectionCode,
    decimal PlannedQuantityMt,
    TimeSpan MinimumQueueTime,
    TimeSpan? MaximumQueueTime,
    bool IsInventoryDecouplingPoint,
    IReadOnlyCollection<ScheduledProcessOperationView> ScheduledOperations);

public sealed record RollingAllocationView(
    Guid ProductionOrderId,
    string ProductionOrderNumber,
    string? SalesOrderNumber,
    DemandSourceType DemandSource,
    decimal PlannedQuantityMt,
    decimal ExistingIntermediateInventoryMt,
    decimal FreshSteelQuantityMt,
    BilletSupplyTraceView? SupplyTrace = null);

/// <summary>
/// Answers "which billet/heat supplies this rolling requirement, is it hot or cold, does it need
/// RHF, and why is it short" from the same persisted MaterialRequirement/MaterialSupplyReservation
/// facts the planning kernel already wrote, without recomputing coverage arithmetic in the UI.
/// </summary>
public sealed record BilletSupplyTraceView(
    MaterialRequirementStatus? Status,
    decimal ShortfallQuantityMt,
    decimal LateSupplyQuantityMt,
    string? Explanation,
    bool RequiresReheat,
    IReadOnlyCollection<BilletSupplyAllocationView> Sources);

public sealed record BilletSupplyAllocationView(
    string? SupplyReference,
    InventoryStage InventoryStage,
    BilletSupplySourceType? ExternalSourceType,
    decimal QuantityMt,
    DateTime AvailableFromUtc,
    string? LocationCode,
    MaterialReservationStatus Status);

public sealed record PlannedPackagingUnitView(
    Guid PlannedPackagingUnitId,
    PackagingUnitType PackagingUnitType,
    int SequenceNumber,
    decimal PlannedWeightMt,
    int? PlannedPieceCount,
    decimal? CutLengthM,
    string? PlannedIdentifier);

public sealed record RollingPlanView(
    Guid RollingPlanId,
    int SequenceNumber,
    string GradeCode,
    string RouteCode,
    string InputCrossSectionCode,
    string OutputCrossSectionCode,
    decimal PlannedQuantityMt,
    decimal ExistingIntermediateInventoryMt,
    decimal FreshSteelQuantityMt,
    Guid? RollingMillResourceId,
    string? RollingMillResourceCode,
    IReadOnlyCollection<ScheduledProcessOperationView> FeedAndRollingOperations,
    IReadOnlyCollection<DownstreamRouteOperationView> DownstreamOperations,
    IReadOnlyCollection<RollingAllocationView> Allocations,
    IReadOnlyCollection<PlannedPackagingUnitView> PackagingUnits);

public sealed record RollingFinishingWorkspaceView(
    PlanContextView Plan,
    int RollingPlanCount,
    decimal PlannedRollingQuantityMt,
    decimal ExistingIntermediateQuantityMt,
    decimal FreshSteelQuantityMt,
    int PlannedBundleCount,
    int PlannedCoilCount,
    IReadOnlyCollection<RollingPlanView> RollingPlans);