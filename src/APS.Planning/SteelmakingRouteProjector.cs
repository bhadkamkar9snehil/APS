using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class SteelmakingRouteProjector
{
    public static ProductionStructurePlanningResult Apply(
        ProductionStructurePlanningResult structure,
        RoutePlanningInput routePlanning,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyCollection<ResourceCapability> capabilities,
        IReadOnlyCollection<PlantFlowLink> flowLinks,
        IReadOnlyCollection<SteelGrade>? steelGrades,
        IReadOnlyCollection<CampaignHeatAllocation>? heatAllocations)
    {
        var issues = structure.Issues.ToList();
        var tasks = structure.SchedulingTasks.ToList();
        var resourceCapabilities = capabilities.GroupBy(x => x.ResourceId).ToDictionary(x => x.Key, x => x.ToArray());
        var routeCapabilities = routePlanning.ResourceCapabilities.GroupBy(x => x.ResourceId).ToDictionary(x => x.Key, x => x.ToArray());
        var routes = routePlanning.Operations.GroupBy(x => x.RouteCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.SequenceNumber).ToArray(), StringComparer.OrdinalIgnoreCase);
        var grades = (steelGrades ?? Array.Empty<SteelGrade>()).ToDictionary(x => x.GradeCode, StringComparer.OrdinalIgnoreCase);
        var allocationsByHeat = (heatAllocations ?? Array.Empty<CampaignHeatAllocation>())
            .GroupBy(x => x.CampaignHeatId)
            .ToDictionary(x => x.Key, x => x.ToArray());

        foreach (var sequence in structure.CastSequences)
        {
            foreach (var sequenceHeat in sequence.Heats.OrderBy(x => x.Position))
            {
                var heat = sequenceHeat.CampaignHeat;
                var campaign = heat.Campaign;
                if (campaign is null || !routes.TryGetValue(campaign.RouteCode, out var route)) continue;

                var ccmIndex = Array.FindIndex(route, x => x.ProcessOperationType == ProcessOperationType.Ccm);
                if (ccmIndex < 0) continue;

                var castingTaskIndex = tasks.FindIndex(x => x.SourceEntityId == heat.Id && x.TaskType == FiniteScheduleTaskType.Casting);
                if (castingTaskIndex < 0)
                {
                    issues.Add(new PlanningIssue(PlanningIssueSeverity.Error, "CCM_TASK_MISSING", $"Heat {campaign.CampaignNumber}/{heat.SequenceNumber:00} has no CCM scheduling task.", heat.Id));
                    continue;
                }

                var heatOrders = allocationsByHeat.TryGetValue(heat.Id, out var exactAllocations)
                    ? exactAllocations.Where(x => x.ProductionOrder is not null).Select(x => x.ProductionOrder!).DistinctBy(x => x.Id).ToArray()
                    : campaign.Allocations
                        .Where(x => x.ProductionOrder is not null && x.FreshSteelQuantityMt > 0m && string.Equals(x.ProductionOrder.GradeCode, heat.GradeCode, StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.ProductionOrder!).DistinctBy(x => x.Id).ToArray();

                if (heatOrders.Length == 0)
                {
                    issues.Add(new PlanningIssue(PlanningIssueSeverity.Error, "HEAT_WITHOUT_DEMAND_PEGGING", $"Heat {campaign.CampaignNumber}/{heat.SequenceNumber:00} is not pegged to any Production Order.", heat.Id));
                    continue;
                }

                var grade = heatOrders.Select(x => x.SteelGrade).FirstOrDefault(x => x is not null)
                            ?? (grades.TryGetValue(heat.GradeCode, out var found) ? found : null);

                var vdRequirement = ResolveRequirement(heatOrders, grade, ProcessOperationType.Vd, issues, heat.Id);
                if (vdRequirement == RequirementResolution.Conflict) continue;
                if (vdRequirement == RequirementResolution.Required && !route.Any(x => x.ProcessOperationType == ProcessOperationType.Vd))
                {
                    issues.Add(new PlanningIssue(PlanningIssueSeverity.Error, "VD_REQUIRED_ROUTE_MISSING", $"Heat {campaign.CampaignNumber}/{heat.SequenceNumber:00} requires VD but route {campaign.RouteCode} contains no VD operation.", heat.Id));
                    continue;
                }

                var due = heatOrders.Select(x => x.RequiredDate).DefaultIfEmpty(campaign.RequiredDate).Min();
                var priority = heatOrders.Select(x => x.Priority).DefaultIfEmpty(0).Max();
                FiniteScheduleTask? predecessor = null;

                // Every operation the route places before CCM is a candidate steelmaking step - not just
                // Eaf/Lrf/Vd. A plant may configure BOF, AOD/VOD, induction furnace, RH, or any number of
                // secondary-metallurgy passes; the route master decides what exists, not this switch (#34).
                foreach (var operation in route.Take(ccmIndex))
                {
                    var effective = ResolveRequirement(heatOrders, grade, operation.ProcessOperationType, issues, heat.Id);
                    if (effective == RequirementResolution.Conflict) break;
                    if (effective == RequirementResolution.Forbidden)
                    {
                        if (operation.Requirement == RequirementDisposition.Required)
                            issues.Add(new PlanningIssue(PlanningIssueSeverity.Error, "REQUIRED_PROCESS_FORBIDDEN", $"Route {campaign.RouteCode} requires {operation.ProcessOperationType} but grade/order requirements forbid it for heat {campaign.CampaignNumber}/{heat.SequenceNumber:00}.", heat.Id));
                        continue;
                    }
                    if (operation.Requirement == RequirementDisposition.Optional && effective != RequirementResolution.Required) continue;

                    var options = BuildResourceOptions(operation, heat, campaign, heatOrders, grade, resources, resourceCapabilities, routeCapabilities);
                    if (options.Count == 0)
                    {
                        issues.Add(new PlanningIssue(PlanningIssueSeverity.Error, "HEAT_PROCESS_RESOURCE_MISSING", $"No eligible physical resource can perform {operation.ProcessOperationType} for heat {campaign.CampaignNumber}/{heat.SequenceNumber:00} ({heat.GradeCode}, {heat.PlannedQuantityMt:0.####} MT).", heat.Id));
                        break;
                    }

                    var dependencies = predecessor is null
                        ? Array.Empty<FiniteScheduleDependency>()
                        : new[] { BuildDependency(predecessor, options, operation, flowLinks, grade, heatOrders, issues, heat.Id) };

                    var task = new FiniteScheduleTask(
                        Guid.NewGuid(), heat.Id, MapTaskType(operation.ProcessOperationType),
                        $"{operation.ProcessOperationType} {campaign.CampaignNumber}/H{heat.SequenceNumber:00}",
                        heat.GradeCode, campaign.CasterSectionCode, heat.PlannedQuantityMt, null, due, priority,
                        options, dependencies, operation.ProcessOperationType);
                    tasks.Add(task);
                    predecessor = task;
                }

                if (issues.Any(x => x.Severity == PlanningIssueSeverity.Error && x.SourceId == heat.Id)) continue;
                if (predecessor is null) continue;

                var castingTask = tasks[castingTaskIndex];
                var ccmOperation = route[ccmIndex];
                var ccmDependency = BuildDependency(predecessor, castingTask.ResourceOptions, ccmOperation, flowLinks, grade, heatOrders, issues, heat.Id);
                tasks[castingTaskIndex] = castingTask with
                {
                    Dependencies = castingTask.Dependencies.Concat(new[] { ccmDependency }).ToArray(),
                    ProcessOperationType = ProcessOperationType.Ccm
                };
            }
        }

        return structure with { SchedulingTasks = tasks, Issues = issues };
    }

    private static IReadOnlyCollection<FiniteScheduleResourceOption> BuildResourceOptions(
        ManufacturingRouteOperation operation,
        CampaignHeat heat,
        Campaign campaign,
        IReadOnlyCollection<ProductionOrder> orders,
        SteelGrade? grade,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyDictionary<Guid, ResourceCapability[]> resourceCapabilities,
        IReadOnlyDictionary<Guid, RouteResourceCapability[]> routeCapabilities)
    {
        var unitType = UnitTypeFor(operation.ProcessOperationType);
        var requiredResourceIds = orders.SelectMany(order => RequiredResourcesFor(order, operation.ProcessOperationType)).Distinct().ToArray();
        if (requiredResourceIds.Length > 1) return Array.Empty<FiniteScheduleResourceOption>();

        var options = new List<FiniteScheduleResourceOption>();
        foreach (var resource in resources.Where(x =>
                     x.IsActive &&
                     x.OperatingState is ResourceOperatingState.Available or ResourceOperatingState.CapacityDerated or ResourceOperatingState.QualityRestricted &&
                     x.ProcessUnitType == unitType))
        {
            if (requiredResourceIds.Length == 1 && resource.Id != requiredResourceIds[0]) continue;
            if (!QuantityFits(resource, heat.PlannedQuantityMt)) continue;

            var routeMatches = routeCapabilities.TryGetValue(resource.Id, out var routeValues)
                ? routeValues.Where(x => x.ProcessOperationType == operation.ProcessOperationType && Matches(x.RouteCode, campaign.RouteCode) && Matches(x.GradeCode, heat.GradeCode) && Matches(x.GradeFamilyCode, grade?.GradeFamilyCode) && Matches(x.CastingClassCode, grade?.CastingClassCode) && Fits(x.MinimumQuantityMt, x.MaximumQuantityMt, heat.PlannedQuantityMt)).ToArray()
                : Array.Empty<RouteResourceCapability>();
            if (routeCapabilities.ContainsKey(resource.Id) && routeMatches.Length == 0) continue;

            var genericMatches = resourceCapabilities.TryGetValue(resource.Id, out var genericValues)
                ? genericValues.Where(x => (!x.ProcessOperationType.HasValue || x.ProcessOperationType == operation.ProcessOperationType) && Matches(x.RouteCode, campaign.RouteCode) && Matches(x.GradeCode, heat.GradeCode) && Matches(x.GradeFamilyCode, grade?.GradeFamilyCode) && Matches(x.CastingClassCode, grade?.CastingClassCode) && Fits(x.MinimumQuantityMt, x.MaximumQuantityMt, heat.PlannedQuantityMt)).ToArray()
                : Array.Empty<ResourceCapability>();
            if (resourceCapabilities.ContainsKey(resource.Id) && genericMatches.Length == 0) continue;

            var duration = routeMatches.Where(x => x.FixedDurationMinutes.HasValue).Select(x => x.FixedDurationMinutes!.Value)
                .Concat(genericMatches.Where(x => x.FixedDurationMinutes.HasValue).Select(x => x.FixedDurationMinutes!.Value))
                .DefaultIfEmpty(resource.NominalResidenceMinutes ?? 0).Max();
            if (duration <= 0)
            {
                var throughput = routeMatches.Where(x => x.ThroughputMtPerHour.HasValue).Select(x => x.ThroughputMtPerHour!.Value)
                    .Concat(genericMatches.Where(x => x.ThroughputMtPerHour.HasValue).Select(x => x.ThroughputMtPerHour!.Value))
                    .Append(resource.NominalThroughputMtPerHour ?? 0m).DefaultIfEmpty(0m).Max();
                duration = throughput > 0m
                    ? Math.Max(1, (int)Math.Ceiling((double)(heat.PlannedQuantityMt / throughput * 60m)))
                    : grade?.ProcessRequirements.FirstOrDefault(x => x.ProcessOperationType == operation.ProcessOperationType)?.MinimumProcessMinutes ?? 60;
            }

            var assignmentPenalty = routeMatches.Select(x => x.AssignmentPenalty).Concat(genericMatches.Select(x => x.AssignmentPenalty)).DefaultIfEmpty(0).Min();
            if (routeMatches.Any(x => x.IsPreferred) || genericMatches.Any(x => x.IsPreferred)) assignmentPenalty = 0;
            options.Add(new FiniteScheduleResourceOption(resource.Id, duration, assignmentPenalty));
        }
        return options;
    }

    private static FiniteScheduleDependency BuildDependency(
        FiniteScheduleTask predecessor,
        IReadOnlyCollection<FiniteScheduleResourceOption> successorOptions,
        ManufacturingRouteOperation successorOperation,
        IReadOnlyCollection<PlantFlowLink> flowLinks,
        SteelGrade? grade,
        IReadOnlyCollection<ProductionOrder> orders,
        ICollection<PlanningIssue> issues,
        Guid heatId)
    {
        var pairs = new List<FiniteScheduleDependencyResourcePair>();
        foreach (var from in predecessor.ResourceOptions)
        foreach (var to in successorOptions)
        {
            var link = flowLinks.FirstOrDefault(x => x.IsEnabled && x.FromResourceId == from.ResourceId && x.ToResourceId == to.ResourceId && (!x.FromProcessOperationType.HasValue || x.FromProcessOperationType == predecessor.ProcessOperationType) && (!x.ToProcessOperationType.HasValue || x.ToProcessOperationType == successorOperation.ProcessOperationType));
            if (link is null) continue;

            var minLag = Math.Max(Minutes(successorOperation.MinimumQueueTime), Minutes(link.MinimumTransferTime));
            var maxLag = MinNullable(successorOperation.MaximumQueueTime is null ? null : Minutes(successorOperation.MaximumQueueTime.Value), link.MaximumTransferTime is null ? null : Minutes(link.MaximumTransferTime.Value));
            maxLag = MinNullable(maxLag, ThermalMaximumLagMinutes(grade, orders, link));
            if (maxLag.HasValue && maxLag.Value < minLag) continue;
            pairs.Add(new FiniteScheduleDependencyResourcePair(from.ResourceId, to.ResourceId, minLag, maxLag));
        }

        if (pairs.Count == 0)
            issues.Add(new PlanningIssue(PlanningIssueSeverity.Error, "PROCESS_FLOW_PATH_MISSING", $"No physical flow path exists from {predecessor.ProcessOperationType} to {successorOperation.ProcessOperationType} for heat {heatId}.", heatId));
        return new FiniteScheduleDependency(predecessor.TaskId, 0, null, pairs);
    }

    private static RequirementResolution ResolveRequirement(IReadOnlyCollection<ProductionOrder> orders, SteelGrade? grade, ProcessOperationType operationType, ICollection<PlanningIssue> issues, Guid heatId)
    {
        var dispositions = new List<RequirementDisposition>();
        var gradeDisposition = grade?.ProcessRequirements.FirstOrDefault(x => x.ProcessOperationType == operationType)?.Requirement;
        if (gradeDisposition.HasValue) dispositions.Add(gradeDisposition.Value);

        foreach (var order in orders)
        {
            var requirement = order.Requirement;
            if (requirement is null) continue;
            if (operationType == ProcessOperationType.Vd)
            {
                if (requirement.RequireVd == true) dispositions.Add(RequirementDisposition.Required);
                if (requirement.ForbidVd == true) dispositions.Add(RequirementDisposition.Forbidden);
            }
            if (operationType == ProcessOperationType.Reheat && requirement.RequireReheating == true) dispositions.Add(RequirementDisposition.Required);
            if (operationType == ProcessOperationType.Tmt && requirement.RequireTmt == true) dispositions.Add(RequirementDisposition.Required);
            dispositions.AddRange(requirement.ProcessOverrides.Where(x => x.ProcessOperationType == operationType).Select(x => x.Requirement));
        }

        if (dispositions.Contains(RequirementDisposition.Required) && dispositions.Contains(RequirementDisposition.Forbidden))
        {
            issues.Add(new PlanningIssue(PlanningIssueSeverity.Error, "PROCESS_REQUIREMENT_CONFLICT", $"Heat {heatId} has conflicting Required/Forbidden requirements for {operationType}.", heatId));
            return RequirementResolution.Conflict;
        }
        if (dispositions.Contains(RequirementDisposition.Required)) return RequirementResolution.Required;
        if (dispositions.Contains(RequirementDisposition.Forbidden)) return RequirementResolution.Forbidden;
        return RequirementResolution.Optional;
    }

    private static IEnumerable<Guid> RequiredResourcesFor(ProductionOrder order, ProcessOperationType operationType)
    {
        if (order.Requirement?.RequiredResourceId is { } general) yield return general;
        foreach (var process in order.Requirement?.ProcessOverrides ?? Array.Empty<OrderProcessRequirement>())
            if (process.ProcessOperationType == operationType && process.RequiredResourceId.HasValue) yield return process.RequiredResourceId.Value;
    }

    private static int? ThermalMaximumLagMinutes(SteelGrade? grade, IReadOnlyCollection<ProductionOrder> orders, PlantFlowLink link)
    {
        if (!link.NominalTemperatureLossCPerMinute.HasValue || link.NominalTemperatureLossCPerMinute.Value <= 0m) return null;

        var minimums = new[] { grade?.MinimumSuperheatC }
            .Concat(orders.Select(x => x.Requirement?.MinimumSuperheatC))
            .Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        var targets = orders.Select(x => x.Requirement?.TargetSuperheatC)
            .Where(x => x.HasValue).Select(x => x!.Value)
            .DefaultIfEmpty(grade?.TargetSuperheatC ?? 0m).ToArray();

        // With no configured superheat window there is no thermal basis for capping the transfer at
        // all. Defaulting both ends to 0 made target <= minimum trivially true and returned a 0-minute
        // maximum lag, which is below any link's minimum transfer time - so every pair was discarded
        // and a plant that merely declared a cooling rate was reported as having no flow path.
        if (minimums.Length == 0 || grade?.TargetSuperheatC is null && orders.All(x => x.Requirement?.TargetSuperheatC is null))
        {
            return null;
        }

        var minimum = minimums.Max();
        var target = targets.Min();
        if (target <= minimum) return 0;
        return Math.Max(0, (int)Math.Floor((double)((target - minimum) / link.NominalTemperatureLossCPerMinute.Value)));
    }

    private static bool QuantityFits(Resource resource, decimal quantity) =>
        (!resource.MinimumHeatWeightMt.HasValue || quantity >= resource.MinimumHeatWeightMt.Value) &&
        (!resource.MaximumHeatWeightMt.HasValue || quantity <= resource.MaximumHeatWeightMt.Value * Math.Clamp(resource.CapacityFactorPct, 0m, 100m) / 100m);

    private static bool Fits(decimal? minimum, decimal? maximum, decimal quantity) => (!minimum.HasValue || quantity >= minimum.Value) && (!maximum.HasValue || quantity <= maximum.Value);

    internal static ProcessUnitType UnitTypeFor(ProcessOperationType operationType) => operationType switch
    {
        ProcessOperationType.Eaf => ProcessUnitType.Eaf,
        ProcessOperationType.Lrf => ProcessUnitType.Lrf,
        ProcessOperationType.Vd => ProcessUnitType.Vd,
        ProcessOperationType.Ccm => ProcessUnitType.Ccm,
        ProcessOperationType.Reheat => ProcessUnitType.ReheatingFurnace,
        ProcessOperationType.HotRoll => ProcessUnitType.HotRollingMill,
        ProcessOperationType.ColdRoll => ProcessUnitType.ColdRollingMill,
        ProcessOperationType.Tmt => ProcessUnitType.TmtWaterBox,
        ProcessOperationType.Cool => ProcessUnitType.CoolingBed,
        ProcessOperationType.Cut => ProcessUnitType.Shear,
        ProcessOperationType.Bundle => ProcessUnitType.BundlingLine,
        ProcessOperationType.Coil => ProcessUnitType.Coiler,
        _ => ProcessUnitType.FinishingLine
    };

    private static FiniteScheduleTaskType MapTaskType(ProcessOperationType operationType) => operationType switch
    {
        ProcessOperationType.Eaf => FiniteScheduleTaskType.Eaf,
        ProcessOperationType.Lrf => FiniteScheduleTaskType.Lrf,
        ProcessOperationType.Vd => FiniteScheduleTaskType.Vd,
        ProcessOperationType.Ccm => FiniteScheduleTaskType.Casting,
        ProcessOperationType.Reheat => FiniteScheduleTaskType.Reheating,
        ProcessOperationType.HotRoll => FiniteScheduleTaskType.HotRolling,
        ProcessOperationType.ColdRoll => FiniteScheduleTaskType.ColdRolling,
        ProcessOperationType.Tmt => FiniteScheduleTaskType.Tmt,
        ProcessOperationType.Cool => FiniteScheduleTaskType.Cooling,
        ProcessOperationType.Cut => FiniteScheduleTaskType.Cutting,
        ProcessOperationType.Bundle => FiniteScheduleTaskType.Bundling,
        ProcessOperationType.Coil => FiniteScheduleTaskType.Coiling,
        _ => FiniteScheduleTaskType.Finishing
    };

    private static bool Matches(string? configured, string? actual) => string.IsNullOrWhiteSpace(configured) || string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);
    private static int Minutes(TimeSpan value) => Math.Max(0, (int)Math.Ceiling(value.TotalMinutes));
    private static int? MinNullable(int? first, int? second) => !first.HasValue ? second : !second.HasValue ? first : Math.Min(first.Value, second.Value);
    private enum RequirementResolution { Optional = 0, Required = 1, Forbidden = 2, Conflict = 3 }
}
