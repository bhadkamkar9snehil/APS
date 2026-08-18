using APS.Domain;

namespace APS.Application;

/// <summary>
/// Read-only material-requirement node. The embedded fact is the persisted Plan-Version truth;
/// children are a projection only and do not duplicate business arithmetic.
/// </summary>
public sealed record MaterialRequirementTreeNode(
    MaterialRequirement Requirement,
    IReadOnlyCollection<MaterialRequirementTreeNode> Children);

public sealed record MaterialRequirementPlanView(
    Guid PlanVersionId,
    IReadOnlyCollection<MaterialRequirement> Flattened,
    IReadOnlyCollection<MaterialRequirementTreeNode> Roots)
{
    public int RequirementCount => Flattened.Count;
    public int RootCount => Roots.Count;
}

public static class MaterialRequirementReadModelBuilder
{
    public static MaterialRequirementPlanView Build(
        Guid planVersionId,
        IReadOnlyCollection<MaterialRequirement>? requirements)
    {
        var flat = (requirements ?? Array.Empty<MaterialRequirement>())
            .OrderBy(x => x.ProductionOrderId)
            .ThenBy(x => Depth(x.RequirementPath))
            .ThenBy(x => x.RequiredAtUtc)
            .ThenBy(x => x.RequirementKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (flat.Length == 0)
            return new MaterialRequirementPlanView(planVersionId, flat, Array.Empty<MaterialRequirementTreeNode>());

        var byId = flat.ToDictionary(x => x.Id);
        var children = flat
            .Where(x => x.ParentRequirementId.HasValue && byId.ContainsKey(x.ParentRequirementId.Value))
            .GroupBy(x => x.ParentRequirementId!.Value)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(y => y.RequiredAtUtc)
                    .ThenBy(y => y.RequirementKey, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        MaterialRequirementTreeNode Project(MaterialRequirement requirement)
        {
            var projectedChildren = children.TryGetValue(requirement.Id, out var rows)
                ? rows.Select(Project).ToArray()
                : Array.Empty<MaterialRequirementTreeNode>();
            return new MaterialRequirementTreeNode(requirement, projectedChildren);
        }

        // Orphans are promoted to roots rather than dropped. This keeps corrupt/historical snapshots inspectable.
        var roots = flat
            .Where(x => !x.ParentRequirementId.HasValue || !byId.ContainsKey(x.ParentRequirementId.Value))
            .OrderBy(x => x.ProductionOrderId)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.RequiredAtUtc)
            .ThenBy(x => x.RequirementKey, StringComparer.OrdinalIgnoreCase)
            .Select(Project)
            .ToArray();

        return new MaterialRequirementPlanView(planVersionId, flat, roots);
    }

    private static int Depth(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? 0
            : path.Split(" -> ", StringSplitOptions.RemoveEmptyEntries).Length;
}
