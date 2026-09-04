using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static partial class MultiStageRouteProjector
{
    public static ProductionStructurePlanningResult Apply(
        ProductionStructurePlanningResult structure,
        CampaignPlanningResult campaignPlan,
        RoutePlanningInput routePlanning,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyCollection<ResourceCapability> genericCapabilities,
        IReadOnlyCollection<PlantFlowLink>? flowLinks = null,
        IReadOnlyCollection<ExternalMaterialSupply>? externalSupplies = null,
        IReadOnlyCollection<CommittedMaterialSupply>? committedSupplies = null,
        IReadOnlyCollection<GradeProcessTemperatureRequirement>? gradeTemperatureRequirements = null,
        DateTime? thermalReferenceTimeUtc = null,
        IReadOnlySet<string>? forcedThermalReheatRoutes = null)
    {
        var issues = structure.Issues.ToList();
        var tasks = structure.SchedulingTasks.ToList();
        var routePlans = (structure.RouteOperationPlans ?? Array.Empty<RouteOperationPlan>()).ToList();
        var decisions = (structure.RouteOperationDecisions ?? Array.Empty<RouteOperationDecision>()).ToList();
        var thermalDecisions = (structure.BilletThermalDecisions ?? Array.Empty<BilletThermalDecision>()).ToList();

        var context = new ProjectionContext(
            structure,
            routePlanning.Operations
                .GroupBy(x => x.RouteCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x.OrderBy(y => y.SequenceNumber).ToArray(),
                    StringComparer.OrdinalIgnoreCase),
            resources
                .Where(x => x.IsActive && x.OperatingState is
                    ResourceOperatingState.Available or
                    ResourceOperatingState.CapacityDerated or
                    ResourceOperatingState.QualityRestricted)
                .ToDictionary(x => x.Id),
            routePlanning.ResourceCapabilities
                .GroupBy(x => x.ResourceId)
                .ToDictionary(x => x.Key, x => x.ToArray()),
            genericCapabilities
                .GroupBy(x => x.ResourceId)
                .ToDictionary(x => x.Key, x => x.ToArray()),
            campaignPlan.InventoryAllocations
                .Where(x => x.Use is
                    PlanningInventoryUse.IntermediateFeed or
                    PlanningInventoryUse.ExternalIntermediateFeed or
                    PlanningInventoryUse.PlannedPurchaseFeed or
                    PlanningInventoryUse.PlannedTransferFeed or
                    PlanningInventoryUse.ManualPlannedFeed or
                    PlanningInventoryUse.CommittedInternalProductionFeed)
                .GroupBy(x => x.ProductionOrderId)
                .ToDictionary(x => x.Key, x => x.ToArray()),
            (externalSupplies ?? Array.Empty<ExternalMaterialSupply>())
                .GroupBy(x => x.SupplyReference, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase),
            (committedSupplies ?? Array.Empty<CommittedMaterialSupply>())
                .GroupBy(x => x.SupplyReference, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase),
            tasks
                .Where(x => x.ProcessOperationType == ProcessOperationType.Ccm || x.TaskType == FiniteScheduleTaskType.Casting)
                .GroupBy(x => x.SourceEntityId)
                .ToDictionary(x => x.Key, x => x.First()),
            BuildRemainingSupplyByHeat(structure, campaignPlan),
            flowLinks ?? Array.Empty<PlantFlowLink>(),
            gradeTemperatureRequirements ?? Array.Empty<GradeProcessTemperatureRequirement>(),
            thermalReferenceTimeUtc,
            resources.Any(x => x.ProcessUnitType != ProcessUnitType.Unknown),
            forcedThermalReheatRoutes,
            issues,
            tasks,
            routePlans,
            decisions,
            thermalDecisions);

        foreach (var rolling in structure.RollingPlans.OrderBy(x => x.SequenceNumber))
            ProjectRollingPlan(rolling, context);

        return structure with
        {
            SchedulingTasks = tasks,
            Issues = issues,
            RouteOperationPlans = routePlans,
            RouteOperationDecisions = decisions,
            BilletThermalDecisions = thermalDecisions
        };
    }

    private static Dictionary<Guid, decimal> BuildRemainingSupplyByHeat(
        ProductionStructurePlanningResult structure,
        CampaignPlanningResult campaignPlan)
    {
        var heatOutput = (campaignPlan.HeatAllocations ?? Array.Empty<CampaignHeatAllocation>())
            .GroupBy(x => x.CampaignHeatId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.PlannedOutputQuantityMt));

        return structure.CastSequences
            .SelectMany(x => x.Heats)
            .Select(x => x.CampaignHeatId)
            .Distinct()
            .ToDictionary(
                heatId => heatId,
                heatId => heatOutput.TryGetValue(heatId, out var output)
                    ? output
                    : structure.PlannedBilletSupplies.Where(x => x.CampaignHeatId == heatId).Sum(x => x.QuantityMt));
    }

    private static void ProjectRollingPlan(RollingPlan rolling, ProjectionContext context)
    {
        var state = TryPrepareRollingProjection(rolling, context);
        if (state is null) return;

        for (var operationIndex = 0; operationIndex < state.Operations.Length; operationIndex++)
        {
            var operation = state.Operations[operationIndex];
            var directive = EvaluateOperationDirective(operationIndex, operation, rolling, state, context);
            if (directive.Action == ProjectionAction.Stop) break;
            if (directive.Action == ProjectionAction.Skip) continue;

            var inputSection = operation.InputCrossSectionCode ?? state.CurrentSection;
            var outputSection = ResolveOutputSection(
                operation,
                state.Orders,
                inputSection,
                state.CurrentSection,
                state.FinalSection,
                context.ActiveResources,
                context.RouteCapabilities,
                context.GenericCapabilities);

            if (!Same(inputSection, state.CurrentSection))
            {
                context.Issues.Add(Error(
                    "ROUTE_SECTION_DISCONTINUITY",
                    $"Route {rolling.RouteCode} operation {operation.SequenceNumber} expects {inputSection} but upstream produces {state.CurrentSection}.",
                    operation.Id));
                break;
            }

            var hotRollIssue = HotRollEntryIssue(operation, rolling, state);
            if (hotRollIssue is not null)
            {
                context.Issues.Add(hotRollIssue);
                break;
            }

            var eligible = BuildEligibleResources(
                operation,
                state.Orders,
                rolling.PlannedQuantityMt,
                inputSection,
                outputSection,
                context.ActiveResources,
                context.RouteCapabilities,
                context.GenericCapabilities);
            if (eligible.Count == 0)
            {
                context.Issues.Add(Error(
                    operation.ProcessOperationType == ProcessOperationType.Reheat
                        ? "REHEAT_RESOURCE_MISSING"
                        : "ROUTE_RESOURCE_NOT_ELIGIBLE",
                    $"No available physical resource can perform {operation.ProcessOperationType} for {rolling.GradeCode} {inputSection}->{outputSection} on route {rolling.RouteCode}.",
                    operation.Id));
                break;
            }

            var routePlan = NewRoutePlan(rolling, state.UpstreamPlanId, operation, inputSection, outputSection);
            context.RoutePlans.Add(routePlan);

            var newTasks = BuildRouteTasks(rolling, routePlan, operation, state, eligible, outputSection, context);
            context.Tasks.AddRange(newTasks);
            RecordHotRollThermalDecisions(rolling, operation, state, newTasks, context);

            context.Decisions.Add(Decision(
                routePlan.Id,
                rolling.RouteCode,
                operation,
                RouteOperationOutcome.Included,
                directive.EffectiveRequirement == EffectiveRequirement.Required
                    ? "REQUIRED"
                    : directive.OptionalReheatReason ?? "ROUTE_TRANSFORMATION_REQUIRED"));

            AdvanceFeedCursors(operation, directive.OptionalReheatReason, state.Cursors, newTasks);
            state.UpstreamPlanId = routePlan.Id;
            state.CurrentSection = outputSection;
            state.SeenHotRoll |= operation.ProcessOperationType == ProcessOperationType.HotRoll;
        }

        ValidateRouteEnd(rolling, state, context.Issues);
    }

    private static RollingProjectionState? TryPrepareRollingProjection(
        RollingPlan rolling,
        ProjectionContext context)
    {
        if (!context.OperationsByRoute.TryGetValue(rolling.RouteCode, out var fullRoute))
        {
            context.Issues.Add(Error("ROUTE_NOT_FOUND", $"No route master exists for {rolling.RouteCode}.", rolling.Id));
            return null;
        }

        var ccmIndex = Array.FindIndex(fullRoute, x => x.ProcessOperationType == ProcessOperationType.Ccm);
        var operations = (ccmIndex >= 0 ? fullRoute.Skip(ccmIndex + 1) : fullRoute).ToArray();
        if (operations.Length == 0) return null;

        var orders = rolling.Allocations
            .Where(x => x.ProductionOrder is not null)
            .Select(x => x.ProductionOrder!)
            .DistinctBy(x => x.Id)
            .ToArray();
        if (orders.Length == 0)
        {
            context.Issues.Add(Error(
                "ROUTE_DEMAND_MISSING",
                $"Rolling demand {rolling.Id} has no Production Order allocations.",
                rolling.Id));
            return null;
        }

        var casterSections = orders
            .Select(x => x.CasterSectionCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var finalSections = orders
            .Select(x => x.FinalCrossSectionCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (casterSections.Length != 1 || finalSections.Length != 1)
        {
            context.Issues.Add(Error(
                "ROUTE_SECTION_AMBIGUOUS",
                $"Rolling demand {rolling.Id} contains multiple caster/final cross-sections and cannot be projected as one route chain.",
                rolling.Id));
            return null;
        }

        var cursors = BuildFeedCursors(
            rolling,
            context.Structure,
            context.CastTaskByHeat,
            context.RemainingSupplyByHeat,
            context.InventoryByPo,
            context.ExternalByReference,
            context.CommittedByReference,
            context.Issues);
        if (cursors.Count == 0 || HasSourceError(context.Issues, rolling.Id)) return null;

        return new RollingProjectionState(
            operations,
            orders,
            cursors,
            casterSections[0],
            finalSections[0],
            rolling.Id);
    }

    private static OperationProjectionDirective EvaluateOperationDirective(
        int operationIndex,
        ManufacturingRouteOperation operation,
        RollingPlan rolling,
        RollingProjectionState state,
        ProjectionContext context)
    {
        var effective = ResolveRequirement(operation, state.Orders, context.Issues, rolling.Id);
        if (effective == EffectiveRequirement.Conflict)
            return new OperationProjectionDirective(ProjectionAction.Stop, effective, null);

        if (effective == EffectiveRequirement.Forbidden)
        {
            context.Decisions.Add(Decision(
                rolling.Id,
                rolling.RouteCode,
                operation,
                RouteOperationOutcome.SkippedForbidden,
                "GRADE_OR_ORDER_FORBIDS"));
            return new OperationProjectionDirective(ProjectionAction.Skip, effective, null);
        }

        if (effective != EffectiveRequirement.Optional)
            return new OperationProjectionDirective(ProjectionAction.Include, effective, null);

        if (operation.ProcessOperationType == ProcessOperationType.Reheat)
        {
            var reheatReason = OptionalReheatReason(
                operationIndex,
                state.Operations,
                state.CurrentSection,
                rolling,
                state.Orders,
                state.Cursors,
                state.SeenHotRoll,
                context.ActiveResources,
                context.RouteCapabilities,
                context.GenericCapabilities,
                context.Links,
                context.GradeTemperatureRequirements,
                context.ThermalReferenceTimeUtc,
                context.ForcedThermalReheatRoutes?.Contains(rolling.RouteCode) == true);
            if (reheatReason is not null)
                return new OperationProjectionDirective(ProjectionAction.Include, effective, reheatReason);

            context.Decisions.Add(Decision(
                rolling.Id,
                rolling.RouteCode,
                operation,
                RouteOperationOutcome.SkippedOptional,
                "HOT_CHARGE_PREFERRED"));
            return new OperationProjectionDirective(ProjectionAction.Skip, effective, null);
        }

        if (ChangesTowardDownstreamNeed(
                operationIndex,
                state.Operations,
                operation,
                state.CurrentSection,
                state.FinalSection))
        {
            return new OperationProjectionDirective(ProjectionAction.Include, effective, null);
        }

        context.Decisions.Add(Decision(
            rolling.Id,
            rolling.RouteCode,
            operation,
            RouteOperationOutcome.SkippedOptional,
            "OPTIONAL_AND_NOT_REQUIRED"));
        return new OperationProjectionDirective(ProjectionAction.Skip, effective, null);
    }

    private static PlanningIssue? HotRollEntryIssue(
        ManufacturingRouteOperation operation,
        RollingPlan rolling,
        RollingProjectionState state)
    {
        if (operation.ProcessOperationType != ProcessOperationType.HotRoll) return null;

        if (state.Cursors.Any(x => !x.IsKnownHot))
        {
            return Error(
                "REHEAT_ROUTE_MISSING",
                $"Cold/buffered billet feed reaches HotRoll on route {rolling.RouteCode} without an included Reheat operation.",
                rolling.Id);
        }

        if (!state.SeenHotRoll &&
            DirectHotChargeForbidden(state.Orders) &&
            state.Cursors.Any(x => !x.PassedReheat))
        {
            return Error(
                "DIRECT_HOT_CHARGE_FORBIDDEN_REHEAT_REQUIRED",
                $"Grade/order policy forbids direct hot charge on route {rolling.RouteCode}; a Reheat operation must be configured and included before the first HotRoll.",
                rolling.Id);
        }

        if (operation.RequiredChargeMode == ChargeMode.ColdCharge && state.Cursors.Any(x => !x.PassedReheat))
        {
            return Error(
                "COLD_CHARGE_REHEAT_REQUIRED",
                $"HotRoll operation {operation.SequenceNumber} requires cold-charge/reheat preparation but no Reheat operation was included.",
                operation.Id);
        }

        return null;
    }

    private static IReadOnlyList<FiniteScheduleTask> BuildRouteTasks(
        RollingPlan rolling,
        RouteOperationPlan routePlan,
        ManufacturingRouteOperation operation,
        RollingProjectionState state,
        IReadOnlyCollection<EligibleResource> eligible,
        string outputSection,
        ProjectionContext context)
    {
        var due = state.Orders.Min(x => x.RequiredDate);
        var priority = state.Orders.Max(x => x.Priority);
        var tasks = new List<FiniteScheduleTask>(state.Cursors.Count);

        foreach (var cursor in state.Cursors)
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
                var directHotTransfer = operation.ProcessOperationType == ProcessOperationType.HotRoll &&
                                        cursor.IsKnownHot &&
                                        !cursor.PassedReheat;
                dependencies = new[]
                {
                    BuildDependency(
                        cursor.Predecessor,
                        options,
                        operation,
                        context.Links,
                        context.ExplicitTopology,
                        directHotTransfer,
                        context.Issues,
                        rolling.Id)
                };
            }

            tasks.Add(new FiniteScheduleTask(
                Guid.NewGuid(),
                routePlan.Id,
                MapTaskType(operation.ProcessOperationType),
                $"{operation.ProcessOperationType} {operation.SequenceNumber} - {rolling.GradeCode}/{outputSection}",
                rolling.GradeCode,
                outputSection,
                cursor.QuantityMt,
                cursor.Predecessor is null ? cursor.AvailableFromUtc : null,
                due,
                priority,
                options,
                dependencies,
                operation.ProcessOperationType));
        }

        return tasks;
    }

    private static void RecordHotRollThermalDecisions(
        RollingPlan rolling,
        ManufacturingRouteOperation operation,
        RollingProjectionState state,
        IReadOnlyList<FiniteScheduleTask> newTasks,
        ProjectionContext context)
    {
        if (operation.ProcessOperationType != ProcessOperationType.HotRoll) return;

        var gradeIds = state.Orders
            .Select(x => x.SteelGrade?.Id)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToHashSet();
        var minimumEntry = context.GradeTemperatureRequirements
            .Where(x =>
                gradeIds.Contains(x.SteelGradeId) &&
                x.ProcessOperationType == ProcessOperationType.HotRoll &&
                x.MinimumEntryTemperatureC.HasValue)
            .Select(x => x.MinimumEntryTemperatureC!.Value)
            .DefaultIfEmpty(decimal.MinValue)
            .Max();

        for (var cursorIndex = 0; cursorIndex < state.Cursors.Count; cursorIndex++)
        {
            var cursor = state.Cursors[cursorIndex];
            var reheatReason = cursor.ReheatReason;
            var thermalReheat = IsThermalReheatReason(reheatReason);
            var policyReheat = reheatReason is not null && !thermalReheat;
            context.ThermalDecisions.Add(new BilletThermalDecision(
                rolling.Id,
                newTasks[cursorIndex].TaskId,
                cursor.Predecessor?.TaskId,
                rolling.RouteCode,
                rolling.GradeCode,
                state.CurrentSection,
                cursor.ThermalBasis ?? (cursor.PassedReheat
                    ? BilletThermalSourceBasis.UnknownYard
                    : BilletThermalSourceBasis.PlannedCcm),
                cursor.TemperatureC,
                cursor.TemperatureObservedOnUtc,
                minimumEntry == decimal.MinValue ? null : minimumEntry,
                cursor.PassedReheat ? null : cursor.TemperatureC,
                null,
                null,
                null,
                cursor.PassedReheat ? BilletThermalOutcome.Reheated : BilletThermalOutcome.HotDirect,
                reheatReason ?? "HOT_CHARGE_PATH_SELECTED",
                cursor.PassedReheat ? "CONFIGURED_REHEAT_PATH" : "PENDING_SCHEDULED_TRANSFER",
                thermalReheat,
                policyReheat,
                thermalReheat && reheatReason is not null ? new[] { reheatReason } : Array.Empty<string>(),
                Array.Empty<string>()));
        }
    }

    private static void AdvanceFeedCursors(
        ManufacturingRouteOperation operation,
        string? optionalReheatReason,
        IReadOnlyList<FeedCursor> cursors,
        IReadOnlyList<FiniteScheduleTask> newTasks)
    {
        for (var index = 0; index < cursors.Count; index++)
        {
            var cursor = cursors[index];
            cursor.Predecessor = newTasks[index];
            cursor.AvailableFromUtc = null;

            if (operation.ProcessOperationType == ProcessOperationType.Reheat)
            {
                cursor.IsKnownHot = true;
                cursor.PassedReheat = true;
                cursor.ReheatReason = optionalReheatReason ?? "REHEAT_REQUIRED_BY_ROUTE_OR_POLICY";
            }
            else if (operation.ProcessOperationType == ProcessOperationType.HotRoll)
            {
                cursor.IsKnownHot = true;
            }

            if (!operation.IsInventoryDecouplingPoint) continue;

            // A decoupling point deliberately breaks guaranteed hot continuity. The material still exists;
            // a later HotRoll must re-establish thermal readiness through the configured Reheat path.
            cursor.IsKnownHot = false;
            cursor.PassedReheat = false;
        }
    }

    private static void ValidateRouteEnd(
        RollingPlan rolling,
        RollingProjectionState state,
        ICollection<PlanningIssue> issues)
    {
        if (HasSourceError(issues, rolling.Id)) return;

        if (!state.SeenHotRoll)
        {
            issues.Add(Error(
                "ROUTE_HOT_ROLL_NOT_PROJECTED",
                $"Route {rolling.RouteCode} created rolling demand but no HotRoll operation was projected.",
                rolling.Id));
            return;
        }

        if (Same(state.CurrentSection, state.FinalSection)) return;

        issues.Add(Error(
            "ROUTE_FINAL_SECTION_NOT_REACHED",
            $"Route {rolling.RouteCode} ends at {state.CurrentSection} but Production Orders require {state.FinalSection}.",
            rolling.Id));
    }

    private sealed record ProjectionContext(
        ProductionStructurePlanningResult Structure,
        IReadOnlyDictionary<string, ManufacturingRouteOperation[]> OperationsByRoute,
        IReadOnlyDictionary<Guid, Resource> ActiveResources,
        IReadOnlyDictionary<Guid, RouteResourceCapability[]> RouteCapabilities,
        IReadOnlyDictionary<Guid, ResourceCapability[]> GenericCapabilities,
        IReadOnlyDictionary<Guid, PlanningInventoryAllocation[]> InventoryByPo,
        IReadOnlyDictionary<string, ExternalMaterialSupply> ExternalByReference,
        IReadOnlyDictionary<string, CommittedMaterialSupply> CommittedByReference,
        IReadOnlyDictionary<Guid, FiniteScheduleTask> CastTaskByHeat,
        IDictionary<Guid, decimal> RemainingSupplyByHeat,
        IReadOnlyCollection<PlantFlowLink> Links,
        IReadOnlyCollection<GradeProcessTemperatureRequirement> GradeTemperatureRequirements,
        DateTime? ThermalReferenceTimeUtc,
        bool ExplicitTopology,
        IReadOnlySet<string>? ForcedThermalReheatRoutes,
        List<PlanningIssue> Issues,
        List<FiniteScheduleTask> Tasks,
        List<RouteOperationPlan> RoutePlans,
        List<RouteOperationDecision> Decisions,
        List<BilletThermalDecision> ThermalDecisions);

    private sealed class RollingProjectionState(
        ManufacturingRouteOperation[] operations,
        ProductionOrder[] orders,
        List<FeedCursor> cursors,
        string currentSection,
        string finalSection,
        Guid upstreamPlanId)
    {
        public ManufacturingRouteOperation[] Operations { get; } = operations;
        public ProductionOrder[] Orders { get; } = orders;
        public List<FeedCursor> Cursors { get; } = cursors;
        public string CurrentSection { get; set; } = currentSection;
        public string FinalSection { get; } = finalSection;
        public Guid UpstreamPlanId { get; set; } = upstreamPlanId;
        public bool SeenHotRoll { get; set; }
    }

    private sealed record OperationProjectionDirective(
        ProjectionAction Action,
        EffectiveRequirement EffectiveRequirement,
        string? OptionalReheatReason);

    private enum ProjectionAction
    {
        Stop = 0,
        Skip = 1,
        Include = 2
    }
}
