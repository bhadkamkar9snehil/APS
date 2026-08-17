using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class FiniteScheduleTaskSequencer
{
    public static IReadOnlyCollection<FiniteScheduleTask> Apply(
        IReadOnlyCollection<FiniteScheduleTask> tasks,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyCollection<TransitionRule> transitionRules)
    {
        var ordered = tasks.ToList();
        var byId = ordered.ToDictionary(t => t.TaskId);
        var resourceById = resources.ToDictionary(r => r.Id);

        var fixedResourceTasks = ordered
            .Select((task, index) => new { Task = task, Index = index })
            .Where(x => x.Task.ResourceOptions.Count == 1)
            .GroupBy(x => new
            {
                ResourceId = x.Task.ResourceOptions.Single().ResourceId,
                x.Task.TaskType
            });

        foreach (var group in fixedResourceTasks)
        {
            if (!resourceById.TryGetValue(group.Key.ResourceId, out var resource)) continue;

            var sequence = group.OrderBy(x => x.Index).Select(x => x.Task).ToArray();
            for (var i = 1; i < sequence.Length; i++)
            {
                var previous = sequence[i - 1];
                var current = sequence[i];

                if (current.SourceEntityId == previous.SourceEntityId)
                {
                    // Feed-block siblings split from the same upstream plan/route-operation already
                    // carry their own dependency to their respective predecessor task, and their order
                    // relative to each other is not meaningful. Chaining them by list-insertion order
                    // would impose an unrelated, incorrect-lag edge alongside their real dependency.
                    continue;
                }

                var setupMinutes = RequiredTransitionMinutes(previous, current, resource, transitionRules);

                var dependencies = current.Dependencies.ToList();
                var existingIndex = dependencies.FindIndex(d => d.PredecessorTaskId == previous.TaskId);
                if (existingIndex >= 0)
                {
                    var existing = dependencies[existingIndex];
                    dependencies[existingIndex] = existing with
                    {
                        MinimumLagMinutes = Math.Max(existing.MinimumLagMinutes, setupMinutes)
                    };
                }
                else
                {
                    dependencies.Add(new FiniteScheduleDependency(previous.TaskId, setupMinutes));
                }

                byId[current.TaskId] = current with { Dependencies = dependencies };
            }
        }

        return ordered.Select(task => byId[task.TaskId]).ToArray();
    }

    private static int RequiredTransitionMinutes(
        FiniteScheduleTask previous,
        FiniteScheduleTask current,
        Resource resource,
        IReadOnlyCollection<TransitionRule> rules)
    {
        var grade = FindRule(rules, resource, TransitionDimension.Grade, previous.GradeCode, current.GradeCode);
        var section = FindRule(rules, resource, TransitionDimension.CrossSection, previous.CrossSectionCode, current.CrossSectionCode);

        return new[] { grade, section }
            .Where(rule => rule is not null && rule.IsAllowed)
            .Select(rule => Minutes(rule!.TransitionTime))
            .DefaultIfEmpty(0)
            .Max();
    }

    private static TransitionRule? FindRule(
        IReadOnlyCollection<TransitionRule> rules,
        Resource resource,
        TransitionDimension dimension,
        string from,
        string to) =>
        rules
            .Where(rule =>
                rule.Dimension == dimension &&
                string.Equals(rule.FromCode, from, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rule.ToCode, to, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(rule => rule.ResourceId == resource.Id)
            .ThenByDescending(rule => rule.ResourceType == resource.ResourceType)
            .FirstOrDefault(rule =>
                rule.ResourceId == resource.Id ||
                rule.ResourceType == resource.ResourceType ||
                (!rule.ResourceId.HasValue && !rule.ResourceType.HasValue));

    private static int Minutes(TimeSpan value) => Math.Max(0, (int)Math.Ceiling(value.TotalMinutes));
}
