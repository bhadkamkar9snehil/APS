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
    double ScheduledHours,
    IReadOnlyCollection<ScheduledProcessOperationView> Operations);

public sealed record FiniteScheduleWorkspaceView(
    PlanContextView Plan,
    DateTime ScheduleStartUtc,
    DateTime ScheduleEndUtc,
    int OperationCount,
    int ResourceCount,
    IReadOnlyCollection<ScheduleResourceLaneView> ResourceLanes);
