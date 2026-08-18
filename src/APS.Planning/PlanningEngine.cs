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
        if (request.ExecutionMode == PlanningExecutionMode.Production && request.RoutePlanning is null)
        {
            throw new PlanningConfigurationException(
                "Production planning requires configured manufacturing-route operations. " +
                "The simplified production-structure path is compatibility/demo behavior only.");
        }

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
            request.HorizonStartUtc,
            request.RoutePlanning,
            request.CommittedMaterialSupplies,
            request.FlowLinks));

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
            materialPreSchedule.ScheduledEvents));

        var resourceAlternatives = PlanningResourceAlternativeProjector.Build(
            originalTasks,
            finiteSchedule.Assignments,
            identities);

        var materialPlan = TimePhasedMaterialPlanner.ResolvePostSchedule(
            request,
            campaignPlan,
            structure,
            finiteSchedule,
            materialPreSchedule);

        var packagePlan = PackagingPlanningService.Build(
            request.ProductionOrders,
            request.MaterialSpecifications,
            request.PackagingSpecifications,
            finiteSchedule,
            structure);

        return new PlanningRunResult(
            planVersionId,
            createdOnUtc,
            campaignPlan,
            structure,
            finiteSchedule,
            finiteSchedule.IsFeasible,
            identities,
            request.ReplanContext?.BaselinePlanVersionId,
            packagePlan,
            requirementSnapshots,
            resourceAlternatives,
            materialPlan);
    }

    private static bool HasErrors(ProductionStructurePlanningResult result) =>
        result.Issues.Any(i => i.Severity == PlanningIssueSeverity.Error);

    private static PlanningRunResult InvalidStructureResult(
        Guid planVersionId,
        DateTime createdOnUtc,
        CampaignPlanningResult campaignPlan,
        ProductionStructurePlanningResult structure,
        Guid? baselinePlanVersionId,
        IReadOnlyCollection<PlanOrderRequirementSnapshot>? requirementSnapshots = null)
    {
        var errors = structure.Issues
            .Where(i => i.Severity == PlanningIssueSeverity.Error)
            .Select(i => $"{i.Code}: {i.Message}")
            .ToArray();
        var finite = new FiniteScheduleResult(
            "NOT_SOLVED",
            false,
            0,
            Array.Empty<FiniteScheduleAssignment>(),
            errors.Select(message => new PlanningIssue(
                PlanningIssueSeverity.Error,
                "STRUCTURE_INFEASIBLE",
                message)).ToArray());
        return new PlanningRunResult(
            planVersionId,
            createdOnUtc,
            campaignPlan,
            structure,
            finite,
            false,
            baselinePlanVersionId: baselinePlanVersionId,
            RequirementSnapshots: requirementSnapshots);
    }

    private static IReadOnlyCollection<FiniteScheduleStabilityConstraint>? BuildStabilityConstraints(
        PlanningRunRequest request,
        IReadOnlyCollection<FiniteScheduleTask> tasks,
        IReadOnlyCollection<PlanningTaskIdentity> identities)
    {
        var context = request.ReplanContext;
        if (context is null || context.BaselineOperations.Count == 0) return null;

        var identityByTaskId = identities.ToDictionary(x => x.TaskId);
        var baselineByKey = context.BaselineOperations.ToDictionary(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase);
        var constraints = new List<FiniteScheduleStabilityConstraint>();

        foreach (var task in tasks)
        {
            if (!identityByTaskId.TryGetValue(task.TaskId, out var identity)) continue;
            if (!baselineByKey.TryGetValue(identity.PlanningKey, out var baseline)) continue;

            var minutesUntilBaselineStart = (baseline.StartUtc - context.ReferenceTimeUtc).TotalMinutes;
            var zone = minutesUntilBaselineStart <= context.TimeFencePolicy.FrozenMinutes
                ? TimeFenceZone.Frozen
                : minutesUntilBaselineStart <= context.TimeFencePolicy.SlushyMinutes
                    ? TimeFenceZone.Slushy
                    : TimeFenceZone.Liquid;

            constraints.Add(new FiniteScheduleStabilityConstraint(
                task.TaskId,
                zone,
                baseline.ResourceId,
                baseline.StartUtc,
                context.TimeFencePolicy.SlushyMovementPenaltyPerMinute,
                context.TimeFencePolicy.SlushyResourceChangePenalty));
        }

        return constraints;
    }

    private static ResourceOverrideApplicationResult ApplyResourceOverrides(
        IReadOnlyCollection<FiniteScheduleTask> tasks,
        IReadOnlyCollection<PlanningTaskIdentity> identities,
        IReadOnlyCollection<OperationResourceOverride>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
            return new ResourceOverrideApplicationResult(tasks, Array.Empty<PlanningIssue>());

        var identityByTask = identities.ToDictionary(x => x.TaskId);
        var overrideByKey = overrides
            .GroupBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);
        var issues = new List<PlanningIssue>();
        var output = new List<FiniteScheduleTask>(tasks.Count);

        foreach (var task in tasks)
        {
            if (!identityByTask.TryGetValue(task.TaskId, out var identity) ||
                !overrideByKey.TryGetValue(identity.PlanningKey, out var resourceOverride))
            {
                output.Add(task);
                continue;
            }

            var selected = task.ResourceOptions.FirstOrDefault(x => x.ResourceId == resourceOverride.ResourceId);
            if (selected is null)
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "RESOURCE_OVERRIDE_NOT_ELIGIBLE",
                    $"Operation {identity.PlanningKey} cannot be dispatched to resource {resourceOverride.ResourceId}; that resource was not an eligible alternative.",
                    identity.SourceEntityId));
                output.Add(task);
                continue;
            }

            output.Add(task with { ResourceOptions = new[] { selected } });
        }

        foreach (var resourceOverride in overrideByKey.Values)
        {
            if (identities.All(x => !string.Equals(x.PlanningKey, resourceOverride.PlanningKey, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "RESOURCE_OVERRIDE_OPERATION_NOT_FOUND",
                    $"Resource override references planning operation {resourceOverride.PlanningKey}, which does not exist in the recalculated plan."));
            }
        }

        return new ResourceOverrideApplicationResult(output, issues);
    }

    private sealed record ResourceOverrideApplicationResult(
        IReadOnlyCollection<FiniteScheduleTask> Tasks,
        IReadOnlyCollection<PlanningIssue> Issues);
}
