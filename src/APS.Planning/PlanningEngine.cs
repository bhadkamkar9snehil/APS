using APS.Application;
using APS.Domain;

namespace APS.Planning;

public sealed class PlanningEngine(
    ICampaignPlanningService campaignPlanning,
    IProductionStructurePlanningService structurePlanning,
    IFiniteScheduleOptimizer scheduleOptimizer) : IPlanningEngine
{
    public PlanningRunResult Run(PlanningRunRequest request)
    {
        var createdOnUtc = DateTime.UtcNow;
        var planVersionId = Guid.NewGuid();
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

        var campaignPlan = campaignPlanning.FormCampaigns(new CampaignPlanningRequest(
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
            request.HorizonStartUtc));

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

            structure = RollingFeedProjector.Apply(
                structure,
                campaignPlan,
                request.RoutePlanning,
                request.Resources,
                request.Capabilities,
                request.FlowLinks,
                request.ExternalMaterialSupplies);
            if (HasErrors(structure))
                return InvalidStructureResult(planVersionId, createdOnUtc, campaignPlan, structure, request.ReplanContext?.BaselinePlanVersionId, requirementSnapshots);

            structure = MultiStageRouteProjector.Apply(
                structure,
                request.RoutePlanning,
                request.Resources,
                effectiveTransitionRules,
                request.FlowLinks);
            if (HasErrors(structure))
                return InvalidStructureResult(planVersionId, createdOnUtc, campaignPlan, structure, request.ReplanContext?.BaselinePlanVersionId, requirementSnapshots);
        }

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
            materialPreSchedule.ScheduleEvents));

        var materialPlan = finiteSchedule.IsFeasible
            ? TimePhasedMaterialPlanner.ResolveAfterSchedule(planVersionId, request, campaignPlan, materialPreSchedule, finiteSchedule)
            : materialPreSchedule;

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

        var feasible = finiteSchedule.IsFeasible && !materialPlan.Issues.Any(x => x.Severity == PlanningIssueSeverity.Error);

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

            // Local repair: everything outside the affected dependency/resource neighborhood remains exact.
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
        var identityByKey = identities.ToDictionary(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase);
        var identityByTask = identities.ToDictionary(x => x.TaskId);
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
