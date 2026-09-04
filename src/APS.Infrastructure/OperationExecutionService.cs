using System.Text.Json;
using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class OperationExecutionService(ApsDbContext db) : IOperationExecutionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<OperationExecutionSnapshot> ApplyAsync(
        OperationExecutionUpdate update,
        CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(update, saveChanges: true, cancellationToken);

    /// <summary>
    /// Applies the canonical operation-grain execution rules without forcing an immediate database save.
    /// Specialized execution adapters such as casting can stage their physical evidence in the same
    /// DbContext transaction and call this method instead of duplicating the operation state machine.
    /// </summary>
    internal async Task<OperationExecutionSnapshot> ApplyCoreAsync(
        OperationExecutionUpdate update,
        bool saveChanges,
        CancellationToken cancellationToken = default)
    {
        Validate(update);

        var operation = await db.PlanOperationSnapshots.SingleOrDefaultAsync(x =>
            x.PlanVersionId == update.PlanVersionId && x.PlanningKey == update.PlanningKey,
            cancellationToken)
            ?? throw new KeyNotFoundException("No planned process operation matches the supplied Plan Version and planning key.");

        var history = DeserializeHistory(operation.ExecutionHistoryJson);
        if (!string.IsNullOrWhiteSpace(update.ExternalEventId))
        {
            var duplicate = history.Any(x =>
                x.Source == update.Source &&
                string.Equals(x.ExternalEventId, update.ExternalEventId, StringComparison.OrdinalIgnoreCase));
            if (duplicate) return Snapshot(operation);
        }

        var previous = operation.ExecutionStatus;
        if (!update.IsCorrection && !CanTransition(previous, update.Status))
        {
            throw new InvalidOperationException(
                $"Operation {update.PlanningKey} cannot move from {previous} to {update.Status} without an explicit correction.");
        }

        var resource = update.ActualResourceId ?? operation.ActualResourceId ?? operation.CommittedResourceId ?? operation.ResourceId;
        var resourceWasEligible = IsEligibleResource(operation, resource);

        // READY is a dispatch/commitment decision, not yet immutable execution truth. Reject an invalid
        // resource and require a real redispatch/replan rather than bypassing the planning constraints.
        if (update.Status == OperationExecutionStatus.Ready &&
            update.ActualResourceId.HasValue &&
            !resourceWasEligible &&
            !update.IsCorrection)
        {
            throw new InvalidOperationException(
                $"Resource {resource} was not an eligible alternative for {operation.PlanningKey}. Create an operational redispatch/replan rather than committing an infeasible assignment.");
        }

        operation.ExecutionStatus = update.Status;
        operation.LastExecutionChangedOnUtc = update.ChangedOnUtc;
        operation.ActualStartUtc = update.ActualStartUtc ?? operation.ActualStartUtc ??
                                   (update.Status == OperationExecutionStatus.Running ? update.ChangedOnUtc : null);
        operation.ActualEndUtc = update.ActualEndUtc ??
                                 (update.Status == OperationExecutionStatus.Completed ? update.ChangedOnUtc : operation.ActualEndUtc);
        operation.ActualQuantityMt = update.ActualQuantityMt ?? operation.ActualQuantityMt;

        if (operation.ActualStartUtc.HasValue && operation.ActualEndUtc.HasValue && operation.ActualEndUtc < operation.ActualStartUtc)
            throw new InvalidOperationException("Operation actual end cannot be before actual start.");
        if (operation.ActualQuantityMt < 0m)
            throw new InvalidOperationException("Operation actual quantity cannot be negative.");

        switch (update.Status)
        {
            case OperationExecutionStatus.Ready:
                if (update.ActualResourceId.HasValue)
                {
                    operation.CommittedResourceId = resource;
                    operation.AssignmentCommitmentState = OperationAssignmentCommitmentState.Committed;
                }
                break;
            case OperationExecutionStatus.Running:
                operation.CommittedResourceId = resource;
                operation.ActualResourceId = resource;
                operation.AssignmentCommitmentState = OperationAssignmentCommitmentState.Running;
                break;
            case OperationExecutionStatus.Completed:
                operation.CommittedResourceId = resource;
                operation.ActualResourceId = resource;
                operation.AssignmentCommitmentState = OperationAssignmentCommitmentState.Completed;
                break;
            case OperationExecutionStatus.Held:
                if (operation.ActualResourceId.HasValue)
                    operation.AssignmentCommitmentState = OperationAssignmentCommitmentState.Running;
                break;
        }

        // Running/completed actuals are facts, even when the plant executed outside the APS-approved
        // option set. Record the fact, flag the deviation, and let replanning/diagnostics repair the future.
        if (update.Status is OperationExecutionStatus.Running or OperationExecutionStatus.Completed &&
            update.ActualResourceId.HasValue &&
            !resourceWasEligible)
        {
            operation.IsOffPlanActualResource = true;
            operation.OffPlanActualReasonCode = "ACTUAL_RESOURCE_NOT_IN_PLANNED_ELIGIBLE_SET";
        }
        else if (update.IsCorrection && resourceWasEligible)
        {
            operation.IsOffPlanActualResource = false;
            operation.OffPlanActualReasonCode = null;
        }

        history.Add(new OperationExecutionEventSnapshot(
            previous,
            update.Status,
            update.ActualResourceId,
            update.ChangedOnUtc,
            update.Source,
            update.ExternalEventId,
            update.Comment));
        operation.ExecutionHistoryJson = JsonSerializer.Serialize(history, JsonOptions);

        // Execution progress is itself a commitment trigger for downstream routable operations. Evaluate
        // the whole Plan Version before saving so successor LRF/VD/CCM/RHF/RM assignments harden only
        // according to their own snapshotted policy.
        await RefreshCommitmentsCoreAsync(update.PlanVersionId, update.ChangedOnUtc, cancellationToken);

        if (saveChanges)
            await db.SaveChangesAsync(cancellationToken);

        return Snapshot(operation);
    }

    public async Task<IReadOnlyCollection<OperationExecutionSnapshot>> RefreshCommitmentsAsync(
        Guid planVersionId,
        DateTime referenceTimeUtc,
        CancellationToken cancellationToken = default)
    {
        var operations = await RefreshCommitmentsCoreAsync(planVersionId, referenceTimeUtc, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return operations.OrderBy(x => x.StartUtc).ThenBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .Select(Snapshot)
            .ToArray();
    }

    private async Task<IReadOnlyCollection<PlanOperationSnapshot>> RefreshCommitmentsCoreAsync(
        Guid planVersionId,
        DateTime referenceTimeUtc,
        CancellationToken cancellationToken)
    {
        var operations = await db.PlanOperationSnapshots
            .Where(x => x.PlanVersionId == planVersionId)
            .OrderBy(x => x.StartUtc)
            .ToListAsync(cancellationToken);
        if (operations.Count == 0)
            throw new KeyNotFoundException("No planned process operations exist for the supplied Plan Version.");

        var byKey = operations.ToDictionary(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase);

        foreach (var operation in operations)
        {
            operation.CommitmentLastEvaluatedOnUtc = referenceTimeUtc;

            if (operation.ExecutionStatus == OperationExecutionStatus.Completed)
            {
                operation.AssignmentCommitmentState = OperationAssignmentCommitmentState.Completed;
                operation.CommittedResourceId ??= operation.ActualResourceId ?? operation.ResourceId;
                continue;
            }
            if (operation.ExecutionStatus is OperationExecutionStatus.Running or OperationExecutionStatus.Held && operation.ActualResourceId.HasValue)
            {
                operation.AssignmentCommitmentState = OperationAssignmentCommitmentState.Running;
                operation.CommittedResourceId ??= operation.ActualResourceId;
                continue;
            }
            if (operation.ExecutionStatus == OperationExecutionStatus.Cancelled) continue;

            var policy = DeserializePolicy(operation.AssignmentPolicyJson);
            if (policy is null) continue;

            var desired = EvaluateClockCommitment(policy, operation.StartUtc, referenceTimeUtc);
            var predecessorKeys = DeserializePredecessorKeys(operation.PredecessorPlanningKeysJson);
            if (predecessorKeys.Count > 0)
            {
                var predecessors = predecessorKeys
                    .Where(byKey.ContainsKey)
                    .Select(key => byKey[key])
                    .ToArray();

                // Do not treat missing predecessor snapshots as satisfied. All snapshotted predecessors
                // must reach the configured gate before process progress can harden this assignment.
                if (predecessors.Length == predecessorKeys.Count)
                {
                    var allRunningOrCompleted = predecessors.All(x =>
                        x.ExecutionStatus is OperationExecutionStatus.Running or OperationExecutionStatus.Completed);
                    var allCompleted = predecessors.All(x => x.ExecutionStatus == OperationExecutionStatus.Completed);
                    var predecessorGateSatisfied =
                        (policy.CommitWhenPredecessorRunning && allRunningOrCompleted) ||
                        (policy.CommitWhenPredecessorCompleted && allCompleted);

                    if (predecessorGateSatisfied)
                    {
                        desired = MaxCommitment(
                            desired,
                            policy.RequireDispatchAcknowledgement
                                ? OperationAssignmentCommitmentState.Firm
                                : OperationAssignmentCommitmentState.Committed);
                    }
                }
            }

            // READY with a selected resource is an explicit dispatch acknowledgement and therefore
            // overrides a policy that otherwise requires acknowledgement before auto-commit.
            if (operation.ExecutionStatus == OperationExecutionStatus.Ready && operation.CommittedResourceId.HasValue)
                desired = MaxCommitment(desired, OperationAssignmentCommitmentState.Committed);

            var promoted = MaxCommitment(operation.AssignmentCommitmentState, desired);
            if (promoted == operation.AssignmentCommitmentState) continue;

            operation.AssignmentCommitmentState = promoted;
            if (promoted == OperationAssignmentCommitmentState.Committed)
                operation.CommittedResourceId ??= operation.ResourceId;
        }

        return operations;
    }

    private static OperationAssignmentCommitmentState EvaluateClockCommitment(
        OperationAssignmentPolicy policy,
        DateTime plannedStartUtc,
        DateTime referenceTimeUtc)
    {
        var minutes = (plannedStartUtc - referenceTimeUtc).TotalMinutes;
        if (minutes <= policy.CommitMinutesBeforeStart)
        {
            return policy.RequireDispatchAcknowledgement
                ? OperationAssignmentCommitmentState.Firm
                : OperationAssignmentCommitmentState.Committed;
        }
        if (minutes <= policy.FirmMinutesBeforeStart) return OperationAssignmentCommitmentState.Firm;
        return OperationAssignmentCommitmentState.Flexible;
    }

    private static OperationAssignmentCommitmentState MaxCommitment(
        OperationAssignmentCommitmentState left,
        OperationAssignmentCommitmentState right) =>
        (OperationAssignmentCommitmentState)Math.Max((int)left, (int)right);

    private static OperationAssignmentPolicy? DeserializePolicy(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<OperationAssignmentPolicy>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> DeserializePredecessorKeys(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions)
                   ?? new List<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsEligibleResource(PlanOperationSnapshot operation, Guid resourceId)
    {
        if (resourceId == operation.ResourceId || resourceId == operation.CommittedResourceId) return true;
        if (string.IsNullOrWhiteSpace(operation.EligibleResourceOptionsJson)) return false;
        try
        {
            var alternatives = JsonSerializer.Deserialize<List<PlanningOperationResourceAlternative>>(
                operation.EligibleResourceOptionsJson,
                JsonOptions) ?? new List<PlanningOperationResourceAlternative>();
            return alternatives.Any(x => x.ResourceId == resourceId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static List<OperationExecutionEventSnapshot> DeserializeHistory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<OperationExecutionEventSnapshot>();
        return JsonSerializer.Deserialize<List<OperationExecutionEventSnapshot>>(json, JsonOptions)
               ?? new List<OperationExecutionEventSnapshot>();
    }

    private static OperationExecutionSnapshot Snapshot(PlanOperationSnapshot operation) => new(
        operation.PlanVersionId,
        operation.PlanningKey,
        operation.ProcessOperationType,
        operation.ResourceId,
        operation.CommittedResourceId,
        operation.ActualResourceId,
        operation.AssignmentCommitmentState,
        operation.ExecutionStatus,
        operation.StartUtc,
        operation.EndUtc,
        operation.ActualStartUtc,
        operation.ActualEndUtc,
        operation.QuantityMt,
        operation.ActualQuantityMt,
        operation.LastExecutionChangedOnUtc,
        operation.IsOffPlanActualResource,
        operation.OffPlanActualReasonCode);

    private static void Validate(OperationExecutionUpdate update)
    {
        if (string.IsNullOrWhiteSpace(update.PlanningKey))
            throw new ArgumentException("PlanningKey is required.", nameof(update));
        if (update.Source != ExecutionUpdateSource.Manual && string.IsNullOrWhiteSpace(update.ExternalEventId))
            throw new ArgumentException("ExternalEventId is required for non-manual execution updates.", nameof(update));
        if (update.ActualQuantityMt < 0m)
            throw new ArgumentOutOfRangeException(nameof(update.ActualQuantityMt));
        if (update.ActualStartUtc.HasValue && update.ActualEndUtc.HasValue && update.ActualEndUtc < update.ActualStartUtc)
            throw new ArgumentException("ActualEndUtc cannot be before ActualStartUtc.", nameof(update));
    }

    private static bool CanTransition(OperationExecutionStatus from, OperationExecutionStatus to)
    {
        if (from == to) return true;
        return from switch
        {
            OperationExecutionStatus.Planned => to is OperationExecutionStatus.Ready or OperationExecutionStatus.Running or OperationExecutionStatus.Held or OperationExecutionStatus.Cancelled,
            OperationExecutionStatus.Ready => to is OperationExecutionStatus.Running or OperationExecutionStatus.Held or OperationExecutionStatus.Cancelled,
            OperationExecutionStatus.Running => to is OperationExecutionStatus.Held or OperationExecutionStatus.Completed,
            OperationExecutionStatus.Held => to is OperationExecutionStatus.Ready or OperationExecutionStatus.Running or OperationExecutionStatus.Cancelled,
            OperationExecutionStatus.Completed => false,
            OperationExecutionStatus.Cancelled => false,
            _ => false
        };
    }
}
