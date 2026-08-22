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
        var timeFencePolicy = proposal.TimeFencePolicy ?? new PlanningTimeFencePolicy();
        var context = await LoadValidationContextAsync(
            proposal.BaselinePlanVersionId,
            [new ValidationTarget(proposal.PlanningKey, proposal.TargetResourceId)],
            cancellationToken);
        return ValidateMove(proposal, timeFencePolicy, context);
    }

    private static PlanningProposalImpact ValidateMove(
        PlanningMoveProposal proposal,
        PlanningTimeFencePolicy timeFencePolicy,
        MoveValidationContext context)
    {
        var operation = context.OperationsByPlanningKey.GetValueOrDefault(proposal.PlanningKey)
            ?? throw new KeyNotFoundException($"Operation {proposal.PlanningKey} is not present in the selected plan.");
        context.ResourcesById.TryGetValue(operation.ResourceId, out var source);
        var target = context.ResourcesById.GetValueOrDefault(proposal.TargetResourceId)
            ?? throw new KeyNotFoundException("The selected target resource no longer exists.");

        var duration = operation.EndUtc - operation.StartUtc;
        var targetEnd = proposal.TargetStartUtc + duration;
        var findings = new List<PlanningConstraintFinding>();

        if (operation.ExecutionStatus is OperationExecutionStatus.Running or OperationExecutionStatus.Completed)
        {
            findings.Add(Blocker(
                "EXECUTION_STATE_PROTECTED",
                $"{operation.ExecutionStatus} work cannot be moved. Repair the future schedule around it.",
                PlannerEntityType.Operation,
                operation.Id,
                operation.PlanningKey));
        }

        var eligible = operation.ResourceId == target.Id ||
                       context.EligibleResourceIdsByPlanningKey.TryGetValue(operation.PlanningKey, out var eligibleResourceIds) &&
                       eligibleResourceIds.Contains(target.Id);
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

        if (proposal.TargetStartUtc < context.State.HorizonStartUtc || targetEnd > context.State.HorizonEndUtc)
        {
            findings.Add(Blocker(
                "OUTSIDE_PLANNING_HORIZON",
                "The proposed operation falls outside the selected planning horizon.",
                PlannerEntityType.Operation,
                operation.Id,
                operation.PlanningKey));
        }

        var frozenEnd = context.State.ReferenceTimeUtc.AddMinutes(timeFencePolicy.FrozenMinutes);
        if (!proposal.AllowFrozenOverride && operation.StartUtc <= frozenEnd)
        {
            findings.Add(Blocker(
                "FROZEN_OPERATION",
                "This operation is inside the frozen time fence. Use an authorized disruption override to move it.",
                PlannerEntityType.Operation,
                operation.Id,
                operation.PlanningKey));
        }

        var calendarConflict = context.CalendarsByResourceId.TryGetValue(target.Id, out var calendars) &&
                               calendars.Any(x => !x.IsAvailable &&
                                                  x.Start < targetEnd &&
                                                  x.End > proposal.TargetStartUtc);
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
            var overlap = context.Operations
                .Where(x => x.Id != operation.Id &&
                            x.ResourceId == target.Id &&
                            x.StartUtc < targetEnd && x.EndUtc > proposal.TargetStartUtc)
                .OrderBy(x => x.StartUtc)
                .FirstOrDefault();
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

        var predecessorKeys = DeserializeKeys(operation.PredecessorPlanningKeysJson);
        var predecessorConflict = context.Operations
            .Where(x => x.Id != operation.Id && predecessorKeys.Contains(x.PlanningKey, StringComparer.OrdinalIgnoreCase))
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

        var affectedSuccessors = context.Operations
            .Where(x => x.Id != operation.Id &&
                        DeserializeKeys(x.PredecessorPlanningKeysJson)
                            .Contains(operation.PlanningKey, StringComparer.OrdinalIgnoreCase) &&
                        x.StartUtc < targetEnd)
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
        var context = await LoadValidationContextAsync(
            request.Proposal.BaselinePlanVersionId,
            [new ValidationTarget(request.Proposal.PlanningKey, request.Proposal.TargetResourceId)],
            cancellationToken);
        var impact = ValidateMove(request.Proposal, request.TimeFencePolicy, context);
        if (!impact.CanApply)
        {
            throw new InvalidOperationException(
                string.Join(" ", impact.Findings
                    .Where(x => x.Severity == PlanningConstraintSeverity.Blocker)
                    .Select(x => x.Message)));
        }

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
                ReferenceTimeUtc: context.State.ReferenceTimeUtc,
                Trigger: PlanTriggerType.OperationalRedispatch,
                Reason: $"Planner schedule move: {request.Proposal.ReasonCode}",
                ScheduleOverrides: new[] { scheduleOverride },
                RepairScope: request.RepairScope),
            cancellationToken);

        return new PlanningMoveApplyResult(impact, replan);
    }

    public async Task<PlanningBulkMoveImpact> ValidateBulkMoveAsync(
        PlanningBulkMoveProposal proposal,
        CancellationToken cancellationToken = default)
    {
        var timeFencePolicy = proposal.TimeFencePolicy ?? new PlanningTimeFencePolicy();
        return await ValidateBulkMoveCoreAsync(proposal, timeFencePolicy, cancellationToken);
    }

    private async Task<PlanningBulkMoveImpact> ValidateBulkMoveCoreAsync(
        PlanningBulkMoveProposal proposal,
        PlanningTimeFencePolicy timeFencePolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal.Moves);
        var findings = new List<PlanningConstraintFinding>();
        if (proposal.Moves.Count < 2)
        {
            findings.Add(new PlanningConstraintFinding(
                "BULK_REQUIRES_MULTIPLE_OPERATIONS",
                PlanningConstraintSeverity.Blocker,
                "An atomic bulk move requires at least two operations."));
        }

        if (proposal.Moves.Any(x => string.IsNullOrWhiteSpace(x.PlanningKey)))
        {
            findings.Add(new PlanningConstraintFinding(
                "BULK_INVALID_OPERATION_KEY",
                PlanningConstraintSeverity.Blocker,
                "Every atomic bulk-move item requires a planning key."));
        }

        var duplicateKeys = proposal.Moves
            .Where(x => !string.IsNullOrWhiteSpace(x.PlanningKey))
            .GroupBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToArray();
        if (duplicateKeys.Length > 0)
        {
            findings.Add(new PlanningConstraintFinding(
                "BULK_DUPLICATE_OPERATION",
                PlanningConstraintSeverity.Blocker,
                $"Each operation may appear once in an atomic bulk move. Duplicate: {string.Join(", ", duplicateKeys)}."));
        }

        var validMoves = proposal.Moves
            .Where(x => !string.IsNullOrWhiteSpace(x.PlanningKey))
            .DistinctBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (validMoves.Length == 0)
        {
            return new PlanningBulkMoveImpact(false, Array.Empty<PlanningProposalImpact>(), findings);
        }

        var context = await LoadValidationContextAsync(
            proposal.BaselinePlanVersionId,
            validMoves.Select(x => new ValidationTarget(x.PlanningKey, x.TargetResourceId)).ToArray(),
            cancellationToken);
        var impacts = validMoves
            .Select(move => ValidateMove(
                new PlanningMoveProposal(
                    proposal.BaselinePlanVersionId,
                    move.PlanningKey,
                    move.TargetResourceId,
                    move.TargetStartUtc,
                    proposal.ReasonCode,
                    proposal.Comment,
                    proposal.AllowFrozenOverride,
                    timeFencePolicy),
                timeFencePolicy,
                context))
            .ToArray();

        foreach (var targetGroup in validMoves.GroupBy(x => x.TargetResourceId))
        {
            if (!context.ResourcesById.TryGetValue(targetGroup.Key, out var targetResource) ||
                targetResource.SchedulingMode != ResourceSchedulingMode.Disjunctive)
            {
                continue;
            }

            var ordered = targetGroup.Select(impactFor).OrderBy(x => x.TargetStartUtc).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                if (ordered[index].TargetStartUtc >= ordered[index - 1].TargetEndUtc) continue;
                findings.Add(new PlanningConstraintFinding(
                    "BULK_TARGET_CONFLICT",
                    PlanningConstraintSeverity.Blocker,
                    $"Atomic move items {ordered[index - 1].PlanningKey} and {ordered[index].PlanningKey} overlap on {ordered[index].TargetResourceCode}."));
            }
        }

        findings.AddRange(impacts.SelectMany(x => x.Findings));
        return new PlanningBulkMoveImpact(
            impacts.Length == proposal.Moves.Count && findings.All(x => x.Severity != PlanningConstraintSeverity.Blocker),
            impacts,
            findings);

        PlanningProposalImpact impactFor(PlanningBulkMoveItem move) => impacts.Single(x =>
            x.PlanningKey.Equals(move.PlanningKey, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<PlanningBulkMoveApplyResult> ApplyBulkMoveAsync(
        PlanningBulkMoveApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request.Proposal.Moves);
        var validMoves = request.Proposal.Moves
            .Where(x => !string.IsNullOrWhiteSpace(x.PlanningKey))
            .DistinctBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var context = validMoves.Length == 0
            ? null
            : await LoadValidationContextAsync(
                request.Proposal.BaselinePlanVersionId,
                validMoves.Select(x => new ValidationTarget(x.PlanningKey, x.TargetResourceId)).ToArray(),
                cancellationToken);
        var impact = context is null
            ? await ValidateBulkMoveCoreAsync(request.Proposal, request.TimeFencePolicy, cancellationToken)
            : ValidateBulkMoveFromContext(request.Proposal, request.TimeFencePolicy, validMoves, context);
        if (!impact.CanApply)
        {
            throw new InvalidOperationException(
                string.Join(" ", impact.Findings
                    .Where(x => x.Severity == PlanningConstraintSeverity.Blocker)
                    .Select(x => x.Message)
                    .Distinct(StringComparer.Ordinal)));
        }

        var scheduleOverrides = request.Proposal.Moves.Select(move => new OperationScheduleOverride(
            move.PlanningKey,
            move.TargetResourceId,
            move.TargetStartUtc,
            request.Proposal.ReasonCode,
            request.Proposal.Comment)).ToArray();
        var replan = await lifecycle.ReplanAsync(
            request.Proposal.BaselinePlanVersionId,
            new PlanningRecalculationRequest(
                request.Planning,
                request.TimeFencePolicy,
                ReferenceTimeUtc: context!.State.ReferenceTimeUtc,
                Trigger: PlanTriggerType.OperationalRedispatch,
                Reason: $"Planner atomic bulk move ({scheduleOverrides.Length} operations): {request.Proposal.ReasonCode}",
                ScheduleOverrides: scheduleOverrides,
                RepairScope: request.RepairScope),
            cancellationToken);

        return new PlanningBulkMoveApplyResult(impact, replan);
    }

    private static PlanningBulkMoveImpact ValidateBulkMoveFromContext(
        PlanningBulkMoveProposal proposal,
        PlanningTimeFencePolicy timeFencePolicy,
        IReadOnlyCollection<PlanningBulkMoveItem> validMoves,
        MoveValidationContext context)
    {
        var findings = new List<PlanningConstraintFinding>();
        if (proposal.Moves.Count < 2)
        {
            findings.Add(new PlanningConstraintFinding(
                "BULK_REQUIRES_MULTIPLE_OPERATIONS",
                PlanningConstraintSeverity.Blocker,
                "An atomic bulk move requires at least two operations."));
        }
        if (proposal.Moves.Any(x => string.IsNullOrWhiteSpace(x.PlanningKey)))
        {
            findings.Add(new PlanningConstraintFinding(
                "BULK_INVALID_OPERATION_KEY",
                PlanningConstraintSeverity.Blocker,
                "Every atomic bulk-move item requires a planning key."));
        }
        var duplicateKeys = proposal.Moves
            .Where(x => !string.IsNullOrWhiteSpace(x.PlanningKey))
            .GroupBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToArray();
        if (duplicateKeys.Length > 0)
        {
            findings.Add(new PlanningConstraintFinding(
                "BULK_DUPLICATE_OPERATION",
                PlanningConstraintSeverity.Blocker,
                $"Each operation may appear once in an atomic bulk move. Duplicate: {string.Join(", ", duplicateKeys)}."));
        }

        var impacts = validMoves
            .Select(move => ValidateMove(
                new PlanningMoveProposal(
                    proposal.BaselinePlanVersionId,
                    move.PlanningKey,
                    move.TargetResourceId,
                    move.TargetStartUtc,
                    proposal.ReasonCode,
                    proposal.Comment,
                    proposal.AllowFrozenOverride,
                    timeFencePolicy),
                timeFencePolicy,
                context))
            .ToArray();

        foreach (var targetGroup in validMoves.GroupBy(x => x.TargetResourceId))
        {
            if (!context.ResourcesById.TryGetValue(targetGroup.Key, out var targetResource) ||
                targetResource.SchedulingMode != ResourceSchedulingMode.Disjunctive)
            {
                continue;
            }

            var ordered = targetGroup
                .Select(move => impacts.Single(x => x.PlanningKey.Equals(move.PlanningKey, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(x => x.TargetStartUtc)
                .ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                if (ordered[index].TargetStartUtc >= ordered[index - 1].TargetEndUtc) continue;
                findings.Add(new PlanningConstraintFinding(
                    "BULK_TARGET_CONFLICT",
                    PlanningConstraintSeverity.Blocker,
                    $"Atomic move items {ordered[index - 1].PlanningKey} and {ordered[index].PlanningKey} overlap on {ordered[index].TargetResourceCode}."));
            }
        }

        findings.AddRange(impacts.SelectMany(x => x.Findings));
        return new PlanningBulkMoveImpact(
            impacts.Length == proposal.Moves.Count && findings.All(x => x.Severity != PlanningConstraintSeverity.Blocker),
            impacts,
            findings);
    }

    private async Task<MoveValidationContext> LoadValidationContextAsync(
        Guid planVersionId,
        IReadOnlyCollection<ValidationTarget> targets,
        CancellationToken cancellationToken)
    {
        var state = await db.PlanVersionStates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.PlanVersionId == planVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("The selected baseline Plan Version no longer exists.");

        var operations = await db.PlanOperationSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId)
            .ToArrayAsync(cancellationToken);
        var operationsByPlanningKey = operations.ToDictionary(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase);

        var planningKeys = targets.Select(x => x.PlanningKey).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var targetResourceIds = targets.Select(x => x.TargetResourceId).Distinct().ToArray();
        var sourceResourceIds = operations
            .Where(x => planningKeys.Contains(x.PlanningKey, StringComparer.OrdinalIgnoreCase))
            .Select(x => x.ResourceId);
        var relevantResourceIds = sourceResourceIds.Concat(targetResourceIds).Distinct().ToArray();

        var resources = relevantResourceIds.Length == 0
            ? Array.Empty<Resource>()
            : await db.Resources.AsNoTracking()
                .Where(x => relevantResourceIds.Contains(x.Id))
                .ToArrayAsync(cancellationToken);
        var resourcesById = resources.ToDictionary(x => x.Id);

        var resourceOptions = planningKeys.Length == 0 || targetResourceIds.Length == 0
            ? Array.Empty<PlanOperationResourceOptionSnapshot>()
            : await db.PlanOperationResourceOptionSnapshots.AsNoTracking()
                .Where(x => x.PlanVersionId == planVersionId &&
                            planningKeys.Contains(x.PlanningKey) &&
                            targetResourceIds.Contains(x.ResourceId))
                .ToArrayAsync(cancellationToken);
        var eligibleResourceIdsByPlanningKey = resourceOptions
            .GroupBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.ResourceId).ToHashSet(),
                StringComparer.OrdinalIgnoreCase);

        var calendars = targetResourceIds.Length == 0
            ? Array.Empty<ResourceCalendar>()
            : await db.ResourceCalendars.AsNoTracking()
                .Where(x => targetResourceIds.Contains(x.ResourceId))
                .ToArrayAsync(cancellationToken);
        var calendarsByResourceId = calendars
            .GroupBy(x => x.ResourceId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        return new MoveValidationContext(
            state,
            operations,
            operationsByPlanningKey,
            resourcesById,
            eligibleResourceIdsByPlanningKey,
            calendarsByResourceId);
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

    private sealed record ValidationTarget(string PlanningKey, Guid TargetResourceId);

    private sealed record MoveValidationContext(
        PlanVersionState State,
        IReadOnlyCollection<PlanOperationSnapshot> Operations,
        IReadOnlyDictionary<string, PlanOperationSnapshot> OperationsByPlanningKey,
        IReadOnlyDictionary<Guid, Resource> ResourcesById,
        IReadOnlyDictionary<string, HashSet<Guid>> EligibleResourceIdsByPlanningKey,
        IReadOnlyDictionary<Guid, ResourceCalendar[]> CalendarsByResourceId);
}
