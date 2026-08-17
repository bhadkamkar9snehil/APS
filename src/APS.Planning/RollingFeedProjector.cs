using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class RollingFeedProjector
{
    public static ProductionStructurePlanningResult Apply(
        ProductionStructurePlanningResult structure,
        CampaignPlanningResult campaignPlan,
        RoutePlanningInput routePlanning,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyCollection<ResourceCapability> capabilities,
        IReadOnlyCollection<PlantFlowLink> flowLinks,
        IReadOnlyCollection<ExternalMaterialSupply>? externalSupplies)
    {
        var tasks = structure.SchedulingTasks.ToList();
        var issues = structure.Issues.ToList();
        var capabilitiesByResource = capabilities.GroupBy(x => x.ResourceId).ToDictionary(x => x.Key, x => x.ToArray());
        var externalByReference = (externalSupplies ?? Array.Empty<ExternalMaterialSupply>())
            .GroupBy(x => x.SupplyReference, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var routeByCode = routePlanning.Operations
            .GroupBy(x => x.RouteCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.SequenceNumber).ToArray(), StringComparer.OrdinalIgnoreCase);
        var inventoryByPo = campaignPlan.InventoryAllocations
            .Where(x => x.Use is PlanningInventoryUse.IntermediateFeed or PlanningInventoryUse.ExternalIntermediateFeed)
            .GroupBy(x => x.ProductionOrderId)
            .ToDictionary(x => x.Key, x => x.ToArray());

        foreach (var plan in structure.RollingPlans)
        {
            if (!routeByCode.TryGetValue(plan.RouteCode, out var route)) continue;
            var reheat = route.FirstOrDefault(x => x.ProcessOperationType == ProcessOperationType.Reheat);
            var rollingTasks = tasks.Where(x => x.SourceEntityId == plan.Id && x.TaskType == FiniteScheduleTaskType.HotRolling).ToArray();
            if (rollingTasks.Length == 0) continue;
            var orders = plan.Allocations.Where(x => x.ProductionOrder is not null).Select(x => x.ProductionOrder!).DistinctBy(x => x.Id).ToArray();

            if (plan.FreshSteelQuantityMt > 0m)
            {
                foreach (var rollingTask in rollingTasks)
                {
                    if (rollingTask.Dependencies.Count == 0) continue;
                    var predecessorId = rollingTask.Dependencies.First().PredecessorTaskId;
                    var castTask = tasks.FirstOrDefault(x => x.TaskId == predecessorId);
                    if (castTask is null) continue;

                    var mustReheat = RequiresReheat(orders) || !CanHotCharge(orders, castTask, rollingTask, flowLinks);
                    if (!mustReheat) continue;
                    if (reheat is null)
                    {
                        issues.Add(Error("REHEAT_ROUTE_MISSING", $"Route {plan.RouteCode} requires reheating before {rollingTask.Name} but has no Reheat operation.", plan.Id));
                        continue;
                    }

                    var reheatTask = CreateReheatTask(rollingTask, castTask, reheat, resources, capabilitiesByResource, flowLinks, issues);
                    if (reheatTask is null) continue;
                    tasks.Add(reheatTask);
                    Replace(tasks, rollingTask with
                    {
                        Dependencies = new[] { PhysicalDependency(reheatTask, rollingTask.ResourceOptions, flowLinks, issues, plan.Id) },
                        ProcessOperationType = ProcessOperationType.HotRoll
                    });
                }
                continue;
            }

            // Existing and purchased billets are cold-charge by default. An external supply may
            // explicitly carry a hot thermal state, in which case a valid direct hot path can bypass RHF.
            var sourceAllocations = plan.Allocations
                .Where(x => x.ProductionOrder is not null)
                .SelectMany(x => inventoryByPo.TryGetValue(x.ProductionOrderId, out var values) ? values : Array.Empty<PlanningInventoryAllocation>())
                .ToArray();
            if (sourceAllocations.Length == 0)
            {
                issues.Add(Error("ROLLING_FEED_SOURCE_MISSING", $"Rolling plan {plan.Id} has no qualified billet source allocation.", plan.Id));
                continue;
            }

            var latestAvailability = sourceAllocations.Where(x => x.AvailableFromUtc.HasValue).Select(x => x.AvailableFromUtc!.Value).DefaultIfEmpty().Max();
            var allExplicitlyHot = sourceAllocations.All(x =>
                x.Use == PlanningInventoryUse.ExternalIntermediateFeed &&
                x.SourceReference is not null &&
                externalByReference.TryGetValue(x.SourceReference, out var supply) &&
                supply.ThermalState is ChargeMode.HotDirect or ChargeMode.HotBuffered);
            var needsReheat = RequiresReheat(orders) || !allExplicitlyHot;

            foreach (var rollingTask in rollingTasks)
            {
                if (!needsReheat)
                {
                    Replace(tasks, rollingTask with
                    {
                        EarliestStartUtc = latestAvailability == default ? rollingTask.EarliestStartUtc : latestAvailability,
                        ProcessOperationType = ProcessOperationType.HotRoll
                    });
                    continue;
                }

                if (reheat is null)
                {
                    issues.Add(Error("REHEAT_ROUTE_MISSING", $"Cold billet feed for rolling plan {plan.Id} requires a Reheat operation on route {plan.RouteCode}.", plan.Id));
                    continue;
                }

                var reheatTask = CreateInventoryReheatTask(
                    rollingTask,
                    latestAvailability == default ? null : latestAvailability,
                    reheat,
                    resources,
                    capabilitiesByResource,
                    issues);
                if (reheatTask is null) continue;
                tasks.Add(reheatTask);
                Replace(tasks, rollingTask with
                {
                    EarliestStartUtc = null,
                    Dependencies = new[] { PhysicalDependency(reheatTask, rollingTask.ResourceOptions, flowLinks, issues, plan.Id) },
                    ProcessOperationType = ProcessOperationType.HotRoll
                });
            }
        }

        return structure with { SchedulingTasks = tasks, Issues = issues };
    }

    private static FiniteScheduleTask? CreateReheatTask(
        FiniteScheduleTask rollingTask,
        FiniteScheduleTask predecessor,
        ManufacturingRouteOperation operation,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyDictionary<Guid, ResourceCapability[]> capabilities,
        IReadOnlyCollection<PlantFlowLink> flowLinks,
        ICollection<PlanningIssue> issues)
    {
        var options = ReheatOptions(rollingTask, resources, capabilities);
        if (options.Count == 0)
        {
            issues.Add(Error("REHEAT_RESOURCE_MISSING", $"No available reheating furnace can process {rollingTask.GradeCode}/{rollingTask.CrossSectionCode}.", rollingTask.SourceEntityId));
            return null;
        }
        return new FiniteScheduleTask(
            Guid.NewGuid(), rollingTask.SourceEntityId, FiniteScheduleTaskType.Reheating,
            $"Reheat {rollingTask.Name}", rollingTask.GradeCode, rollingTask.CrossSectionCode,
            rollingTask.QuantityMt, null, rollingTask.DueUtc, rollingTask.Priority, options,
            new[] { PhysicalDependency(predecessor, options, flowLinks, issues, rollingTask.SourceEntityId) },
            ProcessOperationType.Reheat);
    }

    private static FiniteScheduleTask? CreateInventoryReheatTask(
        FiniteScheduleTask rollingTask,
        DateTime? availableFrom,
        ManufacturingRouteOperation operation,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyDictionary<Guid, ResourceCapability[]> capabilities,
        ICollection<PlanningIssue> issues)
    {
        var options = ReheatOptions(rollingTask, resources, capabilities);
        if (options.Count == 0)
        {
            issues.Add(Error("REHEAT_RESOURCE_MISSING", $"No available reheating furnace can process {rollingTask.GradeCode}/{rollingTask.CrossSectionCode}.", rollingTask.SourceEntityId));
            return null;
        }
        return new FiniteScheduleTask(
            Guid.NewGuid(), rollingTask.SourceEntityId, FiniteScheduleTaskType.Reheating,
            $"Reheat {rollingTask.Name}", rollingTask.GradeCode, rollingTask.CrossSectionCode,
            rollingTask.QuantityMt, availableFrom, rollingTask.DueUtc, rollingTask.Priority,
            options, Array.Empty<FiniteScheduleDependency>(), ProcessOperationType.Reheat);
    }

    private static IReadOnlyCollection<FiniteScheduleResourceOption> ReheatOptions(
        FiniteScheduleTask task,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyDictionary<Guid, ResourceCapability[]> capabilities)
    {
        var result = new List<FiniteScheduleResourceOption>();
        foreach (var resource in resources.Where(x =>
                     x.IsActive && x.ProcessUnitType == ProcessUnitType.ReheatingFurnace &&
                     x.OperatingState is ResourceOperatingState.Available or ResourceOperatingState.CapacityDerated or ResourceOperatingState.QualityRestricted))
        {
            var matches = capabilities.TryGetValue(resource.Id, out var all)
                ? all.Where(x => (!x.ProcessOperationType.HasValue || x.ProcessOperationType == ProcessOperationType.Reheat) && Matches(x.GradeCode, task.GradeCode) && Matches(x.InputCrossSectionCode, task.CrossSectionCode) && Fits(x.MinimumQuantityMt, x.MaximumQuantityMt, task.QuantityMt)).ToArray()
                : Array.Empty<ResourceCapability>();
            if (capabilities.ContainsKey(resource.Id) && matches.Length == 0) continue;

            var duration = matches.Where(x => x.FixedDurationMinutes.HasValue).Select(x => x.FixedDurationMinutes!.Value)
                .DefaultIfEmpty(resource.NominalResidenceMinutes ?? 0).Max();
            if (duration <= 0)
            {
                var throughput = matches.Where(x => x.ThroughputMtPerHour.HasValue).Select(x => x.ThroughputMtPerHour!.Value)
                    .Append(resource.NominalThroughputMtPerHour ?? 0m).DefaultIfEmpty(0m).Max();
                duration = throughput > 0m ? Math.Max(1, (int)Math.Ceiling((double)(task.QuantityMt / throughput * 60m))) : 60;
            }
            result.Add(new FiniteScheduleResourceOption(resource.Id, duration, matches.Select(x => x.AssignmentPenalty).DefaultIfEmpty(0).Min()));
        }
        return result;
    }

    private static FiniteScheduleDependency PhysicalDependency(
        FiniteScheduleTask predecessor,
        IReadOnlyCollection<FiniteScheduleResourceOption> successorOptions,
        IReadOnlyCollection<PlantFlowLink> links,
        ICollection<PlanningIssue> issues,
        Guid sourceId)
    {
        var pairs = new List<FiniteScheduleDependencyResourcePair>();
        foreach (var from in predecessor.ResourceOptions)
        foreach (var to in successorOptions)
        {
            var link = links.FirstOrDefault(x => x.IsEnabled && x.FromResourceId == from.ResourceId && x.ToResourceId == to.ResourceId);
            if (link is null) continue;
            pairs.Add(new FiniteScheduleDependencyResourcePair(
                from.ResourceId, to.ResourceId, Minutes(link.MinimumTransferTime),
                link.MaximumTransferTime.HasValue ? Minutes(link.MaximumTransferTime.Value) : null));
        }
        if (pairs.Count == 0) issues.Add(Error("ROLLING_FEED_PATH_MISSING", "No enabled physical material-flow link exists for the required billet feed path.", sourceId));
        return new FiniteScheduleDependency(predecessor.TaskId, 0, null, pairs);
    }

    private static bool CanHotCharge(
        IReadOnlyCollection<ProductionOrder> orders,
        FiniteScheduleTask castTask,
        FiniteScheduleTask rollingTask,
        IReadOnlyCollection<PlantFlowLink> links)
    {
        if (orders.Any(x => x.SteelGrade?.HotChargeEligible == false || x.Requirement?.ForbidHotCharge == true || x.Requirement?.RequireReheating == true)) return false;
        return castTask.ResourceOptions.Any(from => rollingTask.ResourceOptions.Any(to =>
            links.Any(link => link.IsEnabled && link.FromResourceId == from.ResourceId && link.ToResourceId == to.ResourceId && link.SupportsHotTransfer)));
    }

    private static bool RequiresReheat(IEnumerable<ProductionOrder> orders) => orders.Any(order =>
        order.Requirement?.RequireReheating == true ||
        order.SteelGrade?.ProcessRequirements.Any(x => x.ProcessOperationType == ProcessOperationType.Reheat && x.Requirement == RequirementDisposition.Required) == true ||
        order.Requirement?.ProcessOverrides.Any(x => x.ProcessOperationType == ProcessOperationType.Reheat && x.Requirement == RequirementDisposition.Required) == true);

    private static void Replace(List<FiniteScheduleTask> tasks, FiniteScheduleTask task)
    {
        var index = tasks.FindIndex(x => x.TaskId == task.TaskId);
        if (index >= 0) tasks[index] = task;
    }

    private static PlanningIssue Error(string code, string message, Guid sourceId) => new(PlanningIssueSeverity.Error, code, message, sourceId);
    private static bool Matches(string? configured, string? actual) => string.IsNullOrWhiteSpace(configured) || string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);
    private static bool Fits(decimal? minimum, decimal? maximum, decimal quantity) => (!minimum.HasValue || quantity >= minimum.Value) && (!maximum.HasValue || quantity <= maximum.Value);
    private static int Minutes(TimeSpan value) => Math.Max(0, (int)Math.Ceiling(value.TotalMinutes));
}
