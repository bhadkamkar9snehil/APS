using APS.Application;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed partial class PlannerWorkspaceQueryService
{
    public async Task<PlanComparisonWorkspaceView?> GetPlanComparisonAsync(
        Guid baselinePlanVersionId,
        Guid newPlanVersionId,
        CancellationToken cancellationToken = default)
    {
        var baseline = await BuildPlanContextAsync(baselinePlanVersionId, cancellationToken);
        var next = await BuildPlanContextAsync(newPlanVersionId, cancellationToken);
        if (baseline is null || next is null) return null;

        var difference = await new PlanComparisonService(db)
            .CompareAsync(baselinePlanVersionId, newPlanVersionId, cancellationToken);

        var resourceIds = difference.Differences
            .SelectMany(x => new[] { x.BaselineResourceId, x.NewResourceId })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        var resources = resourceIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.Resources.AsNoTracking()
                .Where(x => resourceIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);

        var changes = difference.Differences
            .OrderByDescending(x => x.ChangeType != PlanOperationChangeType.Unchanged)
            .ThenByDescending(x => Math.Abs(x.StartMovementMinutes))
            .ThenBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => new PlanOperationChangeView(
                x.PlanningKey,
                x.TaskType,
                x.ChangeType,
                ResourceCode(x.BaselineResourceId, resources),
                ResourceCode(x.NewResourceId, resources),
                x.BaselineStartUtc,
                x.NewStartUtc,
                x.BaselineEndUtc,
                x.NewEndUtc,
                x.StartMovementMinutes))
            .ToArray();

        return new PlanComparisonWorkspaceView(
            baseline,
            next,
            difference.AddedOperations,
            difference.RemovedOperations,
            difference.MovedOperations,
            difference.ResourceChangedOperations,
            difference.UnchangedOperations,
            difference.MaximumStartMovementMinutes,
            changes);
    }

    private static string? ResourceCode(Guid? resourceId, IReadOnlyDictionary<Guid, string> resources) =>
        resourceId.HasValue
            ? resources.TryGetValue(resourceId.Value, out var code) ? code : resourceId.Value.ToString("N")[..8]
            : null;
}
