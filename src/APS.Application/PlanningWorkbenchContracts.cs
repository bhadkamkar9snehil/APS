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
    IReadOnlyCollection<PlanningOperationWorkbenchDetail> OperationDetails);
