using APS.Application;
using APS.Domain;

namespace APS.Planning;

public sealed class PlanningEngine(
    ICampaignPlanningService campaignPlanning,
    IProductionStructurePlanningService structurePlanning,
    IFiniteScheduleOptimizer scheduleOptimizer) : IPlanningEngine
{
    public PlanningRunResult Run(PlanningRunRequest request) => RunCore(request, null);

    private PlanningRunResult RunCore(
        PlanningRunRequest request,
        IReadOnlySet<string>? forcedThermalReheatRoutes)
    {
        var sourceRequest = request;
        if (request.ExecutionMode == PlanningExecutionMode.Production && request.RoutePlanning is null)
        {
            throw new PlanningConfigurationException(
                "Production planning requires configured manufacturing-route operations. " +
                "The simplified production-structure path is compatibility/demo behavior only.");
        }

        var createdOnUtc = DateTime.UtcNow;
        var planVersionId = Guid.NewGuid();

        // An operating-state scenario is a different plant, not a post-hoc filter (#17): outages,
        // deratings and grade restrictions are folded into the resource/capability/calendar masters
        // here so campaign formation, heat sizing, route projection and the solver all see the same
        // plant. Substituting them onto the request means every downstream reader picks them up
        // without a second scenario-aware code path to keep in step.
        var plantState = PlanningScenarioApplier.Apply(
            request.Resources,
            request.Capabilities,
            request.ResourceCalendars,
            request.Scenario,
            request.HorizonStartUtc,
            request.HorizonEndUtc);
        request = request with
        {
            Resources = plantState.Resources,
            Capabilities = plantState.Capabilities,
            ResourceCalendars = plantState.Calendars
        };

        SteelOrderRequirementValidator.Validate(request.ProductionOrders, request.SteelGrades);
        var requirementSnapshots = PlanRequirementSnapshotBuilder.Build(planVersionId, request.ProductionOrders);

        var steelTopologyConfigured = request.Resources.Any(x => x.ProcessUnitType != ProcessUnitType.Unknown);
        var effectiveTransitionRules = TransitionRuleMaterializer.Materialize(
            request.TransitionRules,
            request.Resources,
            request.ProductionOrders,
            request.SteelGrades,
            request.CrossSections,
            request.RoutePlanning);

        var campaignPlan = PrecomputedCampaignPlanningAdapter.FormCampaigns(
            campaignPlanning,
            new CampaignPlanningRequest(
                request.ProductionOrders,
                request.Inventory,
                request.CampaignPolicy,
                request.CampaignNumberPrefix,
                steelTopologyConfigured ? request.Resources : null,
                steelTopologyConfigured ? request.Capabilities : null,
                request.SteelGrades,
                request.ExternalMaterialSupplies,
                request.MaterialSupplyPolicy,
                request.MaterialSourcingRules,
                request.HorizonStartUtc,
                request.RoutePlanning,
                request.CommittedMaterialSupplies,
                request.FlowLinks,
                request.PrecomputedCampaignMaterialDemand,
                effectiveTransitionRules));

        var heatAllocations = CampaignHeatAllocationBuilder.Build(campaignPlan.Campaigns);
        campaignPlan = campaignPlan with { HeatAllocations = heatAllocations };

        var structureRequest = new ProductionStructurePlanningRequest(
            campaignPlan.Campaigns,
            request.Resources,
            request.Capabilities,
            effectiveTransitionRules,
            request.FlowLinks,
            request.StructurePolicy,
            request.RoutePlanning,
            request.SteelGrades,
            request.MaterialSpecifications,
            request.ExternalMaterialSupplies);

        // The simplified builder remains intentionally available only to Compatibility-mode callers
        // such as focused tests and the explicitly enabled demo sandbox. Production lifecycle requests
        // are guarded above and must use configured route-driven structure.
        var structure = request.RoutePlanning is null
            ? structurePlanning.Build(structureRequest)
            : ConfiguredRouteProductionStructureBuilder.Build(structureRequest);

        if (HasErrors(structure))
            return InvalidStructureResult(planVersionId, createdOnUtc, campaignPlan, structure, request.ReplanContext?.BaselinePlanVersionId, requirementSnapshots);

        structure = HeatLevelScheduleProjector.Apply(
            structure,
            request.Resources,
            request.Capabilities,
            request.FlowLinks,
            request.StructurePolicy,
            heatAllocations);
        if (HasErrors(structure))
            return InvalidStructureResult(planVersionId, createdOnUtc, campaignPlan, structure, request.ReplanContext?.BaselinePlanVersionId, requirementSnapshots);

        if (request.RoutePlanning is not null)
        {
            structure = SteelmakingRouteProjector.Apply(
                structure,
                request.RoutePlanning,
                request.Resources,
                request.Capabilities,
                request.FlowLinks,
                request.SteelGrades,
                heatAllocations);
            if (HasErrors(structure))
                return InvalidStructureResult(planVersionId, createdOnUtc, campaignPlan, structure, request.ReplanContext?.BaselinePlanVersionId, requirementSnapshots);

            // #58: one configured-route projector owns every operation after CCM, including the first
            // HotRoll. Feed thermal state only selects/skips optional route operations such as Reheat;
            // it no longer creates a separate first-mill topology beside the route model.
            structure = MultiStageRouteProjector.Apply(
                structure,
                campaignPlan,
                request.RoutePlanning,
                request.Resources,
                request.Capabilities,
                request.FlowLinks,
                request.ExternalMaterialSupplies,
                request.CommittedMaterialSupplies,
                request.GradeTemperatureRequirements,
                request.HorizonStartUtc,
                forcedThermalReheatRoutes);
            if (HasErrors(structure))
                return InvalidStructureResult(planVersionId, createdOnUtc, campaignPlan, structure, request.ReplanContext?.BaselinePlanVersionId, requirementSnapshots);
        }

        // Thermal/superheat envelopes narrow the allowed resource pairs and transfer windows between
        // liquid-steel operations (#9) and the configured CCM->HotRoll hot-charge path (#56). It runs
        // last so every route operation already exists, and before task identities are taken so the
        // solver sees the constrained dependencies.
        structure = ThermalConstraintProjector.Apply(
            structure,
            request.Resources,
            request.FlowLinks,
            request.GradeTemperatureRequirements,
            request.ResourceTemperatureCapabilities,
            heatAllocations,
            request.RoutePlanning);
        if (HasErrors(structure))
            return InvalidStructureResult(planVersionId, createdOnUtc, campaignPlan, structure, request.ReplanContext?.BaselinePlanVersionId, requirementSnapshots);

        var identities = PlanningTaskIdentityService.Build(structure);
        var originalTasks = structure.SchedulingTasks.ToArray();
        var overrideResult = ApplyResourceOverrides(structure.SchedulingTasks, identities, request.ReplanContext?.ResourceOverrides);
        if (overrideResult.Issues.Count > 0)
        {
            structure = structure with { Issues = structure.Issues.Concat(overrideResult.Issues).ToArray() };
            return InvalidStructureResult(planVersionId, createdOnUtc, campaignPlan, structure, request.ReplanContext?.BaselinePlanVersionId, requirementSnapshots);
        }
        structure = structure with { SchedulingTasks = overrideResult.Tasks };

        var materialPreSchedule = TimePhasedMaterialPlanner.BuildPreSchedule(request, campaignPlan, structure);
        if (materialPreSchedule.Issues.Any(x => x.Severity == PlanningIssueSeverity.Error))
        {
            var issues = structure.Issues.Concat(materialPreSchedule.Issues).ToArray();
            structure = structure with { Issues = issues };
            var invalid = InvalidStructureResult(planVersionId, createdOnUtc, campaignPlan, structure, request.ReplanContext?.BaselinePlanVersionId, requirementSnapshots);
            return invalid with { MaterialPlan = materialPreSchedule };
        }

        var stabilityConstraints = BuildStabilityConstraints(request, structure.SchedulingTasks, identities);
        var serviceObligations = BuildServiceObligations(structure, heatAllocations);
        var linkedResourceGroups = BuildCasterLinkedGroups(structure);
        var finiteSchedule = scheduleOptimizer.Solve(new FiniteScheduleRequest(
            request.HorizonStartUtc,
            request.HorizonEndUtc,
            structure.SchedulingTasks,
            request.Resources,
            request.ResourceCalendars,
            effectiveTransitionRules,
            request.MaxSolverSeconds,
            stabilityConstraints,
            request.SteelGrades,
            materialPreSchedule.ScheduleEvents,
            serviceObligations,
            linkedResourceGroups));

        if (!finiteSchedule.IsFeasible && forcedThermalReheatRoutes is null)
        {
            var recoveryRoutes = ThermalRecoveryRoutes(structure, request.RoutePlanning);
            if (recoveryRoutes.Count > 0)
                return RunCore(sourceRequest, recoveryRoutes);
        }

        if (finiteSchedule.IsFeasible)
        {
            // Physical caster assignment was left open for CP-SAT (#16); resolve CastSequence.CasterResourceId
            // and PlannedStrandMaterialUnits from the actually-solved assignment before anything downstream
            // (material planning, Plan Version persistence, read models) reads them.
            structure = ResolvedCastingPlanProjector.Apply(structure, finiteSchedule, request.Resources, heatAllocations);
            structure = BilletThermalEvidenceProjector.Apply(
                structure,
                finiteSchedule,
                request.FlowLinks,
                request.GradeTemperatureRequirements,
                request.ResourceTemperatureCapabilities);
        }

        var materialPlan = finiteSchedule.IsFeasible
            ? TimePhasedMaterialPlanner.ResolveAfterSchedule(planVersionId, request, campaignPlan, materialPreSchedule, finiteSchedule)
            : materialPreSchedule;
        if (finiteSchedule.IsFeasible)
        {
            materialPlan = MaterialPlanFinalizer.Finalize(request, structure, materialPlan, finiteSchedule);
        }

        if (materialPlan.Issues.Count > materialPreSchedule.Issues.Count)
        {
            finiteSchedule = finiteSchedule with
            {
                Issues = finiteSchedule.Issues.Concat(materialPlan.Issues.Except(materialPreSchedule.Issues)).ToArray()
            };
        }

        var alternatives = BuildResourceAlternatives(originalTasks, identities, finiteSchedule);
        var packagingUnits = PackagingProjectionService.Build(
            request.ProductionOrders,
            campaignPlan,
            request.MaterialSpecifications,
            request.PackagingSpecifications,
            request.CrossSections);

        var feasible = finiteSchedule.IsFeasible &&
            !materialPlan.Issues.Any(x => x.Severity == PlanningIssueSeverity.Error) &&
            !HasErrors(structure);

        return new PlanningRunResult(
            planVersionId,
            createdOnUtc,
            campaignPlan,
            structure,
            finiteSchedule,
            feasible,
            identities,
            request.ReplanContext?.BaselinePlanVersionId,
            packagingUnits,
            requirementSnapshots,
            alternatives,
            materialPlan);
    }

    private static bool HasErrors(ProductionStructurePlanningResult structure) =>
        structure.Issues.Any(i => i.Severity == PlanningIssueSeverity.Error);

    private static IReadOnlySet<string> ThermalRecoveryRoutes(
        ProductionStructurePlanningResult structure,
        RoutePlanningInput? routePlanning)
    {
        if (routePlanning is null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tasks = structure.SchedulingTasks.ToDictionary(x => x.TaskId);
        var routePlans = (structure.RouteOperationPlans ?? Array.Empty<RouteOperationPlan>()).ToDictionary(x => x.Id);
        var routesWithOptionalReheat = routePlanning.Operations
            .Where(x =>
                x.ProcessOperationType == ProcessOperationType.Reheat &&
                x.Requirement == RequirementDisposition.Optional)
            .Select(x => x.RouteCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return tasks.Values
            .Where(x => x.ProcessOperationType == ProcessOperationType.HotRoll && routePlans.ContainsKey(x.SourceEntityId))
            .Where(x => x.Dependencies.Any(dependency =>
                tasks.TryGetValue(dependency.PredecessorTaskId, out var predecessor) &&
                predecessor.ProcessOperationType == ProcessOperationType.Ccm &&
                (dependency.MaximumLagMinutes.HasValue ||
                 dependency.AllowedResourcePairs?.Any(pair => pair.MaximumLagMinutes.HasValue) == true)))
            .Select(x => routePlans[x.SourceEntityId].RouteCode)
            .Where(routesWithOptionalReheat.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every heat in one continuous cast sequence must physically land on the same CCM even though CP-SAT
    /// is free to choose which one (#16) - ties their Ccm tasks together for FiniteScheduleOptimizer.
    /// </summary>
    private static IReadOnlyCollection<IReadOnlyCollection<Guid>> BuildCasterLinkedGroups(ProductionStructurePlanningResult structure)
    {
        var ccmTaskByHeat = structure.SchedulingTasks
            .Where(x => x.ProcessOperationType == ProcessOperationType.Ccm)
            .GroupBy(x => x.SourceEntityId)
            .ToDictionary(x => x.Key, x => x.First().TaskId);

        return structure.CastSequences
            .Select(sequence => sequence.Heats
                .OrderBy(h => h.Position)
                .Select(h => ccmTaskByHeat.TryGetValue(h.CampaignHeatId, out var taskId) ? (Guid?)taskId : null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToArray())
            .Where(group => group.Length > 1)
            .Cast<IReadOnlyCollection<Guid>>()
            .ToArray();
    }

    private static PlanningRunResult InvalidStructureResult(
        Guid planVersionId,
        DateTime createdOnUtc,
        CampaignPlanningResult campaignPlan,
        ProductionStructurePlanningResult structure,
        Guid? baselinePlanVersionId,
        IReadOnlyCollection<PlanOrderRequirementSnapshot> requirementSnapshots)
    {
        var schedule = new FiniteScheduleResult("StructureInvalid", false, 0, Array.Empty<FiniteScheduleAssignment>(), structure.Issues);
        return new PlanningRunResult(planVersionId, createdOnUtc, campaignPlan, structure, schedule, false,
            Array.Empty<PlanningTaskIdentity>(), baselinePlanVersionId, null, requirementSnapshots);
    }

    private static IReadOnlyCollection<FiniteScheduleServiceObligation> BuildServiceObligations(
        ProductionStructurePlanningResult structure,
        IReadOnlyCollection<CampaignHeatAllocation> heatAllocations)
    {
        var productionOrders = new Dictionary<Guid, ProductionOrder>();
        foreach (var allocation in heatAllocations.Where(x => x.ProductionOrder is not null))
            productionOrders[allocation.ProductionOrderId] = allocation.ProductionOrder!;
        foreach (var allocation in structure.RollingPlans.SelectMany(x => x.Allocations).Where(x => x.ProductionOrder is not null))
            productionOrders[allocation.ProductionOrderId] = allocation.ProductionOrder!;

        var heatById = heatAllocations
            .GroupBy(x => x.CampaignHeatId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var rollingById = structure.RollingPlans.ToDictionary(x => x.Id);
        var routeById = (structure.RouteOperationPlans ?? Array.Empty<RouteOperationPlan>()).ToDictionary(x => x.Id);
        var result = new List<FiniteScheduleServiceObligation>();

        foreach (var task in structure.SchedulingTasks)
        {
            IEnumerable<(Guid ProductionOrderId, decimal QuantityMt)> allocationQuantities;

            if (heatById.TryGetValue(task.SourceEntityId, out var heat))
            {
                allocationQuantities = heat.Select(x => (x.ProductionOrderId, x.PlannedOutputQuantityMt));
            }
            else if (rollingById.TryGetValue(task.SourceEntityId, out var rolling))
            {
                var scale = rolling.PlannedQuantityMt <= 0m
                    ? 1m
                    : Math.Min(1m, task.QuantityMt / rolling.PlannedQuantityMt);
                allocationQuantities = rolling.Allocations.Select(x => (x.ProductionOrderId, x.PlannedQuantityMt * scale));
            }
            else if (routeById.TryGetValue(task.SourceEntityId, out var route))
            {
                var scale = route.PlannedQuantityMt <= 0m
                    ? 1m
                    : Math.Min(1m, task.QuantityMt / route.PlannedQuantityMt);
                allocationQuantities = route.Allocations.Select(x => (x.ProductionOrderId, x.PlannedQuantityMt * scale));
            }
            else
            {
                continue;
            }

            foreach (var allocation in allocationQuantities
                         .Where(x => x.QuantityMt > 0m)
                         .GroupBy(x => x.ProductionOrderId)
                         .Select(x => (ProductionOrderId: x.Key, QuantityMt: x.Sum(y => y.QuantityMt))))
            {
                if (!productionOrders.TryGetValue(allocation.ProductionOrderId, out var po)) continue;
                result.Add(new FiniteScheduleServiceObligation(
                    task.TaskId,
                    po.Id,
                    allocation.QuantityMt,
                    po.RequiredDate,
                    po.Priority));
            }
        }

        return result;
    }

    private static ResourceOverrideApplicationResult ApplyResourceOverrides(
        IReadOnlyCollection<FiniteScheduleTask> tasks,
        IReadOnlyCollection<PlanningTaskIdentity> identities,
        IReadOnlyCollection<OperationResourceOverride>? overrides)
    {
        if (overrides is not { Count: > 0 }) return new ResourceOverrideApplicationResult(tasks, Array.Empty<PlanningIssue>());

        var identityByKey = identities.ToDictionary(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase);
        var taskById = tasks.ToDictionary(x => x.TaskId);
        var issues = new List<PlanningIssue>();
        var replacement = new Dictionary<Guid, FiniteScheduleTask>();

        foreach (var resourceOverride in overrides)
        {
            if (!identityByKey.TryGetValue(resourceOverride.PlanningKey, out var identity) ||
                !taskById.TryGetValue(identity.TaskId, out var task))
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "DISPATCH_OPERATION_NOT_FOUND",
                    $"Operational redispatch references unknown planning key {resourceOverride.PlanningKey}."));
                continue;
            }

            var selected = task.ResourceOptions.FirstOrDefault(x => x.ResourceId == resourceOverride.ResourceId);
            if (selected is null)
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "DISPATCH_RESOURCE_NOT_ELIGIBLE",
                    $"Resource {resourceOverride.ResourceId} was not an eligible alternative for {resourceOverride.PlanningKey}; redispatch is rejected before solve.",
                    task.TaskId));
                continue;
            }

            replacement[task.TaskId] = task with { ResourceOptions = new[] { selected } };
        }

        return new ResourceOverrideApplicationResult(
            tasks.Select(x => replacement.TryGetValue(x.TaskId, out var revised) ? revised : x).ToArray(),
            issues);
    }

    private static IReadOnlyCollection<PlanningOperationResourceAlternative> BuildResourceAlternatives(
        IReadOnlyCollection<FiniteScheduleTask> originalTasks,
        IReadOnlyCollection<PlanningTaskIdentity> identities,
        FiniteScheduleResult schedule)
    {
        var keyByTask = identities.ToDictionary(x => x.TaskId);
        var selectedByTask = schedule.Assignments.ToDictionary(x => x.TaskId, x => x.ResourceId);
        var result = new List<PlanningOperationResourceAlternative>();

        foreach (var task in originalTasks)
        {
            if (!keyByTask.TryGetValue(task.TaskId, out var identity)) continue;
            selectedByTask.TryGetValue(task.TaskId, out var selected);
            foreach (var option in task.ResourceOptions)
            {
                result.Add(new PlanningOperationResourceAlternative(
                    task.TaskId,
                    task.SourceEntityId,
                    identity.PlanningKey,
                    task.ProcessOperationType,
                    option.ResourceId,
                    option.DurationMinutes,
                    option.AssignmentPenalty,
                    selected != Guid.Empty && selected == option.ResourceId,
                    option.EligibilityBasisCode));
            }
        }
        return result;
    }

    private static IReadOnlyCollection<FiniteScheduleStabilityConstraint> BuildStabilityConstraints(
        PlanningRunRequest request,
        IReadOnlyCollection<FiniteScheduleTask> tasks,
        IReadOnlyCollection<PlanningTaskIdentity> identities)
    {
        var context = request.ReplanContext;
        if (context is null || context.BaselineOperations.Count == 0) return Array.Empty<FiniteScheduleStabilityConstraint>();

        var overrideKeys = (context.ResourceOverrides ?? Array.Empty<OperationResourceOverride>())
            .Select(x => x.PlanningKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var taskIds = tasks.Select(x => x.TaskId).ToHashSet();
        var baselineByKey = context.BaselineOperations
            .GroupBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.StartUtc).First(), StringComparer.OrdinalIgnoreCase);
        var policy = context.TimeFencePolicy;
        var constraints = new List<FiniteScheduleStabilityConstraint>();
        var repairTaskIds = overrideKeys.Count == 0
            ? null
            : BuildRepairScopeTaskIds(tasks, identities, context, overrideKeys, baselineByKey);

        foreach (var identity in identities.Where(x => taskIds.Contains(x.TaskId)))
        {
            if (overrideKeys.Contains(identity.PlanningKey)) continue;
            if (!baselineByKey.TryGetValue(identity.PlanningKey, out var baseline) || baseline.EndUtc <= context.ReferenceTimeUtc) continue;

            if (repairTaskIds is not null && !repairTaskIds.Contains(identity.TaskId))
            {
                constraints.Add(new FiniteScheduleStabilityConstraint(
                    identity.TaskId,
                    TimeFenceZone.Frozen,
                    baseline.ResourceId,
                    baseline.StartUtc,
                    0,
                    0));
                continue;
            }

            var minutesToStart = (baseline.StartUtc - context.ReferenceTimeUtc).TotalMinutes;
            var zone = minutesToStart <= policy.FrozenMinutes ? TimeFenceZone.Frozen
                : minutesToStart <= policy.FrozenMinutes + policy.SlushyMinutes ? TimeFenceZone.Slushy
                : TimeFenceZone.Liquid;
            if (zone == TimeFenceZone.Liquid) continue;
            constraints.Add(new FiniteScheduleStabilityConstraint(
                identity.TaskId,
                zone,
                baseline.ResourceId,
                baseline.StartUtc,
                policy.SlushyMovementPenaltyPerMinute,
                policy.SlushyResourceChangePenalty));
        }
        return constraints;
    }

    private static HashSet<Guid> BuildRepairScopeTaskIds(
        IReadOnlyCollection<FiniteScheduleTask> tasks,
        IReadOnlyCollection<PlanningTaskIdentity> identities,
        PlanningReplanContext context,
        IReadOnlySet<string> overrideKeys,
        IReadOnlyDictionary<string, BaselinePlanOperation> baselineByKey)
    {
        var scope = context.RepairScope ?? new RepairScopePolicy();
        if (!scope.FreezeUnaffectedOperations)
            return tasks.Select(x => x.TaskId).ToHashSet();

        var identityByKey = identities.ToDictionary(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase);
        var taskById = tasks.ToDictionary(x => x.TaskId);
        var successors = tasks
            .SelectMany(task => task.Dependencies.Select(dep => (dep.PredecessorTaskId, task.TaskId)))
            .GroupBy(x => x.PredecessorTaskId)
            .ToDictionary(x => x.Key, x => x.Select(y => y.TaskId).Distinct().ToArray());

        var affected = new HashSet<Guid>();
        var queue = new Queue<(Guid TaskId, int Depth)>();
        foreach (var key in overrideKeys)
        {
            if (!identityByKey.TryGetValue(key, out var identity)) continue;
            if (affected.Add(identity.TaskId)) queue.Enqueue((identity.TaskId, 0));
        }

        while (queue.Count > 0)
        {
            var (taskId, depth) = queue.Dequeue();
            if (depth >= Math.Max(0, scope.SuccessorDepth)) continue;
            if (!successors.TryGetValue(taskId, out var next)) continue;
            foreach (var successor in next)
            {
                if (affected.Add(successor)) queue.Enqueue((successor, depth + 1));
            }
        }

        if (scope.IncludeSameResourceNeighbors)
        {
            var seedResources = affected
                .Where(taskById.ContainsKey)
                .SelectMany(id => taskById[id].ResourceOptions.Select(x => x.ResourceId))
                .ToHashSet();
            var horizonEnd = context.ReferenceTimeUtc.AddMinutes(Math.Max(0, scope.RepairHorizonMinutes));
            foreach (var identity in identities)
            {
                if (!baselineByKey.TryGetValue(identity.PlanningKey, out var baseline)) continue;
                if (baseline.StartUtc > horizonEnd || baseline.EndUtc <= context.ReferenceTimeUtc) continue;
                if (seedResources.Contains(baseline.ResourceId)) affected.Add(identity.TaskId);
            }
        }

        return affected;
    }

    private sealed record ResourceOverrideApplicationResult(
        IReadOnlyCollection<FiniteScheduleTask> Tasks,
        IReadOnlyCollection<PlanningIssue> Issues);
}
