using APS.Application;
using APS.Domain;

namespace APS.Planning;

/// <summary>
/// Projects the configured route after CCM without treating the first HotRoll as an architectural
/// boundary. RollingPlan is only the allocation/material-demand anchor. Every actual downstream process,
/// including the first HotRoll, is represented as a RouteOperationPlan in configured sequence order.
///
/// Reheat remains a conditional route operation: known-hot feed takes the direct path when the route,
/// grade/order policy and physical hot-transfer links allow it; cold/yard feed selects the configured
/// Reheat operation. More detailed time/temperature decay remains owned by #56.
/// </summary>
internal static class MultiStageRouteProjector
{
    public static ProductionStructurePlanningResult Apply(
        ProductionStructurePlanningResult structure,
        CampaignPlanningResult campaignPlan,
        RoutePlanningInput routePlanning,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyCollection<ResourceCapability> genericCapabilities,
        IReadOnlyCollection<PlantFlowLink>? flowLinks = null,
        IReadOnlyCollection<ExternalMaterialSupply>? externalSupplies = null,
        IReadOnlyCollection<CommittedMaterialSupply>? committedSupplies = null)
    {
        var issues = structure.Issues.ToList();
        var tasks = structure.SchedulingTasks.ToList();
        var routePlans = (structure.RouteOperationPlans ?? Array.Empty<RouteOperationPlan>()).ToList();
        var decisions = (structure.RouteOperationDecisions ?? Array.Empty<RouteOperationDecision>()).ToList();
        var links = flowLinks ?? Array.Empty<PlantFlowLink>();

        var activeResources = resources
            .Where(x => x.IsActive && x.OperatingState is
                ResourceOperatingState.Available or
                ResourceOperatingState.CapacityDerated or
                ResourceOperatingState.QualityRestricted)
            .ToDictionary(x => x.Id);
        var routeCapabilities = routePlanning.ResourceCapabilities
            .GroupBy(x => x.ResourceId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var capabilities = genericCapabilities
            .GroupBy(x => x.ResourceId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var operationsByRoute = routePlanning.Operations
            .GroupBy(x => x.RouteCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.SequenceNumber).ToArray(), StringComparer.OrdinalIgnoreCase);
        var explicitSteelTopology = resources.Any(x => x.ProcessUnitType != ProcessUnitType.Unknown);

        var inventoryByPo = campaignPlan.InventoryAllocations
            .Where(x => x.Use is
                PlanningInventoryUse.IntermediateFeed or
                PlanningInventoryUse.ExternalIntermediateFeed or
                PlanningInventoryUse.PlannedPurchaseFeed or
                PlanningInventoryUse.PlannedTransferFeed or
                PlanningInventoryUse.ManualPlannedFeed or
                PlanningInventoryUse.CommittedInternalProductionFeed)
            .GroupBy(x => x.ProductionOrderId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var externalByReference = (externalSupplies ?? Array.Empty<ExternalMaterialSupply>())
            .GroupBy(x => x.SupplyReference, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var committedByReference = (committedSupplies ?? Array.Empty<CommittedMaterialSupply>())
            .GroupBy(x => x.SupplyReference, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var castTaskByHeat = tasks
            .Where(x => x.ProcessOperationType == ProcessOperationType.Ccm || x.TaskType == FiniteScheduleTaskType.Casting)
            .GroupBy(x => x.SourceEntityId)
            .ToDictionary(x => x.Key, x => x.First());
        var heatOutput = (campaignPlan.HeatAllocations ?? Array.Empty<CampaignHeatAllocation>())
            .GroupBy(x => x.CampaignHeatId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.PlannedOutputQuantityMt));
        var remainingSupplyByHeat = structure.CastSequences
            .SelectMany(x => x.Heats)
            .Select(x => x.CampaignHeatId)
            .Distinct()
            .ToDictionary(
                heatId => heatId,
                heatId => heatOutput.TryGetValue(heatId, out var output)
                    ? output
                    : structure.PlannedBilletSupplies.Where(x => x.CampaignHeatId == heatId).Sum(x => x.QuantityMt));

        foreach (var rollingPlan in structure.RollingPlans.OrderBy(x => x.SequenceNumber))
        {
            if (!operationsByRoute.TryGetValue(rollingPlan.RouteCode, out var fullRoute))
            {
                issues.Add(Error("ROUTE_NOT_FOUND", $"No route master exists for {rollingPlan.RouteCode}.", rollingPlan.Id));
                continue;
            }

            var ccmIndex = Array.FindIndex(fullRoute, x => x.ProcessOperationType == ProcessOperationType.Ccm);
            var operations = (ccmIndex >= 0 ? fullRoute.Skip(ccmIndex + 1) : fullRoute).ToArray();
            if (operations.Length == 0) continue;

            var orders = rollingPlan.Allocations
                .Where(x => x.ProductionOrder is not null)
                .Select(x => x.ProductionOrder!)
                .DistinctBy(x => x.Id)
                .ToArray();
            if (orders.Length == 0)
            {
                issues.Add(Error("ROUTE_DEMAND_MISSING", $"Rolling demand {rollingPlan.Id} has no Production Order allocations.", rollingPlan.Id));
                continue;
            }

            var casterSections = orders.Select(x => x.CasterSectionCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var finalSections = orders.Select(x => x.FinalCrossSectionCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (casterSections.Length != 1 || finalSections.Length != 1)
            {
                issues.Add(Error(
                    "ROUTE_SECTION_AMBIGUOUS",
                    $"Rolling demand {rollingPlan.Id} contains multiple caster/final cross-sections and cannot be projected as one route chain.",
                    rollingPlan.Id));
                continue;
            }

            var cursors = BuildFeedCursors(
                rollingPlan,
                structure,
                campaignPlan,
                tasks,
                castTaskByHeat,
                remainingSupplyByHeat,
                inventoryByPo,
                externalByReference,
                committedByReference,
                issues);
            if (cursors.Count == 0 || issues.Any(x => x.Severity == PlanningIssueSeverity.Error && x.SourceId == rollingPlan.Id))
                continue;

            var currentSection = casterSections[0];
            var finalSection = finalSections[0];
            var upstreamPlanId = rollingPlan.Id;
            var seenHotRoll = false;

            for (var operationIndex = 0; operationIndex < operations.Length; operationIndex++)
            {
                var operation = operations[operationIndex];
                var effective = ResolveRequirement(operation, orders, issues, rollingPlan.Id);
                if (effective == EffectiveRequirement.Conflict) break;

                if (effective == EffectiveRequirement.Forbidden)
                {
                    decisions.Add(Decision(
                        rollingPlan.Id,
                        rollingPlan.RouteCode,
                        operation,
                        RouteOperationOutcome.SkippedForbidden,
                        "GRADE_OR_ORDER_FORBIDS"));
                    continue;
                }

                var optionalReheatSelected = false;
                if (effective == EffectiveRequirement.Optional)
                {
                    if (operation.ProcessOperationType == ProcessOperationType.Reheat)
                    {
                        optionalReheatSelected = ShouldUseOptionalReheat(
                            operationIndex,
                            operations,
                            currentSection,
                            rollingPlan,
                            orders,
                            cursors,
                            activeResources,
                            routeCapabilities,
                            capabilities,
                            links);
                        if (!optionalReheatSelected)
                        {
                            decisions.Add(Decision(
                                rollingPlan.Id,
                                rollingPlan.RouteCode,
                                operation,
                                RouteOperationOutcome.SkippedOptional,
                                "HOT_CHARGE_PREFERRED"));
                            continue;
                        }
                    }
                    else if (!ChangesTowardDownstreamNeed(operationIndex, operations, operation, currentSection, finalSection))
                    {
                        decisions.Add(Decision(
                            rollingPlan.Id,
                            rollingPlan.RouteCode,
                            operation,
                            RouteOperationOutcome.SkippedOptional,
                            "OPTIONAL_AND_NOT_REQUIRED"));
                        continue;
                    }
                }

                var inputSection = operation.InputCrossSectionCode ?? currentSection;
                var outputSection = operation.OutputCrossSectionCode ?? currentSection;
                if (!string.Equals(inputSection, currentSection, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(Error(
                        "ROUTE_SECTION_DISCONTINUITY",
                        $"Route {rollingPlan.RouteCode} operation {operation.SequenceNumber} expects {inputSection} but upstream produces {currentSection}.",
                        operation.Id));
                    break;
                }

                var eligible = BuildEligibleResources(
                    operation,
                    orders,
                    inputSection,
                    outputSection,
                    activeResources,
                    routeCapabilities,
                    capabilities);
                if (eligible.Count == 0)
                {
                    var code = operation.ProcessOperationType == ProcessOperationType.Reheat
                        ? "REHEAT_RESOURCE_MISSING"
                        : "ROUTE_RESOURCE_NOT_ELIGIBLE";
                    issues.Add(Error(
                        code,
                        $"No available physical resource can perform {operation.ProcessOperationType} for {rollingPlan.GradeCode} {inputSection}->{outputSection} on route {rollingPlan.RouteCode}.",
                        operation.Id));
                    break;
                }

                var routePlan = new RouteOperationPlan
                {
                    RouteCode = rollingPlan.RouteCode,
                    UpstreamPlanId = upstreamPlanId,
                    ProcessOperationType = operation.ProcessOperationType,
                    ReleaseWorkOrderType = operation.ReleaseWorkOrderType,
                    SequenceNumber = operation.SequenceNumber,
                    ResourceId = null,
                    GradeCode = rollingPlan.GradeCode,
                    InputMaterialSpecificationCode = operation.InputMaterialSpecificationCode,
                    OutputMaterialSpecificationCode = operation.OutputMaterialSpecificationCode,
                    InputCrossSectionCode = inputSection,
                    OutputCrossSectionCode = outputSection,
                    PlannedQuantityMt = rollingPlan.PlannedQuantityMt,
                    MinimumQueueTime = operation.MinimumQueueTime,
                    MaximumQueueTime = operation.MaximumQueueTime,
                    IsInventoryDecouplingPoint = operation.IsInventoryDecouplingPoint
                };
                foreach (var allocation in rollingPlan.Allocations)
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
                foreach (var cursor in cursors)
                {
                    var options = eligible.Select(x => new FiniteScheduleResourceOption(
                            x.Resource.Id,
                            DurationMinutes(cursor.QuantityMt, x.RouteCapabilities, x.GenericCapabilities, x.Resource),
                            x.AssignmentPenalty,
                            "CONFIGURED_ROUTE_CAPABILITY"))
                        .ToArray();

                    IReadOnlyCollection<FiniteScheduleDependency> dependencies = Array.Empty<FiniteScheduleDependency>();
                    if (cursor.Predecessor is not null)
                    {
                        var requireHotTransfer = operation.ProcessOperationType == ProcessOperationType.HotRoll &&
                                                 cursor.IsKnownHot &&
                                                 cursor.Predecessor.ProcessOperationType is ProcessOperationType.Ccm or ProcessOperationType.HotRoll;
                        var dependency = BuildDependency(
                            cursor.Predecessor,
                            options,
                            operation,
                            links,
                            explicitSteelTopology,
                            requireHotTransfer,
                            issues,
                            rollingPlan.Id);
                        dependencies = new[] { dependency };
                    }

                    var task = new FiniteScheduleTask(
                        Guid.NewGuid(),
                        routePlan.Id,
                        MapTaskType(operation.ProcessOperationType),
                        $"{operation.ProcessOperationType} {operation.SequenceNumber} - {rollingPlan.GradeCode}/{outputSection}",
                        rollingPlan.GradeCode,
                        outputSection,
                        cursor.QuantityMt,
                        cursor.Predecessor is null ? cursor.AvailableFromUtc : null,
                        due,
                        priority,
                        options,
                        dependencies,
                        operation.ProcessOperationType);
                    newTasks.Add(task);
                }

                tasks.AddRange(newTasks);
                decisions.Add(Decision(
                    routePlan.Id,
                    rollingPlan.RouteCode,
                    operation,
                    RouteOperationOutcome.Included,
                    effective == EffectiveRequirement.Required
                        ? "REQUIRED"
                        : optionalReheatSelected
                            ? "COLD_FEED_REQUIRES_REHEAT"
                            : "ROUTE_TRANSFORMATION_REQUIRED"));

                for (var index = 0; index < cursors.Count; index++)
                {
                    cursors[index].Predecessor = newTasks[index];
                    cursors[index].AvailableFromUtc = null;
                    if (operation.ProcessOperationType is ProcessOperationType.Reheat or ProcessOperationType.HotRoll)
                        cursors[index].IsKnownHot = true;
                    if (operation.IsInventoryDecouplingPoint)
                        cursors[index].IsKnownHot = false;
                }

                upstreamPlanId = routePlan.Id;
                currentSection = outputSection;
                seenHotRoll |= operation.ProcessOperationType == ProcessOperationType.HotRoll;
            }

            if (!seenHotRoll)
            {
                issues.Add(Error(
                    "ROUTE_HOT_ROLL_NOT_PROJECTED",
                    $"Route {rollingPlan.RouteCode} created rolling demand but no HotRoll operation was projected.",
                    rollingPlan.Id));
            }
            else if (!string.Equals(currentSection, finalSection, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(Error(
                    "ROUTE_FINAL_SECTION_NOT_REACHED",
                    $"Route {rollingPlan.RouteCode} ends at {currentSection} but Production Orders require {finalSection}.",
                    rollingPlan.Id));
            }
        }

        return structure with
        {
            SchedulingTasks = tasks,
            Issues = issues,
            RouteOperationPlans = routePlans,
            RouteOperationDecisions = decisions
        };
    }

    private static List<FeedCursor> BuildFeedCursors(
        RollingPlan plan,
        ProductionStructurePlanningResult structure,
        CampaignPlanningResult campaignPlan,
        IReadOnlyCollection<FiniteScheduleTask> tasks,
        IReadOnlyDictionary<Guid, FiniteScheduleTask> castTaskByHeat,
        IDictionary<Guid, decimal> remainingSupplyByHeat,
        IReadOnlyDictionary<Guid, PlanningInventoryAllocation[]> inventoryByPo,
        IReadOnlyDictionary<string, ExternalMaterialSupply> externalByReference,
        IReadOnlyDictionary<string, CommittedMaterialSupply> committedByReference,
        ICollection<PlanningIssue> issues)
    {
        var result = new List<FeedCursor>();

        if (plan.FreshSteelQuantityMt > 0m)
        {
            var eligibleCampaigns = plan.Allocations
                .Where(x => x.FreshSteelQuantityMt > 0m)
                .Select(x => x.CampaignId)
                .ToHashSet();
            var candidateHeats = structure.CastSequences
                .OrderBy(x => x.SequenceNumber)
                .SelectMany(sequence => sequence.Heats.OrderBy(x => x.Position))
                .Where(x => eligibleCampaigns.Contains(x.CampaignHeat.CampaignId) &&
                            string.Equals(x.CampaignHeat.GradeCode, plan.GradeCode, StringComparison.OrdinalIgnoreCase) &&
                            castTaskByHeat.ContainsKey(x.CampaignHeatId))
                .Select(x => x.CampaignHeatId)
                .Distinct()
                .ToArray();

            var remaining = plan.FreshSteelQuantityMt;
            foreach (var heatId in candidateHeats)
            {
                if (remaining <= 0m) break;
                if (!remainingSupplyByHeat.TryGetValue(heatId, out var available) || available <= 0m) continue;
                var quantity = Math.Min(remaining, available);
                remaining -= quantity;
                remainingSupplyByHeat[heatId] = available - quantity;
                result.Add(new FeedCursor(quantity, castTaskByHeat[heatId], null, true));
            }

            if (remaining > 0.0001m)
            {
                issues.Add(Error(
                    "INSUFFICIENT_PLANNED_CAST_OUTPUT",
                    $"Rolling demand {plan.Id} requires {plan.FreshSteelQuantityMt:0.####} MT fresh feed but only {plan.FreshSteelQuantityMt - remaining:0.####} MT planned cast output is available.",
                    plan.Id));
            }
            return result;
        }

        var poIds = plan.Allocations.Select(x => x.ProductionOrderId).ToHashSet();
        var sourceAllocations = poIds
            .SelectMany(poId => inventoryByPo.TryGetValue(poId, out var values)
                ? values
                : Array.Empty<PlanningInventoryAllocation>())
            .ToArray();
        if (sourceAllocations.Length == 0)
        {
            issues.Add(Error(
                "ROLLING_FEED_SOURCE_MISSING",
                $"Rolling demand {plan.Id} has no qualified billet source allocation.",
                plan.Id));
            return result;
        }

        var availability = sourceAllocations
            .Where(x => x.AvailableFromUtc.HasValue)
            .Select(x => x.AvailableFromUtc!.Value)
            .ToArray();
        var availableFrom = availability.Length == 0 ? (DateTime?)null : availability.Max();
        var allKnownHot = sourceAllocations.All(x => IsKnownHotFeed(x, externalByReference, committedByReference));
        result.Add(new FeedCursor(plan.PlannedQuantityMt, null, availableFrom, allKnownHot));
        return result;
    }

    private static bool ShouldUseOptionalReheat(
        int operationIndex,
        IReadOnlyList<ManufacturingRouteOperation> operations,
        string currentSection,
        RollingPlan plan,
        IReadOnlyCollection<ProductionOrder> orders,
        IReadOnlyCollection<FeedCursor> cursors,
        IReadOnlyDictionary<Guid, Resource> activeResources,
        IReadOnlyDictionary<Guid, RouteResourceCapability[]> routeCapabilities,
        IReadOnlyDictionary<Guid, ResourceCapability[]> genericCapabilities,
        IReadOnlyCollection<PlantFlowLink> links)
    {
        if (cursors.Any(x => !x.IsKnownHot)) return true;
        if (orders.Any(x => x.Requirement?.RequireReheating == true)) return true;

        var next = operationIndex + 1 < operations.Count ? operations[operationIndex + 1] : null;
        if (next is null || next.ProcessOperationType != ProcessOperationType.HotRoll) return false;

        var input = next.InputCrossSectionCode ?? currentSection;
        var output = next.OutputCrossSectionCode ?? plan.OutputCrossSectionCode;
        var eligibleHotRoll = BuildEligibleResources(
            next,
            orders,
            input,
            output,
            activeResources,
            routeCapabilities,
            genericCapabilities);
        if (eligibleHotRoll.Count == 0) return false;

        foreach (var cursor in cursors.Where(x => x.Predecessor is not null))
        {
            var hasDirectHotPath = cursor.Predecessor!.ResourceOptions.Any(from =>
                eligibleHotRoll.Any(to => links.Any(link =>
                    link.IsEnabled &&
                    link.SupportsHotTransfer &&
                    link.FromResourceId == from.ResourceId &&
                    link.ToResourceId == to.Resource.Id &&
                    (!link.FromProcessOperationType.HasValue || link.FromProcessOperationType == cursor.Predecessor.ProcessOperationType) &&
                    (!link.ToProcessOperationType.HasValue || link.ToProcessOperationType == ProcessOperationType.HotRoll))));
            if (!hasDirectHotPath) return true;
        }
        return false;
    }

    private static IReadOnlyList<EligibleResource> BuildEligibleResources(
        ManufacturingRouteOperation operation,
        IReadOnlyCollection<ProductionOrder> orders,
        string inputSection,
        string outputSection,
        IReadOnlyDictionary<Guid, Resource> resources,
        IReadOnlyDictionary<Guid, RouteResourceCapability[]> routeCapabilities,
        IReadOnlyDictionary<Guid, ResourceCapability[]> genericCapabilities)
    {
        var result = new List<EligibleResource>();
        var requiredResourceIds = orders.SelectMany(x => RequiredResourcesFor(x, operation.ProcessOperationType)).Distinct().ToArray();
        if (requiredResourceIds.Length > 1) return result;

        var unitType = SteelmakingRouteProjector.UnitTypeFor(operation.ProcessOperationType);
        foreach (var resource in resources.Values)
        {
            if (requiredResourceIds.Length == 1 && resource.Id != requiredResourceIds[0]) continue;

            var routeValues = routeCapabilities.TryGetValue(resource.Id, out var routeAll)
                ? routeAll.Where(x =>
                    x.ProcessOperationType == operation.ProcessOperationType &&
                    orders.All(order =>
                        Matches(x.RouteCode, order.RouteCode) &&
                        Matches(x.GradeCode, order.GradeCode) &&
                        Matches(x.GradeFamilyCode, order.GradeFamilyCode) &&
                        Matches(x.CastingClassCode, order.SteelGrade?.CastingClassCode) &&
                        Matches(x.ProductFamilyCode, order.ProductFamilyCode)) &&
                    Matches(x.InputCrossSectionCode, inputSection) &&
                    Matches(x.OutputCrossSectionCode, outputSection) &&
                    Fits(x.MinimumQuantityMt, x.MaximumQuantityMt, orders.Sum(o => o.RemainingQuantityMt)))
                    .ToArray()
                : Array.Empty<RouteResourceCapability>();
            if (routeCapabilities.ContainsKey(resource.Id) && routeValues.Length == 0) continue;

            var genericValues = genericCapabilities.TryGetValue(resource.Id, out var genericAll)
                ? genericAll.Where(x =>
                    (!x.ProcessOperationType.HasValue || x.ProcessOperationType == operation.ProcessOperationType) &&
                    orders.All(order =>
                        Matches(x.RouteCode, order.RouteCode) &&
                        Matches(x.GradeCode, order.GradeCode) &&
                        Matches(x.GradeFamilyCode, order.GradeFamilyCode) &&
                        Matches(x.CastingClassCode, order.SteelGrade?.CastingClassCode) &&
                        Matches(x.ProductFamilyCode, order.ProductFamilyCode)) &&
                    Matches(x.InputCrossSectionCode, inputSection) &&
                    Matches(x.OutputCrossSectionCode, outputSection))
                    .ToArray()
                : Array.Empty<ResourceCapability>();
            if (genericCapabilities.ContainsKey(resource.Id) && genericValues.Length == 0) continue;

            var hasCapabilityEvidence = routeValues.Length > 0 || genericValues.Length > 0;
            if (!hasCapabilityEvidence && resource.ProcessUnitType != unitType) continue;

            var penalty = routeValues.Select(x => x.AssignmentPenalty)
                .Concat(genericValues.Select(x => x.AssignmentPenalty))
                .DefaultIfEmpty(0)
                .Min();
            if (routeValues.Any(x => x.IsPreferred) || genericValues.Any(x => x.IsPreferred)) penalty = 0;
            result.Add(new EligibleResource(resource, routeValues, genericValues, penalty));
        }
        return result;
    }

    private static FiniteScheduleDependency BuildDependency(
        FiniteScheduleTask predecessor,
        IReadOnlyCollection<FiniteScheduleResourceOption> successorOptions,
        ManufacturingRouteOperation operation,
        IReadOnlyCollection<PlantFlowLink> flowLinks,
        bool requirePhysicalPath,
        bool requireHotTransfer,
        ICollection<PlanningIssue> issues,
        Guid sourceId)
    {
        var pairs = new List<FiniteScheduleDependencyResourcePair>();
        foreach (var from in predecessor.ResourceOptions)
        foreach (var to in successorOptions)
        {
            var link = flowLinks.FirstOrDefault(x =>
                x.IsEnabled &&
                x.FromResourceId == from.ResourceId &&
                x.ToResourceId == to.ResourceId &&
                (!x.FromProcessOperationType.HasValue || x.FromProcessOperationType == predecessor.ProcessOperationType) &&
                (!x.ToProcessOperationType.HasValue || x.ToProcessOperationType == operation.ProcessOperationType) &&
                (!requireHotTransfer || x.SupportsHotTransfer));
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

        if (requireHotTransfer)
        {
            issues.Add(Error(
                "DIRECT_HOT_TRANSFER_UNAVAILABLE",
                $"No enabled hot-transfer path exists from {predecessor.ProcessOperationType} into {operation.ProcessOperationType}; the route must use a configured reheating/buffer path instead.",
                sourceId));
            return new FiniteScheduleDependency(predecessor.TaskId);
        }

        if (requirePhysicalPath)
        {
            issues.Add(Error(
                "ROUTE_PHYSICAL_FLOW_MISSING",
                $"No enabled physical flow path exists from {predecessor.ProcessOperationType} into {operation.ProcessOperationType}.",
                sourceId));
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
            var grade = order.SteelGrade?.ProcessRequirements
                .FirstOrDefault(x => x.ProcessOperationType == operation.ProcessOperationType)?.Requirement;
            if (grade.HasValue) values.Add(grade.Value);

            if (operation.ProcessOperationType == ProcessOperationType.Reheat && order.Requirement?.RequireReheating == true)
                values.Add(RequirementDisposition.Required);
            if (operation.ProcessOperationType == ProcessOperationType.Tmt && order.Requirement?.RequireTmt == true)
                values.Add(RequirementDisposition.Required);

            values.AddRange(order.Requirement?.ProcessOverrides
                .Where(x => x.ProcessOperationType == operation.ProcessOperationType)
                .Select(x => x.Requirement) ?? Array.Empty<RequirementDisposition>());
        }

        if (values.Contains(RequirementDisposition.Required) && values.Contains(RequirementDisposition.Forbidden))
        {
            issues.Add(Error(
                "DOWNSTREAM_PROCESS_REQUIREMENT_CONFLICT",
                $"Conflicting Required/Forbidden requirements exist for {operation.ProcessOperationType}.",
                sourceId));
            return EffectiveRequirement.Conflict;
        }
        if (values.Contains(RequirementDisposition.Forbidden)) return EffectiveRequirement.Forbidden;
        if (values.Contains(RequirementDisposition.Required)) return EffectiveRequirement.Required;
        return EffectiveRequirement.Optional;
    }

    private static bool ChangesTowardDownstreamNeed(
        int operationIndex,
        IReadOnlyList<ManufacturingRouteOperation> operations,
        ManufacturingRouteOperation operation,
        string currentSection,
        string finalSection)
    {
        if (string.IsNullOrWhiteSpace(operation.OutputCrossSectionCode)) return false;
        if (string.Equals(operation.OutputCrossSectionCode, currentSection, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(operation.OutputCrossSectionCode, finalSection, StringComparison.OrdinalIgnoreCase)) return true;

        return operations
            .Skip(operationIndex + 1)
            .Any(next => string.Equals(next.InputCrossSectionCode, operation.OutputCrossSectionCode, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsKnownHotFeed(
        PlanningInventoryAllocation allocation,
        IReadOnlyDictionary<string, ExternalMaterialSupply> externalByReference,
        IReadOnlyDictionary<string, CommittedMaterialSupply> committedByReference)
    {
        if (allocation.SourceReference is null) return false;
        return allocation.Use switch
        {
            PlanningInventoryUse.ExternalIntermediateFeed =>
                externalByReference.TryGetValue(allocation.SourceReference, out var external) &&
                external.ThermalState is ChargeMode.HotDirect or ChargeMode.HotBuffered,
            PlanningInventoryUse.CommittedInternalProductionFeed =>
                committedByReference.TryGetValue(allocation.SourceReference, out var committed) &&
                committed.ThermalState is ChargeMode.HotDirect or ChargeMode.HotBuffered,
            _ => false
        };
    }

    private static IEnumerable<Guid> RequiredResourcesFor(ProductionOrder order, ProcessOperationType operationType)
    {
        if (order.Requirement?.RequiredResourceId is { } general) yield return general;
        foreach (var process in order.Requirement?.ProcessOverrides ?? Array.Empty<OrderProcessRequirement>())
            if (process.ProcessOperationType == operationType && process.RequiredResourceId.HasValue)
                yield return process.RequiredResourceId.Value;
    }

    private static RouteOperationDecision Decision(
        Guid sourceEntityId,
        string routeCode,
        ManufacturingRouteOperation operation,
        RouteOperationOutcome outcome,
        string reasonCode) => new(
        sourceEntityId,
        routeCode,
        operation.SequenceNumber,
        operation.ProcessOperationType,
        operation.Requirement,
        outcome,
        reasonCode);

    private static FiniteScheduleTaskType MapTaskType(ProcessOperationType type) => type switch
    {
        ProcessOperationType.Reheat => FiniteScheduleTaskType.Reheating,
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

    private static int DurationMinutes(
        decimal quantityMt,
        IReadOnlyCollection<RouteResourceCapability> routeCapabilities,
        IReadOnlyCollection<ResourceCapability> genericCapabilities,
        Resource resource)
    {
        var fixedDuration = routeCapabilities.Where(x => x.FixedDurationMinutes.HasValue).Select(x => x.FixedDurationMinutes!.Value)
            .Concat(genericCapabilities.Where(x => x.FixedDurationMinutes.HasValue).Select(x => x.FixedDurationMinutes!.Value))
            .DefaultIfEmpty(resource.NominalResidenceMinutes ?? 0)
            .Max();
        if (fixedDuration > 0) return fixedDuration;

        var throughput = routeCapabilities.Where(x => x.ThroughputMtPerHour.HasValue && x.ThroughputMtPerHour.Value > 0m).Select(x => x.ThroughputMtPerHour!.Value)
            .Concat(genericCapabilities.Where(x => x.ThroughputMtPerHour.HasValue && x.ThroughputMtPerHour.Value > 0m).Select(x => x.ThroughputMtPerHour!.Value))
            .Append(resource.NominalThroughputMtPerHour ?? 0m)
            .DefaultIfEmpty(0m)
            .Max();
        return throughput <= 0m
            ? 60
            : Math.Max(1, (int)Math.Ceiling((double)(quantityMt / throughput * 60m)));
    }

    private static bool Matches(string? configured, string? actual) =>
        string.IsNullOrWhiteSpace(configured) || string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);
    private static bool Fits(decimal? minimum, decimal? maximum, decimal quantity) =>
        (!minimum.HasValue || quantity >= minimum.Value) && (!maximum.HasValue || quantity <= maximum.Value);
    private static int Minutes(TimeSpan value) => Math.Max(0, (int)Math.Ceiling(value.TotalMinutes));
    private static int? MinNullable(int? first, int? second) =>
        !first.HasValue ? second : !second.HasValue ? first : Math.Min(first.Value, second.Value);
    private static PlanningIssue Error(string code, string message, Guid sourceId) =>
        new(PlanningIssueSeverity.Error, code, message, sourceId);

    private sealed record EligibleResource(
        Resource Resource,
        IReadOnlyCollection<RouteResourceCapability> RouteCapabilities,
        IReadOnlyCollection<ResourceCapability> GenericCapabilities,
        int AssignmentPenalty);

    private sealed class FeedCursor(
        decimal quantityMt,
        FiniteScheduleTask? predecessor,
        DateTime? availableFromUtc,
        bool isKnownHot)
    {
        public decimal QuantityMt { get; } = quantityMt;
        public FiniteScheduleTask? Predecessor { get; set; } = predecessor;
        public DateTime? AvailableFromUtc { get; set; } = availableFromUtc;
        public bool IsKnownHot { get; set; } = isKnownHot;
    }

    private enum EffectiveRequirement
    {
        Optional = 0,
        Required = 1,
        Forbidden = 2,
        Conflict = 3
    }
}
