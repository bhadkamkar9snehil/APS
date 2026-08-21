namespace APS.Application;

public enum PlanningConstraintSeverity
{
    Information = 1,
    Warning = 2,
    Blocker = 3
}

public sealed record PlanningConstraintFinding(
    string Code,
    PlanningConstraintSeverity Severity,
    string Message,
    PlannerEntityRef? Entity = null);

public sealed record PlanningMoveProposal(
    Guid BaselinePlanVersionId,
    string PlanningKey,
    Guid TargetResourceId,
    DateTime TargetStartUtc,
    string ReasonCode,
    string? Comment = null,
    bool AllowFrozenOverride = false);

public sealed record PlanningProposalImpact(
    bool CanApply,
    string PlanningKey,
    string SourceResourceCode,
    DateTime SourceStartUtc,
    DateTime SourceEndUtc,
    string TargetResourceCode,
    DateTime TargetStartUtc,
    DateTime TargetEndUtc,
    int MovementMinutes,
    bool ResourceChanged,
    IReadOnlyCollection<PlanningConstraintFinding> Findings);

public sealed record PlanningMoveApplyRequest(
    PlanningMoveProposal Proposal,
    PlanningCalculationRequest Planning,
    PlanningTimeFencePolicy TimeFencePolicy,
    RepairScopePolicy? RepairScope = null);

public sealed record PlanningMoveApplyResult(
    PlanningProposalImpact Impact,
    PersistedPlanningRunResult Replan);

public interface IPlanningWorkbenchCommandService
{
    Task<PlanningProposalImpact> ValidateMoveAsync(
        PlanningMoveProposal proposal,
        CancellationToken cancellationToken = default);

    Task<PlanningMoveApplyResult> ApplyMoveAsync(
        PlanningMoveApplyRequest request,
        CancellationToken cancellationToken = default);
}
