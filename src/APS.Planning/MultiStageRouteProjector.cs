using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class MultiStageRouteProjector
{
    public static ProductionStructurePlanningResult Apply(
        ProductionStructurePlanningResult structure,
        RoutePlanningInput routePlanning,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyCollection<TransitionRule> transitionRules)
    {
        var issues = structure.Issues.ToList();
        var tasks = structure.SchedulingTasks.ToList();
        var routePlans = new List<RouteOperationPlan>();
        var activeResources = resources.Where(x => x.IsActive).ToDictionary(x => x.Id);
        var capabilities = routePlanning.ResourceCapabilities
            .GroupBy(x => x.ResourceId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var operationsByRoute = routePlanning.Operations
            .GroupBy(x => x.RouteCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(y => y.SequenceNumber).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var resourceStates = activeResources.Values.ToDictionary(x => x.Id, x => new ResourceState(x));

        foreach (var hotPlan in structure.RollingPlans
                     .OrderBy(x => x.RollingMillResourceId)
                     .ThenBy(x => x.SequenceNumber))
        {
            if (!operationsByRoute.TryGetValue(hotPlan.RouteCode, out var operations)) continue;
            var hotIndex = Array.FindIndex(operations, x => x.OperationType == WorkOrderType.HotRolling);
            if (hotIndex < 0) continue;

            var finalSections = hotPlan.Allocations
                .Where(x => x.ProductionOrder is not null)
                .Select(x => x.ProductionOrder!.FinalCrossSectionCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (finalSections.Length != 1)
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "ROUTE_FINAL_SECTION_AMBIGUOUS",
                    $"Hot rolling plan {hotPlan.Id} contains multiple final cross-sections and cannot be expanded as one downstream route chain.",
                    hotPlan.Id));
                continue;
            }

            var finalSection = finalSections[0];
            var currentSection = hotPlan.OutputCrossSectionCode;
            var upstreamPlanId = hotPlan.Id;
            var upstreamTasks = tasks
                .Where(x => x.SourceEntityId == hotPlan.Id)
                .ToArray();

            foreach (var operation in operations.Skip(hotIndex + 1))
            {
                if (!ShouldExecute(operation, currentSection, finalSection)) continue;
                if (upstreamTasks.Length == 0)
                {
                    issues.Add(new PlanningIssue(
                        PlanningIssueSeverity.Error,
                        "ROUTE_UPSTREAM_TASK_MISSING",
                        $"Route {hotPlan.RouteCode} operation {operation.SequenceNumber} has no upstream scheduled material blocks.",
                        hotPlan.Id));
                    break;
                }

                var inputSection = operation.InputCrossSectionCode ?? currentSection;
                var outputSection = operation.OutputCrossSectionCode ?? finalSection;
                if (!string.Equals(inputSection, currentSection, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new PlanningIssue(
                        PlanningIssueSeverity.Error,
                        "ROUTE_SECTION_DISCONTINUITY",
                        $"Route {hotPlan.RouteCode} operation {operation.SequenceNumber} expects {inputSection} but upstream produces {currentSection}.",
                        operation.Id));
                    break;
                }

                var representative = hotPlan.Allocations
                    .Select(x => x.ProductionOrder)
                    .First(x => x is not null)!;
                var candidates = resourceStates.Values
                    .Select(state =>
                    {
                        var matching = MatchCapabilities(
                            state.Resource,
                            capabilities,
                            representative,
                            operation.OperationType,
                            inputSection,
                            outputSection);
                        if (matching.Count == 0) return null;
                        if (!TransitionAllowed(
                                transitionRules,
                                state.Resource,
                                TransitionDimension.Grade,
                                state.LastGradeCode,
                                hotPlan.GradeCode) ||
                            !TransitionAllowed(
                                transitionRules,
                                state.Resource,
                                TransitionDimension.CrossSection,
                                state.LastOutputSectionCode,
                                outputSection))
                        {
                            return null;
                        }

                        var duration = upstreamTasks.Sum(task => DurationMinutes(
                            task.QuantityMt,
                            matching.Select(x => x.ThroughputMtPerHour),
                            60));
                        var score = state.LoadMinutes +
                                    TransitionPenalty(transitionRules, state.Resource, TransitionDimension.Grade, state.LastGradeCode, hotPlan.GradeCode) +
                                    TransitionPenalty(transitionRules, state.Resource, TransitionDimension.CrossSection, state.LastOutputSectionCode, outputSection);
                        return new Candidate(state, matching, duration, score);
                    })
                    .Where(x => x is not null)
                    .Cast<Candidate>()
                    .OrderBy(x => x.Score)
                    .ThenBy(x => x.State.Resource.Code)
                    .ToArray();

                if (candidates.Length == 0)
                {
                    issues.Add(new PlanningIssue(
                        PlanningIssueSeverity.Error,
                        "ROUTE_RESOURCE_NOT_ELIGIBLE",
                        $"No resource can perform {operation.OperationType} for {hotPlan.GradeCode} {inputSection}->{outputSection} on route {hotPlan.RouteCode}.",
                        operation.Id));
                    break;
                }

                var selected = candidates[0];
                var routePlan = new RouteOperationPlan
                {
                    RouteCode = hotPlan.RouteCode,
                    UpstreamPlanId = upstreamPlanId,
                    OperationType = operation.OperationType,
                    SequenceNumber = operation.SequenceNumber,
                    ResourceId = selected.State.Resource.Id,
                    GradeCode = hotPlan.GradeCode,
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
                var due = routePlan.Allocations
                    .Where(x => x.ProductionOrder is not null)
                    .Min(x => x.ProductionOrder!.RequiredDate);
                var priority = routePlan.Allocations
                    .Where(x => x.ProductionOrder is not null)
                    .Max(x => x.ProductionOrder!.Priority);
                var newTasks = new List<FiniteScheduleTask>();

                foreach (var upstreamTask in upstreamTasks)
                {
                    var duration = DurationMinutes(
                        upstreamTask.QuantityMt,
                        selected.Capabilities.Select(x => x.ThroughputMtPerHour),
                        60);
                    newTasks.Add(new FiniteScheduleTask(
                        Guid.NewGuid(),
                        routePlan.Id,
                        MapTaskType(operation.OperationType),
                        $"{operation.OperationType} {routePlan.SequenceNumber} - {routePlan.GradeCode}/{outputSection}",
                        routePlan.GradeCode,
                        outputSection,
                        upstreamTask.QuantityMt,
                        null,
                        due,
                        priority,
                        new[] { new FiniteScheduleResourceOption(selected.State.Resource.Id, duration) },
                        new[]
                        {
                            new FiniteScheduleDependency(
                                upstreamTask.TaskId,
                                Minutes(operation.MinimumQueueTime),
                                operation.MaximumQueueTime is null ? null : Minutes(operation.MaximumQueueTime.Value))
                        }));
                }

                tasks.AddRange(newTasks);
                selected.State.LoadMinutes += selected.DurationMinutes;
                selected.State.LastGradeCode = hotPlan.GradeCode;
                selected.State.LastOutputSectionCode = outputSection;
                upstreamPlanId = routePlan.Id;
                upstreamTasks = newTasks.ToArray();
                currentSection = outputSection;
            }

            if (!string.Equals(currentSection, finalSection, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "ROUTE_FINAL_SECTION_NOT_REACHED",
                    $"Route {hotPlan.RouteCode} ends at {currentSection} but Production Orders require {finalSection}.",
                    hotPlan.Id));
            }
        }

        return structure with
        {
            SchedulingTasks = tasks,
            Issues = issues,
            RouteOperationPlans = routePlans
        };
    }

    private static bool ShouldExecute(
        ManufacturingRouteOperation operation,
        string currentSection,
        string finalSection)
    {
        if (!operation.IsOptional) return true;
        return !string.IsNullOrWhiteSpace(operation.OutputCrossSectionCode) &&
               string.Equals(operation.OutputCrossSectionCode, finalSection, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(currentSection, finalSection, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<RouteResourceCapability> MatchCapabilities(
        Resource resource,
        IReadOnlyDictionary<Guid, RouteResourceCapability[]> capabilities,
        ProductionOrder po,
        WorkOrderType operationType,
        string inputSection,
        string outputSection)
    {
        if (!capabilities.TryGetValue(resource.Id, out var values)) return Array.Empty<RouteResourceCapability>();
        return values.Where(x =>
            x.OperationType == operationType &&
            Matches(x.RouteCode, po.RouteCode) &&
            (string.IsNullOrWhiteSpace(x.GradeCode) || Matches(x.GradeCode, po.GradeCode)) &&
            (string.IsNullOrWhiteSpace(x.GradeFamilyCode) || Matches(x.GradeFamilyCode, po.GradeFamilyCode)) &&
            (string.IsNullOrWhiteSpace(x.InputCrossSectionCode) || Matches(x.InputCrossSectionCode, inputSection)) &&
            (string.IsNullOrWhiteSpace(x.OutputCrossSectionCode) || Matches(x.OutputCrossSectionCode, outputSection)) &&
            (string.IsNullOrWhiteSpace(x.ProductFamilyCode) || Matches(x.ProductFamilyCode, po.ProductFamilyCode)))
            .ToArray();
    }

    private static FiniteScheduleTaskType MapTaskType(WorkOrderType type) => type switch
    {
        WorkOrderType.HotRolling => FiniteScheduleTaskType.HotRolling,
        WorkOrderType.ColdRolling => FiniteScheduleTaskType.ColdRolling,
        WorkOrderType.Finishing => FiniteScheduleTaskType.Finishing,
        _ => throw new InvalidOperationException($"Configured downstream route operation {type} is not supported by the current scheduler.")
    };

    private static int DurationMinutes(
        decimal quantityMt,
        IEnumerable<decimal?> throughputs,
        int fallbackMinutes)
    {
        var throughput = throughputs
            .Where(x => x.HasValue && x.Value > 0m)
            .Select(x => x!.Value)
            .DefaultIfEmpty(0m)
            .Max();
        return throughput <= 0m
            ? Math.Max(1, fallbackMinutes)
            : Math.Max(1, (int)Math.Ceiling((double)(quantityMt / throughput * 60m)));
    }

    private static bool TransitionAllowed(
        IReadOnlyCollection<TransitionRule> rules,
        Resource resource,
        TransitionDimension dimension,
        string? from,
        string to)
    {
        if (string.IsNullOrWhiteSpace(from) || Matches(from, to)) return true;
        return FindTransitionRule(rules, resource, dimension, from, to)?.IsAllowed ?? true;
    }

    private static int TransitionPenalty(
        IReadOnlyCollection<TransitionRule> rules,
        Resource resource,
        TransitionDimension dimension,
        string? from,
        string to)
    {
        if (string.IsNullOrWhiteSpace(from) || Matches(from, to)) return 0;
        return FindTransitionRule(rules, resource, dimension, from, to)?.Penalty ?? 0;
    }

    private static TransitionRule? FindTransitionRule(
        IReadOnlyCollection<TransitionRule> rules,
        Resource resource,
        TransitionDimension dimension,
        string from,
        string to) =>
        rules
            .Where(x => x.Dimension == dimension && Matches(x.FromCode, from) && Matches(x.ToCode, to))
            .OrderByDescending(x => x.ResourceId == resource.Id)
            .ThenByDescending(x => x.ResourceType == resource.ResourceType)
            .FirstOrDefault(x =>
                x.ResourceId == resource.Id ||
                x.ResourceType == resource.ResourceType ||
                (!x.ResourceId.HasValue && !x.ResourceType.HasValue));

    private static bool Matches(string? configured, string? actual) =>
        string.IsNullOrWhiteSpace(configured) ||
        string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);

    private static int Minutes(TimeSpan value) => Math.Max(0, (int)Math.Ceiling(value.TotalMinutes));

    private sealed class ResourceState(Resource resource)
    {
        public Resource Resource { get; } = resource;
        public int LoadMinutes { get; set; }
        public string? LastGradeCode { get; set; }
        public string? LastOutputSectionCode { get; set; }
    }

    private sealed record Candidate(
        ResourceState State,
        IReadOnlyList<RouteResourceCapability> Capabilities,
        int DurationMinutes,
        int Score);
}
