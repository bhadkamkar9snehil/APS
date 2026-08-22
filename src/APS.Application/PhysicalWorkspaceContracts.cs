using APS.Domain;

namespace APS.Application;

public sealed record ScheduledProcessOperationView(
    Guid OperationSnapshotId,
    string PlanningKey,
    Guid SourceEntityId,
    ProcessOperationType ProcessOperationType,
    Guid ResourceId,
    string ResourceCode,
    string ResourceName,
    ProcessUnitType ProcessUnitType,
    ResourceOperatingState ResourceOperatingState,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal QuantityMt,
    string GradeCode,
    string CrossSectionCode);

public sealed record StrandOutputView(
    Guid MaterialUnitSnapshotId,
    string PlanningKey,
    int StrandNumber,
    int UnitSequence,
    string GradeCode,
    string CrossSectionCode,
    decimal QuantityMt,
    DateTime? AvailableOnUtc);

public sealed record HeatProcessView(
    Guid CampaignId,
    string CampaignNumber,
    Guid CampaignHeatId,
    int HeatSequenceNumber,
    string GradeCode,
    decimal PlannedQuantityMt,
    Guid? CastSequenceId,
    int? CastSequenceNumber,
    Guid? CasterResourceId,
    string? CasterResourceCode,
    int? TundishNumber,
    IReadOnlyCollection<ScheduledProcessOperationView> Operations,
    IReadOnlyCollection<StrandOutputView> StrandOutputs);

public sealed record SteelmakingCastingWorkspaceView(
    PlanContextView Plan,
    int HeatCount,
    int CastSequenceCount,
    decimal PlannedHeatInputMt,
    decimal PlannedCastOutputMt,
    IReadOnlyCollection<HeatProcessView> Heats);

public sealed record ScheduleResourceLaneView(
    Guid ResourceId,
    string ResourceCode,
    string ResourceName,
    ProcessUnitType ProcessUnitType,
    ResourceOperatingState OperatingState,
    /// <summary>
    /// Sum of this lane's operation durations - work content. On a cumulative resource this counts
    /// concurrent blocks separately, so it is not the time the unit was busy (#35).
    /// </summary>
    double ScheduledHours,
    IReadOnlyCollection<ScheduledProcessOperationView> Operations,
    ResourceSchedulingMode SchedulingMode = ResourceSchedulingMode.Disjunctive,
    /// <summary>Wall-clock hours this lane was running anything, counting overlap once.</summary>
    double OccupiedHours = 0d,
    int PeakConcurrentOperations = 0,
    decimal? NominalConcurrentCapacity = null,
    Guid? PlantId = null,
    string? PlantCode = null,
    string? PlantName = null,
    Guid? AreaId = null,
    string? AreaCode = null,
    string? AreaName = null,
    Guid? ProcessStageId = null,
    string? ProcessStageCode = null,
    string? ProcessStageName = null,
    int DisplayOrder = int.MaxValue);

public sealed record FiniteScheduleWorkspaceView(
    PlanContextView Plan,
    DateTime ScheduleStartUtc,
    DateTime ScheduleEndUtc,
    int OperationCount,
    int ResourceCount,
    IReadOnlyCollection<ScheduleResourceLaneView> ResourceLanes);

public sealed record MaterialFlowEventView(
    MaterialBalanceEventType EventType,
    decimal QuantityDeltaMt,
    decimal RunningBalanceMt,
    DateTime EffectiveAtUtc,
    string? SupplyReference,
    string? Explanation);

public sealed record MaterialFlowPoolView(
    string MaterialPoolKey,
    string GradeCode,
    string CrossSectionCode,
    string? MaterialSpecificationCode,
    string? LocationCode,
    decimal ClosingBalanceMt,
    IReadOnlyCollection<MaterialFlowEventView> Events);

public sealed record MaterialFlowReservationView(
    Guid ProductionOrderId,
    string? ProductionOrderNumber,
    string GradeCode,
    string CrossSectionCode,
    InventoryStage InventoryStage,
    decimal QuantityMt,
    DateTime AvailableFromUtc,
    MaterialReservationStatus Status);

public sealed record MaterialFlowWorkspaceView(
    PlanContextView Plan,
    IReadOnlyCollection<MaterialFlowPoolView> Pools,
    IReadOnlyCollection<MaterialFlowReservationView> Reservations);
