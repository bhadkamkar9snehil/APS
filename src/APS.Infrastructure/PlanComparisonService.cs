using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class PlanComparisonService(ApsDbContext db) : IPlanComparisonService
{
    public async Task<PlanVersionDifference> CompareAsync(
        Guid baselinePlanVersionId,
        Guid newPlanVersionId,
        CancellationToken cancellationToken = default)
    {
        var knownVersions = await db.PlanVersions
            .AsNoTracking()
            .Where(x => x.Id == baselinePlanVersionId || x.Id == newPlanVersionId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (!knownVersions.Contains(baselinePlanVersionId))
            throw new KeyNotFoundException("Baseline plan version was not found.");
        if (!knownVersions.Contains(newPlanVersionId))
            throw new KeyNotFoundException("New plan version was not found.");

        var rows = await db.PlanOperationSnapshots
            .AsNoTracking()
            .Where(x => x.PlanVersionId == baselinePlanVersionId || x.PlanVersionId == newPlanVersionId)
            .ToListAsync(cancellationToken);

        var baseline = rows
            .Where(x => x.PlanVersionId == baselinePlanVersionId)
            .ToDictionary(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase);
        var current = rows
            .Where(x => x.PlanVersionId == newPlanVersionId)
            .ToDictionary(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase);
        var keys = baseline.Keys
            .Concat(current.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var differences = new List<PlanOperationDifference>();
        foreach (var key in keys)
        {
            baseline.TryGetValue(key, out var oldOperation);
            current.TryGetValue(key, out var newOperation);

            if (oldOperation is null)
            {
                differences.Add(new PlanOperationDifference(
                    key,
                    MapTaskType(newOperation!.OperationType),
                    PlanOperationChangeType.Added,
                    null,
                    newOperation.ResourceId,
                    null,
                    newOperation.StartUtc,
                    null,
                    newOperation.EndUtc,
                    0));
                continue;
            }

            if (newOperation is null)
            {
                differences.Add(new PlanOperationDifference(
                    key,
                    MapTaskType(oldOperation.OperationType),
                    PlanOperationChangeType.Removed,
                    oldOperation.ResourceId,
                    null,
                    oldOperation.StartUtc,
                    null,
                    oldOperation.EndUtc,
                    null,
                    0));
                continue;
            }

            var movement = (int)Math.Round(
                (newOperation.StartUtc - oldOperation.StartUtc).TotalMinutes,
                MidpointRounding.AwayFromZero);
            var moved = movement != 0 || newOperation.EndUtc != oldOperation.EndUtc;
            var resourceChanged = newOperation.ResourceId != oldOperation.ResourceId;
            var change = (moved, resourceChanged) switch
            {
                (true, true) => PlanOperationChangeType.MovedAndResourceChanged,
                (true, false) => PlanOperationChangeType.Moved,
                (false, true) => PlanOperationChangeType.ResourceChanged,
                _ => PlanOperationChangeType.Unchanged
            };

            differences.Add(new PlanOperationDifference(
                key,
                MapTaskType(newOperation.OperationType),
                change,
                oldOperation.ResourceId,
                newOperation.ResourceId,
                oldOperation.StartUtc,
                newOperation.StartUtc,
                oldOperation.EndUtc,
                newOperation.EndUtc,
                movement));
        }

        return new PlanVersionDifference(
            baselinePlanVersionId,
            newPlanVersionId,
            differences.Count(x => x.ChangeType == PlanOperationChangeType.Added),
            differences.Count(x => x.ChangeType == PlanOperationChangeType.Removed),
            differences.Count(x => x.ChangeType is PlanOperationChangeType.Moved or PlanOperationChangeType.MovedAndResourceChanged),
            differences.Count(x => x.ChangeType is PlanOperationChangeType.ResourceChanged or PlanOperationChangeType.MovedAndResourceChanged),
            differences.Count(x => x.ChangeType == PlanOperationChangeType.Unchanged),
            differences.Select(x => Math.Abs(x.StartMovementMinutes)).DefaultIfEmpty(0).Max(),
            differences);
    }

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
