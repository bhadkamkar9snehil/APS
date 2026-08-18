using APS.Application;

namespace APS.Planning;

/// <summary>
/// Backward-propagates latest service-feasible start times through the solved process DAG.
/// This is deliberately separate from the actual scheduled start: a supply receipt can make a
/// schedule mathematically feasible while still forcing the operation later than its service target.
/// </summary>
internal static class LatestMaterialNeedTimeProjector
{
    public static IReadOnlyDictionary<Guid, DateTime> Build(
        PlanningRunRequest request,
        ProductionStructurePlanningResult structure,
        FiniteScheduleResult schedule)
    {
        if (!schedule.IsFeasible) return new Dictionary<Guid, DateTime>();

        var taskById = structure.SchedulingTasks.ToDictionary(x => x.TaskId);
        var assignmentById = schedule.Assignments.ToDictionary(x => x.TaskId);
        var latestEnd = new Dictionary<Guid, DateTime>();

        foreach (var task in structure.SchedulingTasks)
        {
            var ownTarget = task.DueUtc ?? request.HorizonEndUtc;
            latestEnd[task.TaskId] = ownTarget < request.HorizonEndUtc ? ownTarget : request.HorizonEndUtc;
        }

        // Repeated relaxation is intentional: route graphs are small DAGs and this remains robust if
        // projection order changes. A predecessor's latest end is the earliest of its own service due
        // and every successor's latest start minus the selected physical transfer/queue lag.
        for (var iteration = 0; iteration < Math.Max(1, structure.SchedulingTasks.Count); iteration++)
        {
            var changed = false;
            foreach (var successor in structure.SchedulingTasks)
            {
                if (!assignmentById.TryGetValue(successor.TaskId, out var successorAssignment)) continue;
                var successorDuration = successorAssignment.EndUtc - successorAssignment.StartUtc;
                var successorLatestStart = latestEnd[successor.TaskId] - successorDuration;

                foreach (var dependency in successor.Dependencies)
                {
                    if (!taskById.ContainsKey(dependency.PredecessorTaskId) ||
                        !assignmentById.TryGetValue(dependency.PredecessorTaskId, out var predecessorAssignment))
                        continue;

                    var lagMinutes = ResolveMinimumLag(
                        dependency,
                        predecessorAssignment.ResourceId,
                        successorAssignment.ResourceId);
                    var candidate = successorLatestStart.AddMinutes(-lagMinutes);
                    if (candidate >= latestEnd[dependency.PredecessorTaskId]) continue;
                    latestEnd[dependency.PredecessorTaskId] = candidate;
                    changed = true;
                }
            }
            if (!changed) break;
        }

        return latestEnd.ToDictionary(pair =>
        {
            if (!assignmentById.TryGetValue(pair.Key, out var assignment)) return pair.Key;
            return pair.Key;
        }, pair =>
        {
            if (!assignmentById.TryGetValue(pair.Key, out var assignment)) return request.HorizonEndUtc;
            return pair.Value - (assignment.EndUtc - assignment.StartUtc);
        });
    }

    private static int ResolveMinimumLag(
        FiniteScheduleDependency dependency,
        Guid predecessorResourceId,
        Guid successorResourceId)
    {
        var pair = dependency.AllowedResourcePairs?.FirstOrDefault(x =>
            x.PredecessorResourceId == predecessorResourceId &&
            x.SuccessorResourceId == successorResourceId);
        return Math.Max(0, pair?.MinimumLagMinutes ?? dependency.MinimumLagMinutes);
    }
}
