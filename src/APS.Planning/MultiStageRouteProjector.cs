using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class MultiStageRouteProjector
{
    public static ProductionStructurePlanningResult Apply(
        ProductionStructurePlanningResult structure,
        RoutePlanningInput routePlanning,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyCollection<TransitionRule> transitionRules,
        IReadOnlyCollection<PlantFlowLink>? flowLinks = null)
    {
        var issues = structure.Issues.ToList();
        var tasks = structure.SchedulingTasks.ToList();
        var routePlans = new List<RouteOperationPlan>();
        var activeResources = resources
            .Where(x => x.IsActive && x.OperatingState is ResourceOperatingState.Available or ResourceOperatingState.CapacityDerated or ResourceOperatingState.QualityRestricted)
            .ToDictionary(x => x.Id);
        var capabilities = routePlanning.ResourceCapabilities
            .GroupBy(x => x.ResourceId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var operationsByRoute = routePlanning.Operations
            .GroupBy(x => x.RouteCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.SequenceNumber).ToArray(), StringComparer.OrdinalIgnoreCase);
        var links = flowLinks ?? Array.Empty<PlantFlowLink>();
        var explicitSteelTopology = resources.Any(x => x.ProcessUnitType != ProcessUnitType.Unknown);

        foreach (var hotPlan in structure.RollingPlans.OrderBy(x => x.SequenceNumber))
        {
            if (!operationsByRoute.TryGetValue(hotPlan.RouteCode, out var operations)) continue;
            var hotIndex = Array.FindIndex(operations, x => x.ProcessOperationType == ProcessOperationType.HotRoll);
            if (hotIndex < 0) continue;

            var orders = hotPlan.Allocations
                .Where(x => x.ProductionOrder is not null)
                .Select(x => x.ProductionOrder!)
                .DistinctBy(x => x.Id)
                .ToArray();
            var finalSections = orders.Select(x => x.FinalCrossSectionCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (finalSections.Length != 1)
            {
                issues.Add(Error("ROUTE_FINAL_SECTION_AMBIGUOUS", $"Hot rolling plan {hotPlan.Id} contains multiple final cross-sections and cannot be expanded as one downstream route chain.", hotPlan.Id));
                continue;
            }

            var finalSection = finalSections[0];
            var currentSection = hotPlan.OutputCrossSectionCode;
            var upstreamPlanId = hotPlan.Id;
            var upstreamTasks = tasks.Where(x => x.SourceEntityId == hotPlan.Id && x.TaskType == FiniteScheduleTaskType.HotRolling).ToArray();

            foreach (var operation in operations.Skip(hotIndex + 1))
            {
                var disposition = ResolveRequirement(operation, orders, issues, hotPlan.Id);
                if (disposition == EffectiveRequirement.Forbidden) continue;
                if (disposition == EffectiveRequirement.Optional && !ChangesTowardFinalSection(operation, currentSection, finalSection)) continue;
                if (disposition == EffectiveRequirement.Conflict) break;

                if (upstreamTasks.Length == 0)
                {
                    issues.Add(Error("ROUTE_UPSTREAM_TASK_MISSING", $"Route {hotPlan.RouteCode} operation {operation.SequenceNumber} has no upstream scheduled material blocks.", hotPlan.Id));
                    break;
                }

                var inputSection = operation.InputCrossSectionCode ?? currentSection;
                var outputSection = operation.OutputCrossSectionCode ?? currentSection;
                if (!string.Equals(inputSection, currentSection, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(Error("ROUTE_SECTION_DISCONTINUITY", $"Route {hotPlan.RouteCode} operation {operation.SequenceNumber} expects {inputSection} but upstream produces {currentSection}.", operation.Id));
                    break;
                }

                var representative = orders[0];
                var eligible = BuildEligibleResources(operation, representative, inputSection, outputSection, activeResources, capabilities);
                if (eligible.Count == 0)
                {
                    issues.Add(Error("ROUTE_RESOURCE_NOT_ELIGIBLE", $"No physical resource can perform {operation.ProcessOperationType} for {hotPlan.GradeCode} {inputSection}->{outputSection} on route {hotPlan.RouteCode}.", operation.Id));
                    break;
                }

                var routePlan = new RouteOperationPlan
                {
                    RouteCode = hotPlan.RouteCode,
                    UpstreamPlanId = upstreamPlanId,
                    ProcessOperationType = operation.ProcessOperationType,
                    ReleaseWorkOrderType = operation.ReleaseWorkOrderType,
                    SequenceNumber = operation.SequenceNumber,
                    ResourceId = null,
                    GradeCode = hotPlan.GradeCode,
                    InputMaterialSpecificationCode = operation.InputMaterialSpecificationCode,
                    OutputMaterialSpecificationCode = operation.OutputMaterialSpecificationCode,
                    InputCrossSectionCode = inputSection,
                    OutputCrossSectionCode = outputSection,
                    PlannedQuantityMt = hotPlan.PlannedQuantityMt,
                    MinimumQueueTime = operation.MinimumQueueTime,
                    MaximumQueueTime = operation.MaximumQueueTime,
                    IsInventoryDecouplingPoint = operation.IsInventoryDecouplingPoint
                };
                foreach (var allocation in hotPlan.Allocations)
                {
                    routePlan.Allocations.Add(new RouteOperationPlanAllocation
                    {
                        RouteOperationPlanId = routePlan.Id,
                        RouteOperationPlan = routePlan,
                        CampaignId = allocation.CampaignId,
                        ProductionOrderId = allocation.ProductionOrderId,
                        ProductionOrder = allocation.ProductionOrder,
                        PlannedQuantityMt = allocation.PlannedQuantityMt
                    });
                }
                routePlans.Add(routePlan);

                var due = orders.Min(x => x.RequiredDate);
                var priority = orders.Max(x => x.Priority);
                var newTasks = new List<FiniteScheduleTask>();
                foreach (var upstreamTask in upstreamTasks)
                {
                    var options = eligible.Select(x => new FiniteScheduleResourceOption(
                            x.Resource.Id,
                            DurationMinutes(upstreamTask.QuantityMt, x.Capabilities, x.Resource),
                            x.AssignmentPenalty))
                        .ToArray();
                    var dependency = BuildDependency(
                        upstreamTask,
                        options,
                        operation,
                        links,
                        explicitSteelTopology,
                        issues,
                        routePlan.Id);

                    newTasks.Add(new FiniteScheduleTask(
                        Guid.NewGuid(),
                        routePlan.Id,
                        MapTaskType(operation.ProcessOperationType),
                        $"{operation.ProcessOperationType} {routePlan.SequenceNumber} - {routePlan.GradeCode}/{outputSection}",
                        routePlan.GradeCode,
                        outputSection,
                        upstreamTask.QuantityMt,
                        null,
                        due,
                        priority,
                        options,
                        new[] { dependency },
                        operation.ProcessOperationType));
                }

                tasks.AddRange(newTasks);
                upstreamPlanId = routePlan.Id;
                upstreamTasks = newTasks.ToArray();
                currentSection = outputSection;
            }

            if (!string.Equals(currentSection, finalSection, StringComparison.OrdinalIgnoreCase))
                issues.Add(Error("ROUTE_FINAL_SECTION_NOT_REACHED", $"Route {hotPlan.RouteCode} ends at {currentSection} but Production Orders require {finalSection}.", hotPlan.Id));
        }

        return structure with { SchedulingTasks = tasks, Issues = issues, RouteOperationPlans = routePlans };
    }

    private static IReadOnlyList<EligibleResource> BuildEligibleResources(
        ManufacturingRouteOperation operation,
        ProductionOrder po,
        string inputSection,
        string outputSection,
        IReadOnlyDictionary<Guid, Resource> resources,
        IReadOnlyDictionary<Guid, RouteResourceCapability[]> capabilities)
    {
        var result = new List<EligibleResource>();
        foreach (var resource in resources.Values)
        {
            if (!capabilities.TryGetValue(resource.Id, out var values)) continue;
            var matches = values.Where(x =>
                    x.ProcessOperationType == operation.ProcessOperationType &&
                    Matches(x.RouteCode, po.RouteCode) &&
                    Matches(x.GradeCode, po.GradeCode) &&
                    Matches(x.GradeFamilyCode, po.GradeFamilyCode) &&
                    Matches(x.CastingClassCode, po.SteelGrade?.CastingClassCode) &&
                    Matches(x.InputCrossSectionCode, inputSection) &&
                    Matches(x.OutputCrossSectionCode, outputSection) &&
                    Matches(x.ProductFamilyCode, po.ProductFamilyCode))
                .ToArray();
            if (matches.Length == 0) continue;
            var penalty = matches.Select(x => x.AssignmentPenalty).DefaultIfEmpty(0).Min();
            if (matches.Any(x => x.IsPreferred)) penalty = 0;
            result.Add(new EligibleResource(resource, matches, penalty));
        }
        return result;
    }

    private static FiniteScheduleDependency BuildDependency(
        FiniteScheduleTask predecessor,
        IReadOnlyCollection<FiniteScheduleResourceOption> successorOptions,
        ManufacturingRouteOperation operation,
        IReadOnlyCollection<PlantFlowLink> flowLinks,
        bool requirePhysicalPath,
        ICollection<PlanningIssue> issues,
        Guid sourceId)
    {
        var pairs = new List<FiniteScheduleDependencyResourcePair>();
        foreach (var from in predecessor.ResourceOptions)
        foreach (var to in successorOptions)
        {
            var link = flowLinks.FirstOrDefault(x => x.IsEnabled && x.FromResourceId == from.ResourceId && x.ToResourceId == to.ResourceId);
            if (link is null) continue;
            pairs.Add(new FiniteScheduleDependencyResourcePair(
                from.ResourceId,
                to.ResourceId,
                Math.Max(Minutes(operation.MinimumQueueTime), Minutes(link.MinimumTransferTime)),
                MinNullable(
                    operation.MaximumQueueTime.HasValue ? Minutes(operation.MaximumQueueTime.Value) : null,
                    link.MaximumTransferTime.HasValue ? Minutes(link.MaximumTransferTime.Value) : null)));
        }

        if (pairs.Count > 0) return new FiniteScheduleDependency(predecessor.TaskId, 0, null, pairs);
        if (requirePhysicalPath)
        {
            issues.Add(Error("ROUTE_PHYSICAL_FLOW_MISSING", $"No enabled physical flow path exists into {operation.ProcessOperationType}.", sourceId));
            return new FiniteScheduleDependency(predecessor.TaskId);
        }
        return new FiniteScheduleDependency(
            predecessor.TaskId,
            Minutes(operation.MinimumQueueTime),
            operation.MaximumQueueTime.HasValue ? Minutes(operation.MaximumQueueTime.Value) : null);
    }

    private static EffectiveRequirement ResolveRequirement(
        ManufacturingRouteOperation operation,
        IReadOnlyCollection<ProductionOrder> orders,
        ICollection<PlanningIssue> issues,
        Guid sourceId)
    {
        var values = new List<RequirementDisposition> { operation.Requirement };
        foreach (var order in orders)
        {
            var grade = order.SteelGrade?.ProcessRequirements.FirstOrDefault(x => x.ProcessOperationType == operation.ProcessOperationType)?.Requirement;
            if (grade.HasValue) values.Add(grade.Value);
            if (operation.ProcessOperationType == ProcessOperationType.Tmt && order.Requirement?.RequireTmt == true)
                values.Add(RequirementDisposition.Required);
            values.AddRange(order.Requirement?.ProcessOverrides
                .Where(x => x.ProcessOperationType == operation.ProcessOperationType)
                .Select(x => x.Requirement) ?? Array.Empty<RequirementDisposition>());
        }

        if (values.Contains(RequirementDisposition.Required) && values.Contains(RequirementDisposition.Forbidden))
        {
            issues.Add(Error("DOWNSTREAM_PROCESS_REQUIREMENT_CONFLICT", $"Conflicting Required/Forbidden requirements exist for {operation.ProcessOperationType}.", sourceId));
            return EffectiveRequirement.Conflict;
        }
        if (values.Contains(RequirementDisposition.Forbidden)) return EffectiveRequirement.Forbidden;
        if (values.Contains(RequirementDisposition.Required)) return EffectiveRequirement.Required;
        return EffectiveRequirement.Optional;
    }

    private static bool ChangesTowardFinalSection(ManufacturingRouteOperation operation, string currentSection, string finalSection) =>
        !string.IsNullOrWhiteSpace(operation.OutputCrossSectionCode) &&
        string.Equals(operation.OutputCrossSectionCode, finalSection, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(currentSection, finalSection, StringComparison.OrdinalIgnoreCase);

    private static FiniteScheduleTaskType MapTaskType(ProcessOperationType type) => type switch
    {
        ProcessOperationType.HotRoll => FiniteScheduleTaskType.HotRolling,
        ProcessOperationType.ColdRoll => FiniteScheduleTaskType.ColdRolling,
        ProcessOperationType.Tmt => FiniteScheduleTaskType.Tmt,
        ProcessOperationType.Cool => FiniteScheduleTaskType.Cooling,
        ProcessOperationType.Cut => FiniteScheduleTaskType.Cutting,
        ProcessOperationType.Bundle => FiniteScheduleTaskType.Bundling,
        ProcessOperationType.Coil => FiniteScheduleTaskType.Coiling,
        ProcessOperationType.Finish => FiniteScheduleTaskType.Finishing,
        _ => FiniteScheduleTaskType.Finishing
    };

    private static int DurationMinutes(decimal quantityMt, IReadOnlyCollection<RouteResourceCapability> capabilities, Resource resource)
    {
        var fixedDuration = capabilities.Where(x => x.FixedDurationMinutes.HasValue).Select(x => x.FixedDurationMinutes!.Value).DefaultIfEmpty(0).Max();
        if (fixedDuration > 0) return fixedDuration;
        var throughput = capabilities.Where(x => x.ThroughputMtPerHour.HasValue && x.ThroughputMtPerHour.Value > 0m)
            .Select(x => x.ThroughputMtPerHour!.Value)
            .Append(resource.NominalThroughputMtPerHour ?? 0m)
            .DefaultIfEmpty(0m)
            .Max();
        return throughput <= 0m ? 60 : Math.Max(1, (int)Math.Ceiling((double)(quantityMt / throughput * 60m)));
    }

    private static bool Matches(string? configured, string? actual) =>
        string.IsNullOrWhiteSpace(configured) || string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);
    private static int Minutes(TimeSpan value) => Math.Max(0, (int)Math.Ceiling(value.TotalMinutes));
    private static int? MinNullable(int? first, int? second) => !first.HasValue ? second : !second.HasValue ? first : Math.Min(first.Value, second.Value);
    private static PlanningIssue Error(string code, string message, Guid sourceId) => new(PlanningIssueSeverity.Error, code, message, sourceId);

    private sealed record EligibleResource(Resource Resource, IReadOnlyList<RouteResourceCapability> Capabilities, int AssignmentPenalty);
    private enum EffectiveRequirement { Optional = 0, Required = 1, Forbidden = 2, Conflict = 3 }
}
