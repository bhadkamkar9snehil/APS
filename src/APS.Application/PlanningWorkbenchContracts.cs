using APS.Domain;

namespace APS.Application;

public enum PlanningWorkbenchExceptionSeverity
{
    Information = 1,
    Warning = 2,
    Critical = 3
}

public enum PlanningWorkbenchExceptionKind
{
    UncoveredDemand = 1,
    DemandAttention = 2,
    ResourceUnavailable = 3,
    MaterialShortage = 4,
    InfeasiblePlan = 5,
    LateDemand = 6
}

public sealed record PlanningWorkbenchException(
    string Code,
    PlanningWorkbenchExceptionKind Kind,
    PlanningWorkbenchExceptionSeverity Severity,
    string Title,
    string Impact,
    PlannerEntityRef? Entity = null);

public sealed record PlanningQueueView(
    int TotalDemand,
    int UnscheduledDemand,
    int LateDemand,
    int Campaigns,
    int MaterialPools,
    int CriticalExceptions,
    int WarningExceptions);

public sealed record PlanningOperationResourceOptionView(
    Guid ResourceId,
    string ResourceCode,
    string ResourceName,
    int DurationMinutes,
    int AssignmentPenalty,
    bool WasSelected,
    string? EligibilityBasisCode);

public sealed record PlanningOperationWorkbenchDetail(
    Guid OperationSnapshotId,
    string PlanningKey,
    Guid SourceEntityId,
    OperationAssignmentCommitmentState CommitmentState,
    OperationExecutionStatus ExecutionStatus,
    DateTime? ActualStartUtc,
    DateTime? ActualEndUtc,
    decimal ActualQuantityMt,
    IReadOnlyCollection<string> PredecessorPlanningKeys,
    IReadOnlyCollection<PlanningOperationResourceOptionView> ResourceOptions,
    string? CampaignNumber,
    int? HeatSequenceNumber,
    IReadOnlyCollection<string> ProductionOrderNumbers);

public enum PlanningDependencyType
{
    FinishStart = 1
}

public enum PlanningDependencyCategory
{
    Routing = 1
}

public sealed record PlanningDependencyLinkView(
    Guid PredecessorOperationSnapshotId,
    string PredecessorPlanningKey,
    Guid SuccessorOperationSnapshotId,
    string SuccessorPlanningKey,
    PlanningDependencyType Type,
    PlanningDependencyCategory Category,
    int? MinimumLagMinutes,
    int CurrentLagMinutes);

public sealed record PlanningResourceCalendarIntervalView(
    Guid ResourceId,
    DateTime StartUtc,
    DateTime EndUtc,
    bool IsAvailable,
    decimal? CapacityFactorPct,
    string? ReasonCode,
    string Source);

public sealed record PlanningBaselinePlacementView(
    Guid BaselinePlanVersionId,
    Guid OperationSnapshotId,
    string PlanningKey,
    Guid ResourceId,
    string ResourceCode,
    string ResourceName,
    ProcessUnitType ProcessUnitType,
    ResourceOperatingState OperatingState,
    ResourceSchedulingMode SchedulingMode,
    DateTime StartUtc,
    DateTime EndUtc,
    ProcessOperationType ProcessOperationType,
    string GradeCode,
    string CrossSectionCode,
    Guid? PlantId,
    string? PlantCode,
    string? PlantName,
    Guid? AreaId,
    string? AreaCode,
    string? AreaName,
    Guid? ProcessStageId,
    string? ProcessStageCode,
    string? ProcessStageName,
    int DisplayOrder);

public enum PlanningCapacityBasis
{
    MachineTime = 1,
    Slots = 2,
    MassEquivalentMt = 3,
    Positions = 4
}

public sealed record PlanningCapacityBucketView(
    Guid ResourceId,
    DateTime StartUtc,
    DateTime EndUtc,
    double AvailableMinutes,
    double ProcessingMinutes,
    double UnavailableMinutes,
    decimal OccupancyRatio,
    PlanningCapacityBasis Basis,
    ResourceSchedulingMode SchedulingMode);

public sealed record PlanningWorkbenchView(
    PlanContextView Plan,
    PlanContextView? Baseline,
    DemandSupplyView Demand,
    CampaignStudioView Campaigns,
    FiniteScheduleWorkspaceView Schedule,
    MaterialFlowWorkspaceView Material,
    PlanComparisonWorkspaceView? Comparison,
    PlanningQueueView Queue,
    IReadOnlyCollection<PlanningWorkbenchException> Exceptions,
    IReadOnlyCollection<PlanningOperationWorkbenchDetail> OperationDetails,
    IReadOnlyCollection<PlanningDependencyLinkView> DependencyLinks,
    IReadOnlyCollection<PlanningResourceCalendarIntervalView> ResourceCalendarIntervals,
    IReadOnlyCollection<PlanningBaselinePlacementView> BaselinePlacements,
    IReadOnlyCollection<PlanningCapacityBucketView> CapacityBuckets);
