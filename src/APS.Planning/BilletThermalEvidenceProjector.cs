using APS.Application;
using APS.Domain;

namespace APS.Planning;

/// <summary>
/// Resolves the billet thermal rule against the resources and timestamps selected by CP-SAT. Route
/// projection owns topology; this projector owns the auditable material-state consequence.
/// </summary>
internal static class BilletThermalEvidenceProjector
{
    public static ProductionStructurePlanningResult Apply(
        ProductionStructurePlanningResult structure,
        FiniteScheduleResult schedule,
        IReadOnlyCollection<PlantFlowLink> flowLinks,
        IReadOnlyCollection<GradeProcessTemperatureRequirement>? gradeTemperatureRequirements,
        IReadOnlyCollection<ResourceTemperatureCapability>? resourceTemperatureCapabilities)
    {
        if (!schedule.IsFeasible) return structure;

        var tasks = structure.SchedulingTasks.ToDictionary(x => x.TaskId);
        var assignments = schedule.Assignments.ToDictionary(x => x.TaskId);
        var routePlans = (structure.RouteOperationPlans ?? Array.Empty<RouteOperationPlan>())
            .ToDictionary(x => x.Id);
        var rollingIds = structure.RollingPlans.Select(x => x.Id).ToHashSet();
        var gradeThermal = (gradeTemperatureRequirements ?? Array.Empty<GradeProcessTemperatureRequirement>())
            .GroupBy(x => (x.SteelGradeId, x.ProcessOperationType))
            .ToDictionary(x => x.Key, x => x.First());
        var resourceThermal = (resourceTemperatureCapabilities ?? Array.Empty<ResourceTemperatureCapability>())
            .GroupBy(x => (x.ResourceId, x.ProcessOperationType))
            .ToDictionary(x => x.Key, x => x.First());
        var initialByTask = (structure.BilletThermalDecisions ?? Array.Empty<BilletThermalDecision>())
            .GroupBy(x => x.HotRollTaskId)
            .ToDictionary(x => x.Key, x => x.Last());
        var decisions = new List<BilletThermalDecision>();
        var resolvedTaskIds = new HashSet<Guid>();

        foreach (var hotRoll in tasks.Values.Where(x => x.ProcessOperationType == ProcessOperationType.HotRoll))
        {
            if (!routePlans.TryGetValue(hotRoll.SourceEntityId, out var routePlan) ||
                !assignments.TryGetValue(hotRoll.TaskId, out var hotRollAssignment))
                continue;

            var dependency = hotRoll.Dependencies.SingleOrDefault();
            if (dependency is null ||
                !tasks.TryGetValue(dependency.PredecessorTaskId, out var sourceTask) ||
                !assignments.TryGetValue(sourceTask.TaskId, out var sourceAssignment))
                continue;

            var order = routePlan.Allocations
                .Select(x => x.ProductionOrder)
                .FirstOrDefault(x => x?.SteelGrade is not null);
            var grade = order?.SteelGrade;
            gradeThermal.TryGetValue((grade?.Id ?? Guid.Empty, ProcessOperationType.HotRoll), out var rollingRequirement);
            gradeThermal.TryGetValue((grade?.Id ?? Guid.Empty, sourceTask.ProcessOperationType), out var sourceRequirement);
            resourceThermal.TryGetValue((sourceAssignment.ResourceId, sourceTask.ProcessOperationType), out var sourceCapability);

            var sourceTemperature = sourceCapability?.NominalExitTemperatureC
                                    ?? sourceRequirement?.TargetExitTemperatureC
                                    ?? sourceCapability?.MaximumAchievableExitTemperatureC
                                    ?? sourceRequirement?.MaximumExitTemperatureC;
            var waitMinutes = Math.Max(
                0,
                (int)Math.Ceiling((hotRollAssignment.StartUtc - sourceAssignment.EndUtc).TotalMinutes));
            var link = flowLinks.FirstOrDefault(x =>
                x.IsEnabled &&
                x.FromResourceId == sourceAssignment.ResourceId &&
                x.ToResourceId == hotRollAssignment.ResourceId);
            var lossPerMinute = Math.Max(
                0m,
                (link?.NominalTemperatureLossCPerMinute ?? 0m) +
                (sourceCapability?.NominalTemperatureLossCPerMinuteWhileHolding ?? 0m));
            decimal? predicted = sourceTemperature.HasValue
                ? sourceTemperature.Value - waitMinutes * lossPerMinute
                : null;
            var selectedPair = dependency.AllowedResourcePairs?.FirstOrDefault(x =>
                x.PredecessorResourceId == sourceAssignment.ResourceId &&
                x.SuccessorResourceId == hotRollAssignment.ResourceId);
            var warnings = new List<string>();
            if (!sourceTemperature.HasValue) warnings.Add("SOURCE_TEMPERATURE_NOT_NUMERIC");
            if (rollingRequirement?.MinimumEntryTemperatureC.HasValue != true)
                warnings.Add("ROLLING_ENTRY_MINIMUM_NOT_CONFIGURED");

            var wasReheated = sourceTask.ProcessOperationType == ProcessOperationType.Reheat;
            var resolved = new BilletThermalDecision(
                ResolveRollingPlanId(routePlan, routePlans, rollingIds),
                hotRoll.TaskId,
                sourceTask.TaskId,
                routePlan.RouteCode,
                hotRoll.GradeCode,
                hotRoll.CrossSectionCode,
                wasReheated ? BilletThermalSourceBasis.Reheated : BilletThermalSourceBasis.PlannedCcm,
                sourceTemperature,
                sourceAssignment.EndUtc,
                rollingRequirement?.MinimumEntryTemperatureC,
                predicted,
                waitMinutes,
                lossPerMinute,
                selectedPair?.MaximumLagMinutes ?? dependency.MaximumLagMinutes,
                wasReheated ? BilletThermalOutcome.Reheated : BilletThermalOutcome.HotDirect,
                wasReheated ? "REHEAT_EXIT_TEMPERATURE" : "ROLLING_ENTRY_TEMPERATURE_PROVEN",
                link is null ? "SCHEDULED_PREDECESSOR_WAIT" : "PHYSICAL_LINK_PLUS_SCHEDULED_WAIT",
                false,
                wasReheated,
                Array.Empty<string>(),
                warnings);

            if (wasReheated && initialByTask.TryGetValue(hotRoll.TaskId, out var initial))
            {
                resolved = resolved with
                {
                    SourceBasis = initial.SourceBasis,
                    SourceTemperatureC = initial.SourceTemperatureC,
                    SourceTemperatureAtUtc = initial.SourceTemperatureAtUtc,
                    MinimumRollingEntryTemperatureC = initial.MinimumRollingEntryTemperatureC ?? resolved.MinimumRollingEntryTemperatureC,
                    ReasonCode = initial.ReasonCode,
                    ReheatRequiredByThermalState = initial.ReheatRequiredByThermalState,
                    ReheatRequiredByPolicy = initial.ReheatRequiredByPolicy,
                    RejectedHotPaths = initial.RejectedHotPaths,
                    Warnings = initial.Warnings.Concat(resolved.Warnings).Distinct().ToArray()
                };
            }
            decisions.Add(resolved);
            resolvedTaskIds.Add(hotRoll.TaskId);
        }

        decisions.AddRange(initialByTask.Values.Where(x => !resolvedTaskIds.Contains(x.HotRollTaskId)));

        return structure with { BilletThermalDecisions = decisions };
    }

    private static Guid ResolveRollingPlanId(
        RouteOperationPlan routePlan,
        IReadOnlyDictionary<Guid, RouteOperationPlan> routePlans,
        IReadOnlySet<Guid> rollingIds)
    {
        var upstreamId = routePlan.UpstreamPlanId;
        while (routePlans.TryGetValue(upstreamId, out var upstream))
            upstreamId = upstream.UpstreamPlanId;
        return rollingIds.Contains(upstreamId) ? upstreamId : Guid.Empty;
    }
}
