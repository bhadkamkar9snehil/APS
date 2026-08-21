using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace APS.Infrastructure;

public sealed class PlanningWorkbenchCommandService(
    ApsDbContext db,
    IPlanningLifecycleService lifecycle) : IPlanningWorkbenchCommandService
{
    public async Task<PlanningProposalImpact> ValidateMoveAsync(
        PlanningMoveProposal proposal,
        CancellationToken cancellationToken = default)
    {
        var state = await db.PlanVersionStates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.PlanVersionId == proposal.BaselinePlanVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("The selected baseline Plan Version no longer exists.");
        var operation = await db.PlanOperationSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.PlanVersionId == proposal.BaselinePlanVersionId && x.PlanningKey == proposal.PlanningKey,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {proposal.PlanningKey} is not present in the selected plan.");
        var source = await db.Resources.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == operation.ResourceId, cancellationToken);
        var target = await db.Resources.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == proposal.TargetResourceId, cancellationToken)
            ?? throw new KeyNotFoundException("The selected target resource no longer exists.");

        var duration = operation.EndUtc - operation.StartUtc;
        var targetEnd = proposal.TargetStartUtc + duration;
        var findings = new List<PlanningConstraintFinding>();

        var eligible = operation.ResourceId == target.Id || await db.PlanOperationResourceOptionSnapshots.AsNoTracking()
            .AnyAsync(x => x.PlanVersionId == proposal.BaselinePlanVersionId &&
                           x.PlanningKey == proposal.PlanningKey &&
                           x.ResourceId == target.Id,
                cancellationToken);
        if (!eligible)
        {
            findings.Add(Blocker(
                "RESOURCE_NOT_ELIGIBLE",
                $"{target.Code} is not an eligible resource for this operation.",
                PlannerEntityType.Resource,
                target.Id,
                target.Code));
        }

        if (target.OperatingState is ResourceOperatingState.Breakdown or
            ResourceOperatingState.PlannedMaintenance or
            ResourceOperatingState.Disabled)
        {
            findings.Add(Blocker(
                "RESOURCE_UNAVAILABLE",
                $"{target.Code} is {target.OperatingState}.",
                PlannerEntityType.Resource,
                target.Id,
                target.Code));
        }

        if (proposal.TargetStartUtc < state.HorizonStartUtc || targetEnd > state.HorizonEndUtc)
        {
            findings.Add(Blocker(
                "OUTSIDE_PLANNING_HORIZON",
                "The proposed operation falls outside the selected planning horizon.",
                PlannerEntityType.Operation,
                operation.Id,
                operation.PlanningKey));
        }

        var frozenEnd = state.ReferenceTimeUtc.AddMinutes(new PlanningTimeFencePolicy().FrozenMinutes);
        if (!proposal.AllowFrozenOverride && operation.StartUtc <= frozenEnd)
        {
            findings.Add(Blocker(
                "FROZEN_OPERATION",
                "This operation is inside the frozen time fence. Use an authorized disruption override to move it.",
                PlannerEntityType.Operation,
                operation.Id,
                operation.PlanningKey));
        }

        var calendarConflict = await db.ResourceCalendars.AsNoTracking()
            .AnyAsync(x => x.ResourceId == target.Id && !x.IsAvailable &&
                           x.Start < targetEnd && x.End > proposal.TargetStartUtc,
                cancellationToken);
        if (calendarConflict)
        {
            findings.Add(Blocker(
                "RESOURCE_CALENDAR_CONFLICT",
                $"{target.Code} is unavailable during the proposed interval.",
                PlannerEntityType.Resource,
                target.Id,
                target.Code));
        }

        if (target.SchedulingMode == ResourceSchedulingMode.Disjunctive)
        {
            var overlap = await db.PlanOperationSnapshots.AsNoTracking()
                .Where(x => x.PlanVersionId == proposal.BaselinePlanVersionId &&
                            x.Id != operation.Id &&
                            x.ResourceId == target.Id &&
                            x.StartUtc < targetEnd && x.EndUtc > proposal.TargetStartUtc)
                .OrderBy(x => x.StartUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (overlap is not null)
            {
                findings.Add(overlap.StartUtc <= frozenEnd
                    ? Blocker(
                        "FROZEN_RESOURCE_CONFLICT",
                        $"The proposed interval conflicts with frozen work on {target.Code}.",
                        PlannerEntityType.Operation,
                        overlap.Id,
                        overlap.PlanningKey)
                    : new PlanningConstraintFinding(
                        "RESOURCE_REPAIR_REQUIRED",
                        PlanningConstraintSeverity.Warning,
                        $"The proposed interval uses occupied time on {target.Code}; the solver will move affected flexible work.",
                        new PlannerEntityRef(
                            PlannerEntityType.Operation,
                            overlap.Id,
                            overlap.PlanningKey)));
            }
        }

        var planOperations = await db.PlanOperationSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == proposal.BaselinePlanVersionId && x.Id != operation.Id)
            .ToArrayAsync(cancellationToken);
        var predecessorKeys = DeserializeKeys(operation.PredecessorPlanningKeysJson);
        var predecessorConflict = planOperations
            .Where(x => predecessorKeys.Contains(x.PlanningKey, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(x => x.EndUtc)
            .FirstOrDefault(x => x.EndUtc > proposal.TargetStartUtc);
        if (predecessorConflict is not null)
        {
            findings.Add(predecessorConflict.StartUtc <= frozenEnd
                ? Blocker(
                    "FROZEN_PREDECESSOR_CONFLICT",
                    "The proposed start is earlier than a frozen predecessor can finish.",
                    PlannerEntityType.Operation,
                    predecessorConflict.Id,
                    predecessorConflict.PlanningKey)
                : new PlanningConstraintFinding(
                    "PREDECESSOR_REPAIR_REQUIRED",
                    PlanningConstraintSeverity.Warning,
                    "An upstream operation must be repaired before this operation can start at the proposed time.",
                    new PlannerEntityRef(
                        PlannerEntityType.Operation,
                        predecessorConflict.Id,
                        predecessorConflict.PlanningKey)));
        }

        var affectedSuccessors = planOperations
            .Where(x => DeserializeKeys(x.PredecessorPlanningKeysJson)
                .Contains(operation.PlanningKey, StringComparer.OrdinalIgnoreCase) && x.StartUtc < targetEnd)
            .ToArray();
        if (affectedSuccessors.Length > 0)
        {
            findings.Add(new PlanningConstraintFinding(
                "SUCCESSOR_REPAIR_REQUIRED",
                PlanningConstraintSeverity.Warning,
                $"{affectedSuccessors.Length} downstream operation(s) will be repaired after this move."));
        }

        if (findings.All(x => x.Severity != PlanningConstraintSeverity.Blocker))
        {
            findings.Add(new PlanningConstraintFinding(
                "SOLVER_REPAIR_REQUIRED",
                PlanningConstraintSeverity.Information,
                "Applying this move will repair affected successors and revalidate material, thermal, and sequence constraints."));
        }

        return new PlanningProposalImpact(
            findings.All(x => x.Severity != PlanningConstraintSeverity.Blocker),
            operation.PlanningKey,
            source?.Code ?? "Unknown resource",
            operation.StartUtc,
            operation.EndUtc,
            target.Code,
            proposal.TargetStartUtc,
            targetEnd,
            (int)Math.Round((proposal.TargetStartUtc - operation.StartUtc).TotalMinutes),
            operation.ResourceId != target.Id,
            findings);
    }

    public async Task<PlanningMoveApplyResult> ApplyMoveAsync(
        PlanningMoveApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        var impact = await ValidateMoveAsync(request.Proposal, cancellationToken);
        if (!impact.CanApply)
        {
            throw new InvalidOperationException(
                string.Join(" ", impact.Findings
                    .Where(x => x.Severity == PlanningConstraintSeverity.Blocker)
                    .Select(x => x.Message)));
        }

        var baselineState = await db.PlanVersionStates.AsNoTracking()
            .SingleAsync(x => x.PlanVersionId == request.Proposal.BaselinePlanVersionId, cancellationToken);
        var scheduleOverride = new OperationScheduleOverride(
            request.Proposal.PlanningKey,
            request.Proposal.TargetResourceId,
            request.Proposal.TargetStartUtc,
            request.Proposal.ReasonCode,
            request.Proposal.Comment);
        var replan = await lifecycle.ReplanAsync(
            request.Proposal.BaselinePlanVersionId,
            new PlanningRecalculationRequest(
                request.Planning,
                request.TimeFencePolicy,
                ReferenceTimeUtc: baselineState.ReferenceTimeUtc,
                Trigger: PlanTriggerType.OperationalRedispatch,
                Reason: $"Planner schedule move: {request.Proposal.ReasonCode}",
                ScheduleOverrides: new[] { scheduleOverride },
                RepairScope: request.RepairScope),
            cancellationToken);

        return new PlanningMoveApplyResult(impact, replan);
    }

    private static PlanningConstraintFinding Blocker(
        string code,
        string message,
        PlannerEntityType type,
        Guid id,
        string displayCode) =>
        new(code, PlanningConstraintSeverity.Blocker, message, new PlannerEntityRef(type, id, displayCode));

    private static IReadOnlyCollection<string> DeserializeKeys(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch (JsonException) { return Array.Empty<string>(); }
    }
}
