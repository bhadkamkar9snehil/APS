using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class PlanningControlAssumptionTests
{
    [Fact]
    public async Task Plan_version_persists_full_planner_control_profile()
    {
        await using var db = new ApsDbContext(new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-planner-controls-{Guid.NewGuid():N}")
            .Options);
        var repository = new PlanVersionRepository(db);
        var now = new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc);
        var weights = new CampaignObjectiveWeights(1600m, 2m, 65m, 6m, 11m)
        {
            GradeTransitionCostWeight = 3m,
            HeatTargetDeviationPerMt = 4m,
            CampaignStabilityChangePerMt = 5m
        };
        var campaign = new CampaignPlanningPolicy(78m, 68m, 86m, 430m, 520m, false, true, 98m, weights);
        var structure = new ProductionStructurePlanningPolicy(6, 49, 750, 97m, 110, false, true);
        var fence = new PlanningTimeFencePolicy(240, 840, 75, 6500);
        var assignment = new[] { new OperationAssignmentPolicy(ProcessOperationType.Ccm, 100, 25, true, false, true) };
        var repair = new RepairScopePolicy(3, 480, true, false);
        var scenario = new PlanningScenario { ScenarioCode = "CCM-DERATED", Name = "CCM derated" };

        var planningRequest = new PlanningRunRequest(
            Array.Empty<ProductionOrder>(),
            Array.Empty<InventoryPosition>(),
            Array.Empty<Resource>(),
            Array.Empty<ResourceCapability>(),
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            Array.Empty<PlantFlowLink>(),
            campaign,
            structure,
            now,
            now.AddDays(7),
            47,
            ReplanContext: new PlanningReplanContext(Guid.NewGuid(), now, fence, Array.Empty<BaselinePlanOperation>(), RepairScope: repair),
            AssignmentPolicies: assignment,
            Scenario: scenario);

        var result = new PlanningRunResult(
            Guid.NewGuid(),
            now,
            new CampaignPlanningResult(
                Array.Empty<Campaign>(),
                Array.Empty<ProductionOrder>(),
                new Dictionary<Guid, decimal>(),
                new Dictionary<Guid, decimal>(),
                new Dictionary<Guid, decimal>(),
                Array.Empty<PlanningInventoryAllocation>()),
            new ProductionStructurePlanningResult(
                Array.Empty<CastSequence>(),
                Array.Empty<RollingPlan>(),
                Array.Empty<PlannedBilletSupply>(),
                Array.Empty<FiniteScheduleTask>(),
                Array.Empty<PlanningIssue>()),
            new FiniteScheduleResult("Optimal", true, 0, Array.Empty<FiniteScheduleAssignment>(), Array.Empty<PlanningIssue>()),
            true);

        var saved = await repository.SaveAsync(new PersistPlanningRunRequest(planningRequest, result, PlanTriggerType.Manual, now));
        var reloaded = await repository.GetAsync(saved.PlanVersionId);
        var assumptions = reloaded!.Assumptions!;

        Assert.Equal(78m, assumptions.CampaignPolicy!.NominalHeatSizeMt);
        Assert.Equal(520m, assumptions.CampaignPolicy.MaximumCampaignQuantityMt);
        Assert.Null(assumptions.CampaignPolicy.BaselineCampaignAllocations);
        Assert.Equal(6, assumptions.StructurePolicy!.MaximumHeatsPerCastSequence);
        Assert.Equal(240, assumptions.TimeFencePolicy!.FrozenMinutes);
        Assert.Equal(840, assumptions.TimeFencePolicy.SlushyMinutes);
        Assert.Equal(47, assumptions.MaxSolverSeconds);
        Assert.Equal("CCM-DERATED", assumptions.ScenarioCode);
        Assert.Equal(100, Assert.Single(assumptions.AssignmentPolicies!).FirmMinutesBeforeStart);
        Assert.Equal(480, assumptions.RepairScopePolicy!.RepairHorizonMinutes);
        Assert.Equal(3m, assumptions.CampaignObjectiveWeights.GradeTransitionCostWeight);
    }
}
