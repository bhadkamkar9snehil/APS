using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace APS.Planning.Tests;

public sealed class CanonicalBackendBoundaryTests
{
    [Fact]
    public async Task Production_lifecycle_derives_demand_before_the_production_kernel_and_does_not_double_net_fg()
    {
        var resource = NewResource("RM-1", ProcessUnitType.HotRollingMill, ResourceType.RollingMill);
        var routeOperation = new ManufacturingRouteOperation
        {
            ManufacturingRouteId = Guid.NewGuid(),
            RouteCode = "SMS-RM",
            SequenceNumber = 1,
            ProcessOperationType = ProcessOperationType.HotRoll,
            ReleaseWorkOrderType = WorkOrderType.HotRolling,
            OutputCrossSectionCode = "16MM"
        };
        var inventoryRow = new InventoryPosition
        {
            MaterialCode = "FG-16",
            GradeCode = "G1",
            CrossSectionCode = "16MM",
            Stage = InventoryStage.FinishedGoods,
            AvailableQuantityMt = 25m
        };
        var masters = new FakeMasterProvider(new PlanningMasterDataSnapshot(
            Array.Empty<Plant>(),
            Array.Empty<ProcessStage>(),
            new[] { resource },
            Array.Empty<ResourceCapability>(),
            Array.Empty<ResourceCalendar>(),
            Array.Empty<PlantFlowLink>(),
            Array.Empty<TransitionRule>(),
            Array.Empty<ManufacturingRoute>(),
            new[] { routeOperation },
            Array.Empty<RouteResourceCapability>()));
        var inventory = new FakeInventoryProvider(new[] { inventoryRow });
        var demand = new CapturingDemandService();
        var engine = new CapturingPlanningEngine();
        var plans = new FakePlanRepository();
        var lifecycle = NewLifecycle(engine, plans, masters, inventory, demand);

        var outcome = await lifecycle.CalculateAsync(NewCalculationRequest());

        Assert.NotNull(engine.LastRequest);
        Assert.Equal(PlanningExecutionMode.Production, engine.LastRequest!.ExecutionMode);
        Assert.Same(inventoryRow, Assert.Single(demand.LastInventory!));
        Assert.Empty(engine.LastRequest.Inventory); // FG has already been consumed by demand orchestration.
        Assert.Equal(resource.Id, Assert.Single(engine.LastRequest.Resources).Id);
        Assert.NotNull(engine.LastRequest.RoutePlanning);
        Assert.Equal(engine.LastResult!.PlanVersionId, outcome.Version.PlanVersionId);
        Assert.NotNull(plans.LastPersistRequest);
        Assert.Same(demand.LastResult, plans.LastPersistRequest!.Demand);
        Assert.Equal(engine.LastResult.PlanVersionId, plans.LastPersistRequest.PlanningResult.PlanVersionId);
    }

    [Fact]
    public async Task Production_lifecycle_rejects_missing_configured_route_instead_of_using_compatibility_fallback()
    {
        var resource = NewResource("RM-1", ProcessUnitType.HotRollingMill, ResourceType.RollingMill);
        var masters = new FakeMasterProvider(new PlanningMasterDataSnapshot(
            Array.Empty<Plant>(),
            Array.Empty<ProcessStage>(),
            new[] { resource },
            Array.Empty<ResourceCapability>(),
            Array.Empty<ResourceCalendar>(),
            Array.Empty<PlantFlowLink>(),
            Array.Empty<TransitionRule>(),
            Array.Empty<ManufacturingRoute>(),
            Array.Empty<ManufacturingRouteOperation>(),
            Array.Empty<RouteResourceCapability>()));
        var lifecycle = NewLifecycle(
            new CapturingPlanningEngine(),
            new FakePlanRepository(),
            masters,
            new FakeInventoryProvider(Array.Empty<InventoryPosition>()),
            new CapturingDemandService());

        var exception = await Assert.ThrowsAsync<PlanningConfigurationException>(
            () => lifecycle.CalculateAsync(NewCalculationRequest()));

        Assert.Contains(exception.Issues, x => x.Contains("manufacturing-route", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Production_lifecycle_rejects_speculative_commercial_supply_actions()
    {
        var resource = NewResource("RM-1", ProcessUnitType.HotRollingMill, ResourceType.RollingMill);
        var masters = new FakeMasterProvider(new PlanningMasterDataSnapshot(
            Array.Empty<Plant>(),
            Array.Empty<ProcessStage>(),
            new[] { resource },
            Array.Empty<ResourceCapability>(),
            Array.Empty<ResourceCalendar>(),
            Array.Empty<PlantFlowLink>(),
            Array.Empty<TransitionRule>(),
            Array.Empty<ManufacturingRoute>(),
            new[]
            {
                new ManufacturingRouteOperation
                {
                    ManufacturingRouteId = Guid.NewGuid(),
                    RouteCode = "SMS-RM",
                    SequenceNumber = 1,
                    ProcessOperationType = ProcessOperationType.HotRoll,
                    ReleaseWorkOrderType = WorkOrderType.HotRolling
                }
            },
            Array.Empty<RouteResourceCapability>()));
        var lifecycle = NewLifecycle(
            new CapturingPlanningEngine(),
            new FakePlanRepository(),
            masters,
            new FakeInventoryProvider(Array.Empty<InventoryPosition>()),
            new CapturingDemandService());
        var request = NewCalculationRequest() with
        {
            MaterialSupplyPolicy = new MaterialSupplyPlanningPolicy(AllowExternalBuy: true)
        };

        var exception = await Assert.ThrowsAsync<PlanningConfigurationException>(
            () => lifecycle.CalculateAsync(request));

        Assert.Contains(exception.Issues, x => x.Contains("manufacturing-only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Production_kernel_mode_rejects_missing_route_even_when_called_directly()
    {
        var engine = new APS.Planning.PlanningEngine(
            new NeverCalledCampaignPlanner(),
            new NeverCalledStructurePlanner(),
            new NeverCalledOptimizer());
        var request = new PlanningRunRequest(
            Array.Empty<ProductionOrder>(),
            Array.Empty<InventoryPosition>(),
            Array.Empty<Resource>(),
            Array.Empty<ResourceCapability>(),
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            Array.Empty<PlantFlowLink>(),
            new CampaignPlanningPolicy(100m, 90m, 110m, 500m, 600m),
            new ProductionStructurePlanningPolicy(),
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(1),
            ExecutionMode: PlanningExecutionMode.Production);

        Assert.Throws<PlanningConfigurationException>(() => engine.Run(request));
    }

    [Fact]
    public async Task Persisted_release_is_identity_only_and_idempotent()
    {
        await using var db = NewDb();
        var planVersionId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var rollingPlanId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var start = new DateTime(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);

        db.PlanVersions.Add(new PlanVersion { Id = planVersionId, VersionNumber = "PLAN-TEST", IsReleased = false });
        db.PlanVersionStates.Add(new PlanVersionState
        {
            PlanVersionId = planVersionId,
            Status = PlanVersionStatus.Feasible,
            ReferenceTimeUtc = start.AddHours(-1),
            HorizonStartUtc = start,
            HorizonEndUtc = start.AddDays(1),
            IsActive = true,
            MaterialRequirementsJson = "[]",
            MaterialSupplyRequirementsJson = "[]"
        });
        db.PlanProductionOrderSnapshots.Add(NewPoSnapshot(planVersionId, poId));
        db.PlanRollingPlanSnapshots.Add(new PlanRollingPlanSnapshot
        {
            PlanVersionId = planVersionId,
            RollingPlanId = rollingPlanId,
            ProductionOrderId = poId,
            RollingMillResourceId = resourceId,
            SequenceNumber = 1,
            GradeCode = "G1",
            InputCrossSectionCode = "150X150",
            OutputCrossSectionCode = "16MM",
            RouteCode = "SMS-RM",
            PlannedQuantityMt = 100m,
            FreshSteelQuantityMt = 100m
        });
        db.PlanRollingPlanAllocationSnapshots.Add(new PlanRollingPlanAllocationSnapshot
        {
            PlanVersionId = planVersionId,
            RollingPlanId = rollingPlanId,
            CampaignId = Guid.NewGuid(),
            ProductionOrderId = poId,
            PlannedQuantityMt = 100m,
            FreshSteelQuantityMt = 100m
        });
        db.PlanOperationSnapshots.Add(new PlanOperationSnapshot
        {
            PlanVersionId = planVersionId,
            PlanningKey = "ROLL:1",
            SourceEntityId = rollingPlanId,
            OperationType = PlanOperationType.HotRolling,
            ProcessOperationType = ProcessOperationType.HotRoll,
            ResourceId = resourceId,
            AssignmentCommitmentState = OperationAssignmentCommitmentState.Flexible,
            StartUtc = start,
            EndUtc = start.AddHours(2),
            QuantityMt = 100m,
            GradeCode = "G1",
            CrossSectionCode = "16MM"
        });
        await db.SaveChangesAsync();

        var service = new PersistedPlanReleaseService(db, new PlanReleaseRepository(db));
        await service.ApproveAsync(planVersionId);
        var first = await service.ReleaseAsync(planVersionId);
        var second = await service.ReleaseAsync(planVersionId);

        var firstWo = Assert.Single(first.WorkOrders);
        var secondWo = Assert.Single(second.WorkOrders);
        Assert.Equal(WorkOrderType.HotRolling, firstWo.WorkOrderType);
        Assert.Equal(firstWo.Id, secondWo.Id);
        Assert.Equal(poId, Assert.Single(firstWo.Allocations).ProductionOrderId);
        Assert.Single(first.Operations);
        Assert.Equal(ProcessOperationType.HotRoll, Assert.Single(first.Operations).ProcessOperationType);
        Assert.Single(await db.WorkOrders.ToArrayAsync());
    }

    [Fact]
    public async Task Persisted_release_includes_configured_downstream_route_operation_snapshots()
    {
        await using var db = NewDb();
        var planVersionId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var routePlanId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var start = new DateTime(2026, 8, 21, 6, 0, 0, DateTimeKind.Utc);

        db.PlanVersions.Add(new PlanVersion { Id = planVersionId, VersionNumber = "PLAN-ROUTE" });
        db.PlanVersionStates.Add(new PlanVersionState
        {
            PlanVersionId = planVersionId,
            Status = PlanVersionStatus.Feasible,
            ReferenceTimeUtc = start.AddHours(-1),
            HorizonStartUtc = start,
            HorizonEndUtc = start.AddDays(1),
            IsActive = true,
            MaterialRequirementsJson = "[]",
            MaterialSupplyRequirementsJson = "[]"
        });
        db.PlanProductionOrderSnapshots.Add(NewPoSnapshot(planVersionId, poId));
        db.PlanRouteOperationSnapshots.Add(new PlanRouteOperationSnapshot
        {
            PlanVersionId = planVersionId,
            RouteOperationPlanId = routePlanId,
            RouteCode = "SMS-RM-FIN",
            UpstreamPlanId = Guid.NewGuid(),
            ProcessOperationType = ProcessOperationType.Bundle,
            ReleaseWorkOrderType = WorkOrderType.Finishing,
            SequenceNumber = 3,
            ResourceId = resourceId,
            GradeCode = "G1",
            InputCrossSectionCode = "16MM",
            OutputCrossSectionCode = "BUNDLE-16",
            PlannedQuantityMt = 100m,
            MinimumQueueTime = TimeSpan.FromMinutes(5)
        });
        db.PlanRouteOperationAllocationSnapshots.Add(new PlanRouteOperationAllocationSnapshot
        {
            PlanVersionId = planVersionId,
            RouteOperationPlanId = routePlanId,
            CampaignId = campaignId,
            ProductionOrderId = poId,
            PlannedQuantityMt = 100m
        });
        db.PlanOperationSnapshots.Add(new PlanOperationSnapshot
        {
            PlanVersionId = planVersionId,
            PlanningKey = "ROUTE:BUNDLE:1",
            SourceEntityId = routePlanId,
            OperationType = PlanOperationType.Bundling,
            ProcessOperationType = ProcessOperationType.Bundle,
            ResourceId = resourceId,
            AssignmentCommitmentState = OperationAssignmentCommitmentState.Committed,
            StartUtc = start,
            EndUtc = start.AddMinutes(45),
            QuantityMt = 100m,
            GradeCode = "G1",
            CrossSectionCode = "BUNDLE-16"
        });
        await db.SaveChangesAsync();

        var service = new PersistedPlanReleaseService(db, new PlanReleaseRepository(db));
        await service.ApproveAsync(planVersionId);
        var release = await service.ReleaseAsync(planVersionId);

        var workOrder = Assert.Single(release.WorkOrders);
        Assert.Equal(WorkOrderType.Finishing, workOrder.WorkOrderType);
        Assert.Equal(campaignId, workOrder.CampaignId);
        Assert.Equal(poId, Assert.Single(workOrder.Allocations).ProductionOrderId);
        var scheduled = Assert.Single(release.Operations);
        Assert.Equal(ProcessOperationType.Bundle, scheduled.ProcessOperationType);
        Assert.True(scheduled.IsFrozen);
    }

    private static PlanningLifecycleService NewLifecycle(
        IPlanningEngine engine,
        IPlanVersionRepository plans,
        IPlanningMasterDataProvider masters,
        IInventorySnapshotProvider inventory,
        IProductionDemandOrchestrationService demand) => new(
            engine,
            plans,
            masters,
            inventory,
            new EmptyActualStateProvider(),
            demand,
            NullLogger<PlanningLifecycleService>.Instance);

    private static PlanningCalculationRequest NewCalculationRequest() => new(
        new PlanningDemandSelection(),
        new CampaignPlanningPolicy(100m, 90m, 110m, 500m, 600m),
        new ProductionStructurePlanningPolicy(),
        new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc));

    private static Resource NewResource(string code, ProcessUnitType unitType, ResourceType resourceType) => new()
    {
        PlantId = Guid.NewGuid(),
        ProcessStageId = Guid.NewGuid(),
        Code = code,
        Name = code,
        ProcessUnitType = unitType,
        ResourceType = resourceType
    };

    private static PlanProductionOrderSnapshot NewPoSnapshot(Guid planVersionId, Guid poId) => new()
    {
        PlanVersionId = planVersionId,
        ProductionOrderId = poId,
        ProductionOrderNumber = "PO-1",
        DemandSource = DemandSourceType.MakeToOrder,
        MaterialCode = "FG-16",
        GradeCode = "G1",
        FinalCrossSectionCode = "16MM",
        CasterSectionCode = "150X150",
        RouteCode = "SMS-RM",
        PlannedQuantityMt = 100m,
        RemainingQuantityMt = 100m,
        RequiredDate = new DateTime(2026, 8, 25),
        Status = ProductionOrderStatus.Planned,
        RollingRequirementMt = 100m,
        FreshSteelRequirementMt = 100m
    };

    private static ApsDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-canonical-{Guid.NewGuid():N}")
            .Options;
        return new ApsDbContext(options);
    }

    private sealed class CapturingPlanningEngine : IPlanningEngine
    {
        public PlanningRunRequest? LastRequest { get; private set; }
        public PlanningRunResult? LastResult { get; private set; }

        public PlanningRunResult Run(PlanningRunRequest request)
        {
            LastRequest = request;
            var campaign = new CampaignPlanningResult(
                Array.Empty<Campaign>(),
                Array.Empty<ProductionOrder>(),
                new Dictionary<Guid, decimal>(),
                new Dictionary<Guid, decimal>(),
                new Dictionary<Guid, decimal>(),
                Array.Empty<PlanningInventoryAllocation>());
            var structure = new ProductionStructurePlanningResult(
                Array.Empty<CastSequence>(),
                Array.Empty<RollingPlan>(),
                Array.Empty<PlannedBilletSupply>(),
                Array.Empty<FiniteScheduleTask>(),
                Array.Empty<PlanningIssue>());
            var schedule = new FiniteScheduleResult(
                "OPTIMAL",
                true,
                0,
                Array.Empty<FiniteScheduleAssignment>(),
                Array.Empty<PlanningIssue>());
            LastResult = new PlanningRunResult(Guid.NewGuid(), DateTime.UtcNow, campaign, structure, schedule, true);
            return LastResult;
        }
    }

    private sealed class CapturingDemandService : IProductionDemandOrchestrationService
    {
        public IReadOnlyCollection<InventoryPosition>? LastInventory { get; private set; }
        public DemandOrchestrationResult? LastResult { get; private set; }

        public Task<SalesOrderReconciliationResult> ReconcileSalesOrdersAsync(
            IReadOnlyCollection<SalesOrderDemandInput> salesOrders,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesOrderReconciliationResult(0, 0, salesOrders.Count, 0, Array.Empty<Guid>()));

        public Task<DemandOrchestrationResult> PrepareAsync(
            PlanningDemandSelection selection,
            IReadOnlyCollection<InventoryPosition> inventory,
            PlanningMasterDataSnapshot masters,
            DateTime referenceTimeUtc,
            DateTime horizonEndUtc,
            CancellationToken cancellationToken = default)
        {
            LastInventory = inventory;
            LastResult = new DemandOrchestrationResult(
                Array.Empty<ProductionOrder>(),
                Array.Empty<DemandOrchestrationItem>(),
                Array.Empty<ProductionOrder>(),
                Array.Empty<PlanningIssue>());
            return Task.FromResult(LastResult);
        }

        public Task<IReadOnlyCollection<DemandOrchestrationItem>> GetCurrentMtoDemandAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<DemandOrchestrationItem>>(Array.Empty<DemandOrchestrationItem>());
    }

    private sealed class FakeMasterProvider(PlanningMasterDataSnapshot snapshot) : IPlanningMasterDataProvider
    {
        public Task<PlanningMasterDataSnapshot> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    private sealed class FakeInventoryProvider(IReadOnlyCollection<InventoryPosition> rows) : IInventorySnapshotProvider
    {
        public Task<IReadOnlyCollection<InventoryPosition>> GetInventoryAsync(CancellationToken cancellationToken = default) => Task.FromResult(rows);
    }

    private sealed class FakePlanRepository : IPlanVersionRepository
    {
        private readonly Dictionary<Guid, PlanVersionSnapshot> _versions = new();
        public PersistPlanningRunRequest? LastPersistRequest { get; private set; }

        public Task<PlanVersionSnapshot> SaveAsync(PersistPlanningRunRequest request, CancellationToken cancellationToken = default)
        {
            LastPersistRequest = request;
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
            _versions[snapshot.PlanVersionId] = snapshot;
            return Task.FromResult(snapshot);
        }

        public Task<PlanVersionSnapshot?> GetAsync(Guid planVersionId, CancellationToken cancellationToken = default)
        {
            _versions.TryGetValue(planVersionId, out var value);
            return Task.FromResult(value);
        }

        public Task<IReadOnlyCollection<BaselinePlanOperation>> GetBaselineOperationsAsync(Guid planVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<BaselinePlanOperation>>(Array.Empty<BaselinePlanOperation>());

        public Task<IReadOnlyCollection<BaselineCampaignAllocation>> GetBaselineCampaignAllocationsAsync(
            Guid planVersionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<BaselineCampaignAllocation>>(Array.Empty<BaselineCampaignAllocation>());
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

    private sealed class NeverCalledCampaignPlanner : ICampaignPlanningService
    {
        public CampaignPlanningResult FormCampaigns(CampaignPlanningRequest request) =>
            throw new InvalidOperationException("Production route guard should run before campaign planning.");
    }

    private sealed class NeverCalledStructurePlanner : IProductionStructurePlanningService
    {
        public ProductionStructurePlanningResult Build(ProductionStructurePlanningRequest request) =>
            throw new InvalidOperationException("Production route guard should run before structure planning.");
    }

    private sealed class NeverCalledOptimizer : IFiniteScheduleOptimizer
    {
        public FiniteScheduleResult Solve(FiniteScheduleRequest request) =>
            throw new InvalidOperationException("Production route guard should run before scheduling.");
    }
}
