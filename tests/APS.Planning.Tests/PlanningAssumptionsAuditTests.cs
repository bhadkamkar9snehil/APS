using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

/// <summary>
/// A plan has to be able to say what it was solved under (#15/#17/#35). Resource masters, operating
/// scenarios and campaign objective weights all change after a plan is cut; without capturing them a
/// plan cannot be explained a week later, and Plan Compare cannot tell an assumption change apart
/// from a demand change.
/// </summary>
public sealed class PlanningAssumptionsAuditTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Plan_version_records_the_scenario_weights_and_scheduling_modes_it_was_solved_under()
    {
        await using var db = CreateDb();
        var repository = new PlanVersionRepository(db);

        var furnace = new Resource
        {
            PlantId = Guid.NewGuid(),
            ProcessStageId = Guid.NewGuid(),
            Code = "RHF-1",
            Name = "Reheating furnace 1",
            ResourceType = ResourceType.Furnace,
            ProcessUnitType = ProcessUnitType.ReheatingFurnace,
            SchedulingMode = ResourceSchedulingMode.Cumulative,
            CapacityBasis = ResourceCapacityBasis.Slots,
            NominalConcurrentCapacity = 4m,
            CapacityFactorPct = 80m,
            AppliesSequenceRules = false
        };
        var mill = new Resource
        {
            PlantId = furnace.PlantId,
            ProcessStageId = Guid.NewGuid(),
            Code = "RM-1",
            Name = "Rolling mill 1",
            ResourceType = ResourceType.RollingMill
        };

        var weights = CampaignObjectiveWeights.Default with { EarlyProductionPerMtDay = 7m };
        var decision = new CampaignCompositionDecision(
            "G1|SEQ-A|150X150",
            new CampaignObjectiveBreakdown("SERVICE_WINDOW_03D", 2, 0m, 120m, 10m, 0m, 980m),
            new[]
            {
                new CampaignObjectiveBreakdown("SERVICE_WINDOW_03D", 2, 0m, 120m, 10m, 0m, 980m),
                new CampaignObjectiveBreakdown("FILL_TO_CAPACITY", 1, 0m, 4200m, 0m, 0m, 29440m)
            });

        var saved = await repository.SaveAsync(new PersistPlanningRunRequest(
            PlanningRequest(new[] { furnace, mill }, weights, ScenarioCode("SMS-DOWN")),
            PlanningResult(new[] { decision }),
            PlanTriggerType.Manual,
            Now,
            "Assumption capture"));

        var assumptions = saved.Assumptions;
        Assert.NotNull(assumptions);
        Assert.Equal("SMS-DOWN", assumptions.ScenarioCode);
        Assert.Equal(7m, assumptions.CampaignObjectiveWeights.EarlyProductionPerMtDay);

        // The rejected alternative is part of the record: without it the plan says what it did but not
        // what it declined to do.
        var recordedDecision = Assert.Single(assumptions.CampaignCompositionDecisions);
        Assert.Equal("SERVICE_WINDOW_03D", recordedDecision.Selected.StrategyCode);
        Assert.Contains(recordedDecision.Considered, x => x.StrategyCode == "FILL_TO_CAPACITY");

        var recordedFurnace = Assert.Single(assumptions.ResourceScheduling, x => x.ResourceCode == "RHF-1");
        Assert.Equal(ResourceSchedulingMode.Cumulative, recordedFurnace.SchedulingMode);
        Assert.Equal(ResourceCapacityBasis.Slots, recordedFurnace.CapacityBasis);
        Assert.Equal(4m, recordedFurnace.NominalConcurrentCapacity);
        // The derating is recorded too - the same furnace at 100% is a different plant.
        Assert.Equal(80m, recordedFurnace.CapacityFactorPct);
        Assert.False(recordedFurnace.AppliesSequenceRules);

        var recordedMill = Assert.Single(assumptions.ResourceScheduling, x => x.ResourceCode == "RM-1");
        Assert.Equal(ResourceSchedulingMode.Disjunctive, recordedMill.SchedulingMode);
    }

    [Fact]
    public async Task Assumptions_survive_a_reload_rather_than_only_existing_in_the_save_response()
    {
        await using var db = CreateDb();
        var repository = new PlanVersionRepository(db);

        var saved = await repository.SaveAsync(new PersistPlanningRunRequest(
            PlanningRequest(Array.Empty<Resource>(), CampaignObjectiveWeights.Default, ScenarioCode("HEAT-WAVE")),
            PlanningResult(),
            PlanTriggerType.Manual,
            Now));

        var reloaded = await repository.GetAsync(saved.PlanVersionId);

        Assert.NotNull(reloaded!.Assumptions);
        Assert.Equal("HEAT-WAVE", reloaded.Assumptions!.ScenarioCode);
    }

    [Fact]
    public async Task Plan_without_a_scenario_records_that_it_used_the_configured_plant()
    {
        await using var db = CreateDb();
        var repository = new PlanVersionRepository(db);

        var saved = await repository.SaveAsync(new PersistPlanningRunRequest(
            PlanningRequest(Array.Empty<Resource>(), CampaignObjectiveWeights.Default, scenario: null),
            PlanningResult(),
            PlanTriggerType.Manual,
            Now));

        // Null is the meaningful value here - the plant as configured, not "we forgot to record it".
        Assert.NotNull(saved.Assumptions);
        Assert.Null(saved.Assumptions!.ScenarioCode);
    }

    [Fact]
    public async Task Billet_thermal_decision_survives_plan_version_reload()
    {
        await using var db = CreateDb();
        var repository = new PlanVersionRepository(db);
        var decision = new BilletThermalDecision(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ROUTE-HOT",
            "G1",
            "150X150",
            BilletThermalSourceBasis.ActualMeasurement,
            1080m,
            Now.AddMinutes(-5),
            1000m,
            1055m,
            5,
            5m,
            16,
            BilletThermalOutcome.HotDirect,
            "ACTUAL_ROLLING_ENTRY_TEMPERATURE_PROVEN",
            "MEASURED_AT_SOURCE_PLUS_SCHEDULED_WAIT",
            false,
            false,
            Array.Empty<string>(),
            Array.Empty<string>());

        var saved = await repository.SaveAsync(new PersistPlanningRunRequest(
            PlanningRequest(Array.Empty<Resource>(), CampaignObjectiveWeights.Default, scenario: null),
            PlanningResult(thermalDecisions: new[] { decision }),
            PlanTriggerType.ExecutionFeedback,
            Now));
        var reloaded = await repository.GetAsync(saved.PlanVersionId);

        var recorded = Assert.Single(reloaded!.Assumptions!.BilletThermalDecisions!);
        Assert.Equal(BilletThermalSourceBasis.ActualMeasurement, recorded.SourceBasis);
        Assert.Equal(1080m, recorded.SourceTemperatureC);
        Assert.Equal(1055m, recorded.PredictedOrActualRollingEntryTemperatureC);
        Assert.Equal("ACTUAL_ROLLING_ENTRY_TEMPERATURE_PROVEN", recorded.ReasonCode);
    }

    private static PlanningScenario ScenarioCode(string code) => new()
    {
        ScenarioCode = code,
        Name = code
    };

    private static PlanningRunRequest PlanningRequest(
        IReadOnlyCollection<Resource> resources,
        CampaignObjectiveWeights weights,
        PlanningScenario? scenario) =>
        new(
            Array.Empty<ProductionOrder>(),
            Array.Empty<InventoryPosition>(),
            resources,
            Array.Empty<ResourceCapability>(),
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            Array.Empty<PlantFlowLink>(),
            new CampaignPlanningPolicy(60m, 50m, 70m, 500m, 1000m, ObjectiveWeights: weights),
            new ProductionStructurePlanningPolicy(),
            Now,
            Now.AddDays(7),
            Scenario: scenario);

    private static PlanningRunResult PlanningResult(
        CampaignCompositionDecision[]? decisions = null,
        IReadOnlyCollection<BilletThermalDecision>? thermalDecisions = null) =>
        new(
            Guid.NewGuid(),
            Now,
            new CampaignPlanningResult(
                Array.Empty<Campaign>(),
                Array.Empty<ProductionOrder>(),
                new Dictionary<Guid, decimal>(),
                new Dictionary<Guid, decimal>(),
                new Dictionary<Guid, decimal>(),
                Array.Empty<PlanningInventoryAllocation>(),
                CompositionDecisions: decisions ?? Array.Empty<CampaignCompositionDecision>()),
            new ProductionStructurePlanningResult(
                Array.Empty<CastSequence>(),
                Array.Empty<RollingPlan>(),
                Array.Empty<PlannedBilletSupply>(),
                Array.Empty<FiniteScheduleTask>(),
                Array.Empty<PlanningIssue>(),
                BilletThermalDecisions: thermalDecisions),
            new FiniteScheduleResult(
                "Optimal",
                true,
                0,
                Array.Empty<FiniteScheduleAssignment>(),
                Array.Empty<PlanningIssue>()),
            true);

    private static ApsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-plan-assumptions-{Guid.NewGuid():N}")
            .Options;
        return new ApsDbContext(options);
    }
}
