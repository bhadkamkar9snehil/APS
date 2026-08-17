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
            request.CampaignNumberPrefix));

        var structure = structurePlanning.Build(new ProductionStructurePlanningRequest(
            campaignPlan.Campaigns,
            request.Resources,
            request.Capabilities,
            request.TransitionRules,
            request.FlowLinks,
            request.StructurePolicy));

        if (structure.Issues.Any(i => i.Severity == PlanningIssueSeverity.Error))
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
                false);
        }

        var finiteSchedule = scheduleOptimizer.Solve(new FiniteScheduleRequest(
            request.HorizonStartUtc,
            request.HorizonEndUtc,
            structure.SchedulingTasks,
            request.Resources,
            request.ResourceCalendars,
            request.TransitionRules,
            request.MaxSolverSeconds));

        return new PlanningRunResult(
            planVersionId,
            createdOnUtc,
            campaignPlan,
            structure,
            finiteSchedule,
            finiteSchedule.IsFeasible);
    }
}
