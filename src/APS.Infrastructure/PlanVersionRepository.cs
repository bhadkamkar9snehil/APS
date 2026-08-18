using System.Text.Json;
using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class PlanVersionRepository(ApsDbContext db) : IPlanVersionRepository
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PlanVersionSnapshot> SaveAsync(
        PersistPlanningRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = request.PlanningResult;
        var existing = await db.PlanVersions
            .AsNoTracking()
            .AnyAsync(x => x.Id == result.PlanVersionId, cancellationToken);
        if (existing)
        {
            return await GetAsync(result.PlanVersionId, cancellationToken)
                   ?? throw new InvalidOperationException("Persisted plan version could not be reloaded.");
        }

        var version = new PlanVersion
        {
            Id = result.PlanVersionId,
            VersionNumber = $"PLAN-{result.CreatedOnUtc:yyyyMMdd-HHmmss}-{result.PlanVersionId.ToString("N")[..6].ToUpperInvariant()}",
            CreatedOnUtc = result.CreatedOnUtc,
            Reason = request.Reason,
            IsReleased = false
        };

        var state = new PlanVersionState
        {
            PlanVersionId = result.PlanVersionId,
            ParentPlanVersionId = result.BaselinePlanVersionId,
            Status = result.IsFeasible ? PlanVersionStatus.Feasible : PlanVersionStatus.Failed,
            Trigger = request.Trigger,
            ReferenceTimeUtc = request.ReferenceTimeUtc,
            HorizonStartUtc = request.PlanningRequest.HorizonStartUtc,
            HorizonEndUtc = request.PlanningRequest.HorizonEndUtc,
            SolverStatus = result.Schedule.SolverStatus,
            ObjectiveValue = result.IsFeasible ? result.Schedule.ObjectiveValue : null,
            IsActive = result.IsFeasible,
            MaterialRequirementsJson = Serialize(result.MaterialPlan?.Requirements),
            MaterialSupplyRequirementsJson = Serialize(result.MaterialPlan?.SupplyRequirements),
            MaterialReservationsJson = Serialize(result.MaterialPlan?.Reservations),
            MaterialLedgerJson = Serialize(result.MaterialPlan?.LedgerEvents)
        };

        if (result.IsFeasible)
        {
            var activeStates = await db.PlanVersionStates
                .Where(x => x.IsActive)
                .ToListAsync(cancellationToken);
            foreach (var active in activeStates)
            {
                active.IsActive = false;
                if (active.Status == PlanVersionStatus.Feasible)
                {
                    active.Status = PlanVersionStatus.Superseded;
                }
            }
        }

        db.PlanVersions.Add(version);
        db.PlanVersionStates.Add(state);

        var tasksById = result.ProductionStructure.SchedulingTasks.ToDictionary(x => x.TaskId);
        var identitiesById = (result.TaskIdentities ?? Array.Empty<PlanningTaskIdentity>())
            .ToDictionary(x => x.TaskId);
        var alternativesByKey = (result.ResourceAlternatives ?? Array.Empty<PlanningOperationResourceAlternative>())
            .GroupBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.AssignmentPenalty).ThenBy(y => y.ResourceId).ToArray(), StringComparer.OrdinalIgnoreCase);
        var overridesByKey = (request.PlanningRequest.ReplanContext?.ResourceOverrides ?? Array.Empty<OperationResourceOverride>())
            .GroupBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);
        var baselineByKey = (request.PlanningRequest.ReplanContext?.BaselineOperations ?? Array.Empty<BaselinePlanOperation>())
            .GroupBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.StartUtc).First(), StringComparer.OrdinalIgnoreCase);

        foreach (var assignment in result.Schedule.Assignments)
        {
            if (!tasksById.TryGetValue(assignment.TaskId, out var task)) continue;
            if (!identitiesById.TryGetValue(assignment.TaskId, out var identity)) continue;

            overridesByKey.TryGetValue(identity.PlanningKey, out var resourceOverride);
            baselineByKey.TryGetValue(identity.PlanningKey, out var baseline);
            alternativesByKey.TryGetValue(identity.PlanningKey, out var alternatives);
            var commitment = resourceOverride?.CommitmentState ?? OperationAssignmentCommitmentState.Flexible;

            db.PlanOperationSnapshots.Add(new PlanOperationSnapshot
            {
                PlanVersionId = result.PlanVersionId,
                PlanningKey = identity.PlanningKey,
                SourceEntityId = assignment.SourceEntityId,
                OperationType = MapOperationType(task.TaskType),
                ProcessOperationType = ResolveProcessOperationType(task),
                ResourceId = assignment.ResourceId,
                CommittedResourceId = commitment is OperationAssignmentCommitmentState.Firm or OperationAssignmentCommitmentState.Committed
                    ? assignment.ResourceId
                    : null,
                ActualResourceId = null,
                AssignmentCommitmentState = commitment,
                EligibleResourceOptionsJson = Serialize(alternatives),
                PreviousPlannedResourceId = resourceOverride is not null && baseline is not null && baseline.ResourceId != assignment.ResourceId
                    ? baseline.ResourceId
                    : null,
                RedispatchReasonCode = resourceOverride?.ReasonCode,
                RedispatchComment = resourceOverride?.Comment,
                RedispatchedOnUtc = resourceOverride is null ? null : request.ReferenceTimeUtc,
                StartUtc = assignment.StartUtc,
                EndUtc = assignment.EndUtc,
                QuantityMt = task.QuantityMt,
                GradeCode = task.GradeCode,
                CrossSectionCode = task.CrossSectionCode
            });
        }

        foreach (var allocation in result.CampaignPlan.InventoryAllocations)
        {
            db.PlanInventoryAllocationSnapshots.Add(new PlanInventoryAllocationSnapshot
            {
                PlanVersionId = result.PlanVersionId,
                ProductionOrderId = allocation.ProductionOrderId,
                Stage = allocation.Stage,
                MaterialCode = allocation.MaterialCode,
                GradeCode = allocation.GradeCode,
                CrossSectionCode = allocation.CrossSectionCode,
                LocationCode = allocation.LocationCode,
                QuantityMt = allocation.QuantityMt,
                UseCode = (int)allocation.Use
            });
        }

        var assignmentByTask = result.Schedule.Assignments.ToDictionary(x => x.TaskId);
        foreach (var unit in result.ProductionStructure.PlannedStrandMaterialUnits ?? Array.Empty<PlannedStrandMaterialUnit>())
        {
            assignmentByTask.TryGetValue(unit.AvailabilityTaskId, out var availability);
            db.PlanMaterialUnitSnapshots.Add(new PlanMaterialUnitSnapshot
            {
                PlanVersionId = result.PlanVersionId,
                PlanningKey = unit.PlanningKey,
                CampaignId = unit.CampaignId,
                CampaignHeatId = unit.CampaignHeatId,
                CastSequenceId = unit.CastSequenceId,
                CasterResourceId = unit.CasterResourceId,
                StrandNumber = unit.StrandNumber,
                UnitSequence = unit.UnitSequence,
                GradeCode = unit.GradeCode,
                CrossSectionCode = unit.CrossSectionCode,
                QuantityMt = unit.QuantityMt,
                AvailableOnUtc = availability?.EndUtc
            });
        }

        PlanStructureSnapshotProjector.AddToContext(db, request.PlanningRequest, result);

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(result.PlanVersionId, cancellationToken)
               ?? throw new InvalidOperationException("Saved plan version could not be reloaded.");
    }

    public async Task<PlanVersionSnapshot?> GetAsync(
        Guid planVersionId,
        CancellationToken cancellationToken = default)
    {
        var version = await db.PlanVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == planVersionId, cancellationToken);
        if (version is null) return null;

        var state = await db.PlanVersionStates
            .AsNoTracking()
            .SingleAsync(x => x.PlanVersionId == planVersionId, cancellationToken);
        var operations = await GetBaselineOperationsAsync(planVersionId, cancellationToken);

        return new PlanVersionSnapshot(
            version.Id,
            version.VersionNumber,
            state.ParentPlanVersionId,
            state.Status,
            state.Trigger,
            version.CreatedOnUtc,
            state.ReferenceTimeUtc,
            state.HorizonStartUtc,
            state.HorizonEndUtc,
            state.SolverStatus,
            state.ObjectiveValue,
            state.IsActive,
            operations);
    }

    public async Task<IReadOnlyCollection<BaselinePlanOperation>> GetBaselineOperationsAsync(
        Guid planVersionId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.PlanOperationSnapshots
            .AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId)
            .OrderBy(x => x.StartUtc)
            .ToListAsync(cancellationToken);

        return rows.Select(x => new BaselinePlanOperation(
                x.PlanningKey,
                x.ActualResourceId ?? x.CommittedResourceId ?? x.ResourceId,
                x.StartUtc,
                x.EndUtc,
                MapTaskType(x.OperationType)))
            .ToArray();
    }

    private static string? Serialize<T>(IReadOnlyCollection<T>? values) =>
        values is null ? null : JsonSerializer.Serialize(values, SnapshotJsonOptions);

    private static ProcessOperationType ResolveProcessOperationType(FiniteScheduleTask task)
    {
        if (task.ProcessOperationType != ProcessOperationType.Unknown) return task.ProcessOperationType;
        return task.TaskType switch
        {
            FiniteScheduleTaskType.Eaf => ProcessOperationType.Eaf,
            FiniteScheduleTaskType.Lrf => ProcessOperationType.Lrf,
            FiniteScheduleTaskType.Vd => ProcessOperationType.Vd,
            FiniteScheduleTaskType.Casting => ProcessOperationType.Ccm,
            FiniteScheduleTaskType.Reheating => ProcessOperationType.Reheat,
            FiniteScheduleTaskType.HotRolling => ProcessOperationType.HotRoll,
            FiniteScheduleTaskType.ColdRolling => ProcessOperationType.ColdRoll,
            FiniteScheduleTaskType.Tmt => ProcessOperationType.Tmt,
            FiniteScheduleTaskType.Cooling => ProcessOperationType.Cool,
            FiniteScheduleTaskType.Cutting => ProcessOperationType.Cut,
            FiniteScheduleTaskType.Bundling => ProcessOperationType.Bundle,
            FiniteScheduleTaskType.Coiling => ProcessOperationType.Coil,
            FiniteScheduleTaskType.Finishing => ProcessOperationType.Finish,
            _ => ProcessOperationType.Unknown
        };
    }

    private static PlanOperationType MapOperationType(FiniteScheduleTaskType type) => type switch
    {
        FiniteScheduleTaskType.Casting => PlanOperationType.Casting,
        FiniteScheduleTaskType.HotRolling => PlanOperationType.HotRolling,
        FiniteScheduleTaskType.ColdRolling => PlanOperationType.ColdRolling,
        FiniteScheduleTaskType.Finishing => PlanOperationType.Finishing,
        FiniteScheduleTaskType.Eaf => PlanOperationType.Eaf,
        FiniteScheduleTaskType.Lrf => PlanOperationType.Lrf,
        FiniteScheduleTaskType.Vd => PlanOperationType.Vd,
        FiniteScheduleTaskType.Reheating => PlanOperationType.Reheating,
        FiniteScheduleTaskType.Tmt => PlanOperationType.Tmt,
        FiniteScheduleTaskType.Cooling => PlanOperationType.Cooling,
        FiniteScheduleTaskType.Cutting => PlanOperationType.Cutting,
        FiniteScheduleTaskType.Bundling => PlanOperationType.Bundling,
        FiniteScheduleTaskType.Coiling => PlanOperationType.Coiling,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    private static FiniteScheduleTaskType MapTaskType(PlanOperationType type) => type switch
    {
        PlanOperationType.Casting => FiniteScheduleTaskType.Casting,
        PlanOperationType.HotRolling => FiniteScheduleTaskType.HotRolling,
        PlanOperationType.ColdRolling => FiniteScheduleTaskType.ColdRolling,
        PlanOperationType.Finishing => FiniteScheduleTaskType.Finishing,
        PlanOperationType.Eaf => FiniteScheduleTaskType.Eaf,
        PlanOperationType.Lrf => FiniteScheduleTaskType.Lrf,
        PlanOperationType.Vd => FiniteScheduleTaskType.Vd,
        PlanOperationType.Reheating => FiniteScheduleTaskType.Reheating,
        PlanOperationType.Tmt => FiniteScheduleTaskType.Tmt,
        PlanOperationType.Cooling => FiniteScheduleTaskType.Cooling,
        PlanOperationType.Cutting => FiniteScheduleTaskType.Cutting,
        PlanOperationType.Bundling => FiniteScheduleTaskType.Bundling,
        PlanOperationType.Coiling => FiniteScheduleTaskType.Coiling,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}
