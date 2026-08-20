using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class ThermalConstraintProjector
{
    public static ProductionStructurePlanningResult Apply(
        ProductionStructurePlanningResult structure,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyCollection<PlantFlowLink> flowLinks,
        IReadOnlyCollection<GradeProcessTemperatureRequirement>? gradeTemperatureRequirements,
        IReadOnlyCollection<ResourceTemperatureCapability>? resourceTemperatureCapabilities,
        IReadOnlyCollection<CampaignHeatAllocation>? heatAllocations,
        RoutePlanningInput? routePlanning = null)
    {
        var temperatures = gradeTemperatureRequirements ?? Array.Empty<GradeProcessTemperatureRequirement>();
        var resourceThermal = resourceTemperatureCapabilities ?? Array.Empty<ResourceTemperatureCapability>();
        if (temperatures.Count == 0 && resourceThermal.Count == 0) return structure;

        var liquidSteelOperations = LiquidSteelOperations(routePlanning);

        var tasks = structure.SchedulingTasks.ToDictionary(x => x.TaskId);
        var issues = structure.Issues.ToList();
        var resourcesById = resources.ToDictionary(x => x.Id);
        var allocationsByHeat = (heatAllocations ?? Array.Empty<CampaignHeatAllocation>())
            .GroupBy(x => x.CampaignHeatId)
            .ToDictionary(x => x.Key, x => x.Where(y => y.ProductionOrder is not null).Select(y => y.ProductionOrder!).DistinctBy(y => y.Id).ToArray());
        var gradeTempByKey = temperatures
            .GroupBy(x => (x.SteelGradeId, x.ProcessOperationType))
            .ToDictionary(x => x.Key, x => x.First());
        var resourceTempByKey = resourceThermal
            .GroupBy(x => (x.ResourceId, x.ProcessOperationType))
            .ToDictionary(x => x.Key, x => x.First());

        foreach (var task in tasks.Values.Where(x => x.Dependencies.Count > 0 && liquidSteelOperations.Contains(x.ProcessOperationType)).ToArray())
        {
            if (!allocationsByHeat.TryGetValue(task.SourceEntityId, out var orders) || orders.Length == 0) continue;
            var grade = orders.Select(x => x.SteelGrade).FirstOrDefault(x => x is not null);
            if (grade is null) continue;

            var updatedDependencies = new List<FiniteScheduleDependency>();
            foreach (var dependency in task.Dependencies)
            {
                if (!tasks.TryGetValue(dependency.PredecessorTaskId, out var predecessor) ||
                    !liquidSteelOperations.Contains(predecessor.ProcessOperationType))
                {
                    updatedDependencies.Add(dependency);
                    continue;
                }

                var pairs = BuildPairs(
                    predecessor,
                    task,
                    dependency,
                    grade,
                    orders,
                    resourcesById,
                    flowLinks,
                    gradeTempByKey,
                    resourceTempByKey);

                if (pairs.Count == 0)
                {
                    issues.Add(new PlanningIssue(
                        PlanningIssueSeverity.Error,
                        "THERMAL_ROUTE_INFEASIBLE",
                        $"No {predecessor.ProcessOperationType}->{task.ProcessOperationType} resource pair can keep heat {task.SourceEntityId} inside its required temperature/superheat window.",
                        task.SourceEntityId));
                    updatedDependencies.Add(dependency with { AllowedResourcePairs = Array.Empty<FiniteScheduleDependencyResourcePair>() });
                    continue;
                }

                updatedDependencies.Add(dependency with
                {
                    MinimumLagMinutes = 0,
                    MaximumLagMinutes = null,
                    AllowedResourcePairs = pairs
                });
            }

            tasks[task.TaskId] = task with { Dependencies = updatedDependencies };
        }

        return structure with { SchedulingTasks = tasks.Values.ToArray(), Issues = issues };
    }

    private static IReadOnlyCollection<FiniteScheduleDependencyResourcePair> BuildPairs(
        FiniteScheduleTask predecessor,
        FiniteScheduleTask successor,
        FiniteScheduleDependency dependency,
        SteelGrade grade,
        IReadOnlyCollection<ProductionOrder> orders,
        IReadOnlyDictionary<Guid, Resource> resources,
        IReadOnlyCollection<PlantFlowLink> flowLinks,
        IReadOnlyDictionary<(Guid SteelGradeId, ProcessOperationType ProcessOperationType), GradeProcessTemperatureRequirement> gradeTemperature,
        IReadOnlyDictionary<(Guid ResourceId, ProcessOperationType ProcessOperationType), ResourceTemperatureCapability> resourceTemperature)
    {
        var existingPairs = dependency.AllowedResourcePairs?.Count > 0
            ? dependency.AllowedResourcePairs
            : predecessor.ResourceOptions.SelectMany(from => successor.ResourceOptions.Select(to =>
                new FiniteScheduleDependencyResourcePair(from.ResourceId, to.ResourceId, dependency.MinimumLagMinutes, dependency.MaximumLagMinutes))).ToArray();

        var result = new List<FiniteScheduleDependencyResourcePair>();
        foreach (var pair in existingPairs)
        {
            if (!resources.ContainsKey(pair.PredecessorResourceId) || !resources.ContainsKey(pair.SuccessorResourceId)) continue;
            var link = flowLinks.FirstOrDefault(x =>
                x.IsEnabled &&
                x.FromResourceId == pair.PredecessorResourceId &&
                x.ToResourceId == pair.SuccessorResourceId);
            if (link is null) continue;

            var downstreamRange = RequiredEntryRange(grade, orders, successor.ProcessOperationType, gradeTemperature);
            var upstreamRange = AchievableExitRange(grade, predecessor.ProcessOperationType, pair.PredecessorResourceId, gradeTemperature, resourceTemperature);

            var minLag = Math.Max(pair.MinimumLagMinutes, Minutes(link.MinimumTransferTime));
            var maxLag = MinNullable(pair.MaximumLagMinutes, link.MaximumTransferTime.HasValue ? Minutes(link.MaximumTransferTime.Value) : null);
            if (downstreamRange.Minimum.HasValue || downstreamRange.Maximum.HasValue)
            {
                if (!upstreamRange.Minimum.HasValue && !upstreamRange.Maximum.HasValue)
                {
                    // Temperature-constrained grade with no upstream thermal master cannot be proven feasible.
                    continue;
                }

                var lossPerMinute = Math.Max(
                    0m,
                    (link.NominalTemperatureLossCPerMinute ?? 0m) +
                    (resourceTemperature.TryGetValue((pair.PredecessorResourceId, predecessor.ProcessOperationType), out var cap)
                        ? cap.NominalTemperatureLossCPerMinuteWhileHolding ?? 0m
                        : 0m));

                if (downstreamRange.Minimum.HasValue)
                {
                    var sourceMaximum = upstreamRange.Maximum ?? upstreamRange.Target ?? upstreamRange.Minimum;
                    if (!sourceMaximum.HasValue) continue;
                    if (lossPerMinute <= 0m)
                    {
                        if (sourceMaximum.Value < downstreamRange.Minimum.Value) continue;
                    }
                    else
                    {
                        var thermalMaxLag = (int)Math.Floor((double)((sourceMaximum.Value - downstreamRange.Minimum.Value) / lossPerMinute));
                        if (thermalMaxLag < 0) continue;
                        maxLag = MinNullable(maxLag, thermalMaxLag);
                    }
                }

                if (downstreamRange.Maximum.HasValue)
                {
                    var sourceMinimum = upstreamRange.Minimum ?? upstreamRange.Target ?? upstreamRange.Maximum;
                    if (sourceMinimum.HasValue && sourceMinimum.Value > downstreamRange.Maximum.Value)
                    {
                        if (lossPerMinute <= 0m) continue;
                        var thermalMinLag = (int)Math.Ceiling((double)((sourceMinimum.Value - downstreamRange.Maximum.Value) / lossPerMinute));
                        minLag = Math.Max(minLag, thermalMinLag);
                    }
                }
            }

            if (maxLag.HasValue && maxLag.Value < minLag) continue;
            result.Add(pair with { MinimumLagMinutes = minLag, MaximumLagMinutes = maxLag });
        }

        return result;
    }

    private static TemperatureRange RequiredEntryRange(
        SteelGrade grade,
        IReadOnlyCollection<ProductionOrder> orders,
        ProcessOperationType operation,
        IReadOnlyDictionary<(Guid SteelGradeId, ProcessOperationType ProcessOperationType), GradeProcessTemperatureRequirement> gradeTemperature)
    {
        gradeTemperature.TryGetValue((grade.Id, operation), out var process);
        decimal? minimum = process?.MinimumEntryTemperatureC;
        decimal? target = process?.TargetEntryTemperatureC;
        decimal? maximum = process?.MaximumEntryTemperatureC;

        if (operation == ProcessOperationType.Ccm)
        {
            minimum = Max(minimum, grade.MinimumCastingTemperatureC);
            maximum = Min(maximum, grade.MaximumCastingTemperatureC);
            target ??= grade.TargetCastingTemperatureC;

            if (grade.LiquidusTemperatureC.HasValue)
            {
                minimum = Max(minimum, Add(grade.LiquidusTemperatureC, grade.MinimumSuperheatC));
                maximum = Min(maximum, Add(grade.LiquidusTemperatureC, grade.MaximumSuperheatC));
                target ??= Add(grade.LiquidusTemperatureC, grade.TargetSuperheatC);

                foreach (var order in orders)
                {
                    minimum = Max(minimum, Add(grade.LiquidusTemperatureC, order.Requirement?.MinimumSuperheatC));
                    maximum = Min(maximum, Add(grade.LiquidusTemperatureC, order.Requirement?.MaximumSuperheatC));
                    target = order.Requirement?.TargetSuperheatC.HasValue == true
                        ? Add(grade.LiquidusTemperatureC, order.Requirement.TargetSuperheatC)
                        : target;
                    minimum = Max(minimum, order.Requirement?.MinimumCastingTemperatureC);
                    maximum = Min(maximum, order.Requirement?.MaximumCastingTemperatureC);
                }
            }
        }

        return new TemperatureRange(minimum, target, maximum);
    }

    private static TemperatureRange AchievableExitRange(
        SteelGrade grade,
        ProcessOperationType operation,
        Guid resourceId,
        IReadOnlyDictionary<(Guid SteelGradeId, ProcessOperationType ProcessOperationType), GradeProcessTemperatureRequirement> gradeTemperature,
        IReadOnlyDictionary<(Guid ResourceId, ProcessOperationType ProcessOperationType), ResourceTemperatureCapability> resourceTemperature)
    {
        gradeTemperature.TryGetValue((grade.Id, operation), out var gradeProcess);
        resourceTemperature.TryGetValue((resourceId, operation), out var resource);

        var minimum = Max(gradeProcess?.MinimumExitTemperatureC, resource?.MinimumAchievableExitTemperatureC);
        var maximum = Min(gradeProcess?.MaximumExitTemperatureC, resource?.MaximumAchievableExitTemperatureC);
        var target = resource?.NominalExitTemperatureC ?? gradeProcess?.TargetExitTemperatureC;

        if (minimum.HasValue && maximum.HasValue && minimum > maximum)
            return new TemperatureRange(null, null, null);
        return new TemperatureRange(minimum, target, maximum);
    }

    /// <summary>
    /// Steel is liquid from the primary vessel until it leaves the caster, so whatever a route places
    /// at or before CCM is where thermal constraints apply - a plant may configure BOF, AOD/VOD, an
    /// induction furnace, RH, or several secondary-metallurgy passes, and the route master decides
    /// what exists rather than a fixed type whitelist in code (#34).
    /// The Eaf/Lrf/Vd/Ccm set is only the fallback for compatibility callers that supply no route.
    /// </summary>
    private static IReadOnlySet<ProcessOperationType> LiquidSteelOperations(RoutePlanningInput? routePlanning)
    {
        var operations = routePlanning?.Operations ?? Array.Empty<ManufacturingRouteOperation>();
        if (operations.Count == 0) return DefaultLiquidSteelOperations;

        var result = new HashSet<ProcessOperationType>();
        foreach (var route in operations.GroupBy(x => x.RouteCode, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = route.OrderBy(x => x.SequenceNumber).ToArray();
            var casterSequence = ordered.FirstOrDefault(x => x.ProcessOperationType == ProcessOperationType.Ccm)?.SequenceNumber;
            // A route with no caster never carries liquid steel - an existing-billet rolling route, say.
            if (casterSequence is null) continue;

            foreach (var operation in ordered.Where(x => x.SequenceNumber <= casterSequence.Value))
            {
                result.Add(operation.ProcessOperationType);
            }
        }

        return result.Count == 0 ? DefaultLiquidSteelOperations : result;
    }

    private static readonly IReadOnlySet<ProcessOperationType> DefaultLiquidSteelOperations =
        new HashSet<ProcessOperationType>
        {
            ProcessOperationType.Eaf,
            ProcessOperationType.Lrf,
            ProcessOperationType.Vd,
            ProcessOperationType.Ccm
        };

    private static decimal? Add(decimal? a, decimal? b) => a.HasValue && b.HasValue ? a.Value + b.Value : null;
    private static decimal? Max(decimal? a, decimal? b) => a.HasValue && b.HasValue ? Math.Max(a.Value, b.Value) : a ?? b;
    private static decimal? Min(decimal? a, decimal? b) => a.HasValue && b.HasValue ? Math.Min(a.Value, b.Value) : a ?? b;
    private static int? MinNullable(int? a, int? b) => a.HasValue && b.HasValue ? Math.Min(a.Value, b.Value) : a ?? b;
    private static int Minutes(TimeSpan value) => Math.Max(0, (int)Math.Ceiling(value.TotalMinutes));
    private sealed record TemperatureRange(decimal? Minimum, decimal? Target, decimal? Maximum);
}
