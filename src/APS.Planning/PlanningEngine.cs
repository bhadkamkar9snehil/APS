using APS.Application;

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

        var campaignPlan = campaignPlanning.FormCampaigns(new CampaignPlanningRequest(
            request.ProductionOrders,
            request.Inventory,
            request.CampaignPolicy,
            request.CampaignNumberPrefix,
            request.Resources,
            request.Capabilities,
            request.SteelGrades,
            request.ExternalMaterialSupplies));

        var structureRequest = new ProductionStructurePlanningRequest(
            campaignPlan.Campaigns,
            request.Resources,
            request.Capabilities,
            request.TransitionRules,
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
        {
            return InvalidStructureResult(planVersionId, createdOnUtc, campaignPlan, structure, request.ReplanContext?.BaselinePlanVersionId);
        }

        structure = HeatLevelScheduleProjector.Apply(
            structure,
            request.Resources,
            request.Capabilities,
            request.FlowLinks,
            request.StructurePolicy);

        if (HasErrors(structure))
        {
            return InvalidStructureResult(planVersionId, createdOnUtc, campaignPlan, structure, request.ReplanContext?.BaselinePlanVersionId);
        }

        if (request.RoutePlanning is not null)
        {
            structure = SteelmakingRouteProjector.Apply(
                structure,
                request.RoutePlanning,
                request.Resources,
                request.Capabilities,
                request.FlowLinks,
                request.SteelGrades);

            if (HasErrors(structure))
            {
                return InvalidStructureResult(planVersionId, createdOnUtc, campaignPlan, structure, request.ReplanContext?.BaselinePlanVersionId);
            }

            structure = MultiStageRouteProjector.Apply(
                structure,
                request.RoutePlanning,
                request.Resources,
                request.TransitionRules);

            if (HasErrors(structure))
            {
                return InvalidStructureResult(planVersionId, createdOnUtc, campaignPlan, structure, request.ReplanContext?.BaselinePlanVersionId);
            }
        }

        var identities = PlanningTaskIdentityService.Build(structure);
        var stabilityConstraints = BuildStabilityConstraints(request, structure.SchedulingTasks, identities);

        var finiteSchedule = scheduleOptimizer.Solve(new FiniteScheduleRequest(
            request.HorizonStartUtc,
            request.HorizonEndUtc,
            structure.SchedulingTasks,
            request.Resources,
            request.ResourceCalendars,
            request.TransitionRules,
            request.MaxSolverSeconds,
            stabilityConstraints));

        return new PlanningRunResult(
            planVersionId,
            createdOnUtc,
            campaignPlan,
            structure,
            finiteSchedule,
            finiteSchedule.IsFeasible,
            identities,
            request.ReplanContext?.BaselinePlanVersionId);
    }

    private static bool HasErrors(ProductionStructurePlanningResult structure) =>
        structure.Issues.Any(i => i.Severity == PlanningIssueSeverity.Error);

    private static PlanningRunResult InvalidStructureResult(
        Guid planVersionId,
        DateTime createdOnUtc,
        CampaignPlanningResult campaignPlan,
        ProductionStructurePlanningResult structure,
        Guid? baselinePlanVersionId)
    {
        var schedule = new FiniteScheduleResult(
            "StructureInvalid",
            false,
            0,
            Array.Empty<FiniteScheduleAssignment>(),
            structure.Issues);

        return new PlanningRunResult(
            planVersionId,
            createdOnUtc,
            campaignPlan,
            structure,
            schedule,
            false,
            Array.Empty<PlanningTaskIdentity>(),
            baselinePlanVersionId);
    }

    private static IReadOnlyCollection<FiniteScheduleStabilityConstraint> BuildStabilityConstraints(
        PlanningRunRequest request,
        IReadOnlyCollection<FiniteScheduleTask> tasks,
        IReadOnlyCollection<PlanningTaskIdentity> identities)
    {
        var context = request.ReplanContext;
        if (context is null || context.BaselineOperations.Count == 0)
        {
            return Array.Empty<FiniteScheduleStabilityConstraint>();
        }

        var taskIds = tasks.Select(x => x.TaskId).ToHashSet();
        var baselineByKey = context.BaselineOperations
            .GroupBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.StartUtc).First(), StringComparer.OrdinalIgnoreCase);
        var policy = context.TimeFencePolicy;
        var constraints = new List<FiniteScheduleStabilityConstraint>();

        foreach (var identity in identities.Where(x => taskIds.Contains(x.TaskId)))
        {
            if (!baselineByKey.TryGetValue(identity.PlanningKey, out var baseline)) continue;
            if (baseline.EndUtc <= context.ReferenceTimeUtc) continue;

            var minutesToStart = (baseline.StartUtc - context.ReferenceTimeUtc).TotalMinutes;
            var zone = minutesToStart <= policy.FrozenMinutes
                ? TimeFenceZone.Frozen
                : minutesToStart <= policy.FrozenMinutes + policy.SlushyMinutes
                    ? TimeFenceZone.Slushy
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
}
