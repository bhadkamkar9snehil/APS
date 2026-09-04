using APS.Application;
using APS.UI.State;

namespace APS.UI.Tests;

public sealed class PlannerConstraintStateTests
{
    [Fact]
    public void Defaults_build_a_real_planning_request_and_time_fence()
    {
        var state = new PlannerConstraintState();
        var start = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc);
        var request = state.BuildCalculationRequest(start, start.AddDays(7));
        var fence = state.BuildTimeFencePolicy();

        Assert.Equal(90m, request.CampaignPolicy.NominalHeatSizeMt);
        Assert.Equal(63m, request.CampaignPolicy.MinimumHeatSizeMt);
        Assert.Equal(103.5m, request.CampaignPolicy.MaximumHeatSizeMt);
        Assert.Equal(600m, request.CampaignPolicy.MaximumCampaignQuantityMt);
        Assert.Equal(8, request.StructurePolicy.MaximumHeatsPerCastSequence);
        Assert.Equal(20, request.MaxSolverSeconds);
        Assert.Null(request.ScenarioCode);
        Assert.Null(request.AssignmentPolicies);
        Assert.Equal(120, fence.FrozenMinutes);
        Assert.Equal(720, fence.SlushyMinutes);
    }

    [Fact]
    public void Planner_changes_flow_into_calculation_contract()
    {
        var state = new PlannerConstraintState
        {
            NominalHeatSizeMt = 75m,
            MinimumHeatSizeMt = 65m,
            MaximumHeatSizeMt = 82m,
            TargetCampaignQuantityMt = 420m,
            MaximumCampaignQuantityMt = 500m,
            ScenarioCode = "CCM-OUTAGE",
            MaxSolverSeconds = 45,
            FrozenMinutes = 180,
            SlushyMinutes = 600,
            UseAssignmentCommitmentPolicy = true,
            FirmMinutesBeforeStart = 90,
            CommitMinutesBeforeStart = 20
        };

        var start = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc);
        var request = state.BuildCalculationRequest(start, start.AddDays(5));

        Assert.Equal(75m, request.CampaignPolicy.NominalHeatSizeMt);
        Assert.Equal(420m, request.CampaignPolicy.TargetCampaignQuantityMt);
        Assert.Equal("CCM-OUTAGE", request.ScenarioCode);
        Assert.Equal(45, request.MaxSolverSeconds);
        Assert.NotNull(request.AssignmentPolicies);
        Assert.All(request.AssignmentPolicies!, policy =>
        {
            Assert.Equal(90, policy.FirmMinutesBeforeStart);
            Assert.Equal(20, policy.CommitMinutesBeforeStart);
        });
        Assert.Equal(180, state.BuildTimeFencePolicy().FrozenMinutes);
        Assert.Equal(600, state.BuildTimeFencePolicy().SlushyMinutes);
    }

    [Fact]
    public void Invalid_profile_is_rejected_before_planning()
    {
        var state = new PlannerConstraintState
        {
            MinimumHeatSizeMt = 100m,
            NominalHeatSizeMt = 90m,
            MaximumCampaignQuantityMt = 50m,
            CommitMinutesBeforeStart = 180,
            FirmMinutesBeforeStart = 120,
            UseAssignmentCommitmentPolicy = true
        };

        var issues = state.Validate();

        Assert.Contains(issues, x => x.Contains("Nominal heat", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, x => x.Contains("Maximum campaign", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, x => x.Contains("Commit window", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Persisted_assumptions_can_rehydrate_the_planner_profile()
    {
        var campaign = new CampaignPlanningPolicy(80m, 70m, 88m, 450m, 520m, false, false, 98m);
        var structure = new ProductionStructurePlanningPolicy(6, 48, 700, 97m, 105, false, true);
        var fence = new PlanningTimeFencePolicy(240, 900, 70, 6000);
        var assumptions = new PlanningAssumptions(
            "DERATED",
            new CampaignObjectiveWeights(1500m, 2m, 60m, 5m, 9m),
            Array.Empty<CampaignCompositionDecision>(),
            Array.Empty<ResourceSchedulingAssumption>(),
            CampaignPolicy: campaign,
            StructurePolicy: structure,
            TimeFencePolicy: fence,
            MaxSolverSeconds: 55);

        var state = new PlannerConstraintState();
        state.ApplyAssumptions(assumptions);

        Assert.Equal("DERATED", state.ScenarioCode);
        Assert.Equal(80m, state.NominalHeatSizeMt);
        Assert.Equal(6, state.MaximumHeatsPerCastSequence);
        Assert.Equal(240, state.FrozenMinutes);
        Assert.Equal(55, state.MaxSolverSeconds);
        Assert.Equal(1500m, state.ServiceRiskPerMtDay);
    }
}
