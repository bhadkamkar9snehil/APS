using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace APS.Planning.Tests;

public sealed class PlanningLifecycleControlInheritanceTests
{
    private static readonly DateTime HorizonStart = new(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime HorizonEnd = HorizonStart.AddDays(7);

    [Fact]
    public async Task Calculate_rejects_unknown_operating_scenario_instead_of_silently_using_baseline()
    {
        var engine = new CapturingEngine();
        var lifecycle = NewLifecycle(engine, new FakePlanRepository(), Masters());
        var request = CalculationRequest() with { ScenarioCode = "SCENARIO-THAT-NO-LONGER-EXISTS" };

        var exception = await Assert.ThrowsAsync<PlanningConfigurationException>(() => lifecycle.CalculateAsync(request));

        Assert.Contains(exception.Issues, x => x.Contains("was not found", StringComparison.OrdinalIgnoreCase));
        Assert.Null(engine.LastRequest);
    }

    [Fact]
    public async Task Operational_replan_preserves_meaningful_null_baseline_scenario_and_assignment_policy()
    {
        var scenario = new PlanningScenario { ScenarioCode = "CCM-DOWN", Name = "CCM down" };
        var legacyWeights = CampaignObjectiveWeights.Default with { EarlyProductionPerMtDay = 17m };
        var baselineAssumptions = new PlanningAssumptions(
            ScenarioCode: null,
            CampaignObjectiveWeights: legacyWeights,
            CampaignCompositionDecisions: Array.Empty<CampaignCompositionDecision>(),
            ResourceScheduling: Array.Empty<ResourceSchedulingAssumption>(),
            TimeFencePolicy: new PlanningTimeFencePolicy(360, 900, 88, 7700),
            AssignmentPolicies: null);
        var baseline = Baseline(baselineAssumptions);
        var plans = new FakePlanRepository(baseline);
        var engine = new CapturingEngine();
        var lifecycle = NewLifecycle(engine, plans, Masters(new[] { scenario }));
        var staleCallerAssignment = new[] { new OperationAssignmentPolicy(ProcessOperationType.Ccm, FirmMinutesBeforeStart: 999) };
        var request = CalculationRequest() with
        {
            ScenarioCode = scenario.ScenarioCode,
            AssignmentPolicies = staleCallerAssignment,
            CampaignPolicy = CalculationRequest().CampaignPolicy with
            {
                ObjectiveWeights = CampaignObjectiveWeights.Default with { EarlyProductionPerMtDay = 999m }
            }
        };

        await lifecycle.ReplanAsync(
            baseline.PlanVersionId,
            new PlanningRecalculationRequest(
                request,
                new PlanningTimeFencePolicy(1, 1),
                UseBaselinePlanningControls: true));

        Assert.NotNull(engine.LastRequest);
        Assert.Null(engine.LastRequest!.Scenario);
        Assert.Null(engine.LastRequest.AssignmentPolicies);
        Assert.Equal(17m, engine.LastRequest.CampaignPolicy.ObjectiveWeights!.EarlyProductionPerMtDay);
        Assert.Equal(360, engine.LastRequest.ReplanContext!.TimeFencePolicy.FrozenMinutes);
        Assert.Equal(900, engine.LastRequest.ReplanContext.TimeFencePolicy.SlushyMinutes);
    }

    [Fact]
    public async Task Intentional_planner_what_if_uses_current_controls_instead_of_baseline_controls()
    {
        var scenario = new PlanningScenario { ScenarioCode = "CCM-DOWN", Name = "CCM down" };
        var baselineAssumptions = new PlanningAssumptions(
            ScenarioCode: null,
            CampaignObjectiveWeights: CampaignObjectiveWeights.Default,
            CampaignCompositionDecisions: Array.Empty<CampaignCompositionDecision>(),
            ResourceScheduling: Array.Empty<ResourceSchedulingAssumption>(),
            CampaignPolicy: new CampaignPlanningPolicy(70m, 60m, 80m, 400m, 500m),
            TimeFencePolicy: new PlanningTimeFencePolicy(120, 720));
        var baseline = Baseline(baselineAssumptions);
        var plans = new FakePlanRepository(baseline);
        var engine = new CapturingEngine();
        var lifecycle = NewLifecycle(engine, plans, Masters(new[] { scenario }));
        var current = CalculationRequest() with
        {
            ScenarioCode = scenario.ScenarioCode,
            CampaignPolicy = new CampaignPlanningPolicy(92m, 80m, 105m, 650m, 800m)
        };

        await lifecycle.ReplanAsync(
            baseline.PlanVersionId,
            new PlanningRecalculationRequest(
                current,
                new PlanningTimeFencePolicy(300, 1000),
                UseBaselinePlanningControls: false));

        Assert.NotNull(engine.LastRequest);
        Assert.Equal("CCM-DOWN", engine.LastRequest!.Scenario?.ScenarioCode);
        Assert.Equal(92m, engine.LastRequest.CampaignPolicy.NominalHeatSizeMt);
        Assert.Equal(300, engine.LastRequest.ReplanContext!.TimeFencePolicy.FrozenMinutes);
    }

    private static PlanningLifecycleService NewLifecycle(
        IPlanningEngine engine,
        IPlanVersionRepository plans,
        PlanningMasterDataSnapshot masters) => new(
            engine,
            plans,
            new FakeMasterProvider(masters),
            new EmptyInventoryProvider(),
            new EmptyActualStateProvider(),
            new EmptyDemandService(),
            NullLogger<PlanningLifecycleService>.Instance);

    private static PlanningCalculationRequest CalculationRequest() => new(
        new PlanningDemandSelection(),
        new CampaignPlanningPolicy(90m, 70m, 105m, 500m, 650m),
        new ProductionStructurePlanningPolicy(),
        HorizonStart,
        HorizonEnd,
        20);

    private static PlanningMasterDataSnapshot Masters(IReadOnlyCollection<PlanningScenario>? scenarios = null)
    {
        var resource = new Resource
        {
            PlantId = Guid.NewGuid(),
            ProcessStageId = Guid.NewGuid(),
            Code = "RM-1",
            Name = "Rolling mill 1",
            ResourceType = ResourceType.RollingMill,
            ProcessUnitType = ProcessUnitType.HotRollingMill
        };
        var route = new ManufacturingRoute { RouteCode = "STD-BAR", Name = "Standard bar" };
        var operation = new ManufacturingRouteOperation
        {
            ManufacturingRouteId = route.Id,
            RouteCode = route.RouteCode,
            SequenceNumber = 1,
            ProcessOperationType = ProcessOperationType.HotRoll,
            ReleaseWorkOrderType = WorkOrderType.HotRolling
        };

        return new PlanningMasterDataSnapshot(
            Array.Empty<Plant>(),
            Array.Empty<ProcessStage>(),
            new[] { resource },
            Array.Empty<ResourceCapability>(),
            Array.Empty<ResourceCalendar>(),
            Array.Empty<PlantFlowLink>(),
            Array.Empty<TransitionRule>(),
            new[] { route },
            new[] { operation },
            Array.Empty<RouteResourceCapability>(),
            PlanningScenarios: scenarios);
    }

    private static PlanVersionSnapshot Baseline(PlanningAssumptions assumptions) => new(
        Guid.NewGuid(),
        "PLAN-BASELINE",
        null,
        PlanVersionStatus.Feasible,
        PlanTriggerType.Manual,
        HorizonStart,
        HorizonStart,
        HorizonStart,
        HorizonEnd,
        "Optimal",
        0,
        true,
        Array.Empty<BaselinePlanOperation>(),
        Assumptions: assumptions);

    private sealed class CapturingEngine : IPlanningEngine
    {
        public PlanningRunRequest? LastRequest { get; private set; }

        public PlanningRunResult Run(PlanningRunRequest request)
        {
            LastRequest = request;
            return new PlanningRunResult(
                Guid.NewGuid(),
                HorizonStart,
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
        }
    }

    private sealed class FakePlanRepository(PlanVersionSnapshot? baseline = null) : IPlanVersionRepository
    {
        private readonly Dictionary<Guid, PlanVersionSnapshot> versions = baseline is null
            ? new()
            : new() { [baseline.PlanVersionId] = baseline };

        public Task<PlanVersionSnapshot> SaveAsync(PersistPlanningRunRequest request, CancellationToken cancellationToken = default)
        {
            var result = request.PlanningResult;
            var snapshot = new PlanVersionSnapshot(
                result.PlanVersionId,
                $"PLAN-{result.PlanVersionId:N}",
                result.BaselinePlanVersionId,
                result.IsFeasible ? PlanVersionStatus.Feasible : PlanVersionStatus.Failed,
                request.Trigger,
                result.CreatedOnUtc,
                request.ReferenceTimeUtc,
                request.PlanningRequest.HorizonStartUtc,
                request.PlanningRequest.HorizonEndUtc,
                result.Schedule.SolverStatus,
                result.Schedule.ObjectiveValue,
                result.IsFeasible,
                Array.Empty<BaselinePlanOperation>());
            versions[snapshot.PlanVersionId] = snapshot;
            return Task.FromResult(snapshot);
        }

        public Task<PlanVersionSnapshot?> GetAsync(Guid planVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(versions.TryGetValue(planVersionId, out var snapshot) ? snapshot : null);

        public Task<IReadOnlyCollection<BaselinePlanOperation>> GetBaselineOperationsAsync(Guid planVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<BaselinePlanOperation>>(Array.Empty<BaselinePlanOperation>());

        public Task<IReadOnlyCollection<BaselineCampaignAllocation>> GetBaselineCampaignAllocationsAsync(Guid planVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<BaselineCampaignAllocation>>(Array.Empty<BaselineCampaignAllocation>());
    }

    private sealed class FakeMasterProvider(PlanningMasterDataSnapshot snapshot) : IPlanningMasterDataProvider
    {
        public Task<PlanningMasterDataSnapshot> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    private sealed class EmptyInventoryProvider : IInventorySnapshotProvider
    {
        public Task<IReadOnlyCollection<InventoryPosition>> GetInventoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<InventoryPosition>>(Array.Empty<InventoryPosition>());
    }

    private sealed class EmptyActualStateProvider : IReplanningActualStateProvider
    {
        public Task<ReplanningActualState> GetAsync(
            Guid baselinePlanVersionId,
            DateTime referenceTimeUtc,
            IReadOnlyCollection<BaselinePlanOperation> baselineOperations,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReplanningActualState(
                baselineOperations,
                Array.Empty<InventoryPosition>(),
                Array.Empty<string>(),
                Array.Empty<string>()));
    }

    private sealed class EmptyDemandService : IProductionDemandOrchestrationService
    {
        public Task<SalesOrderReconciliationResult> ReconcileSalesOrdersAsync(IReadOnlyCollection<SalesOrderDemandInput> salesOrders, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesOrderReconciliationResult(0, 0, 0, 0, Array.Empty<Guid>()));

        public Task<DemandOrchestrationResult> PrepareAsync(
            PlanningDemandSelection selection,
            IReadOnlyCollection<InventoryPosition> inventory,
            PlanningMasterDataSnapshot masters,
            DateTime referenceTimeUtc,
            DateTime horizonEndUtc,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DemandOrchestrationResult(
                Array.Empty<ProductionOrder>(),
                Array.Empty<DemandOrchestrationItem>(),
                Array.Empty<ProductionOrder>(),
                Array.Empty<PlanningIssue>()));

        public Task<IReadOnlyCollection<DemandOrchestrationItem>> GetCurrentMtoDemandAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<DemandOrchestrationItem>>(Array.Empty<DemandOrchestrationItem>());
    }
}
