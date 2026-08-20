using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

/// <summary>
/// GitHub #34 acceptance 9: Plan Version readback must reproduce the effective route and operation
/// decisions. PlanOperationSnapshot recorded the operations that ran and nothing about the chain they
/// came from, so a heat whose VD was skipped because the grade did not require it was
/// indistinguishable from a heat on a route that never had a VD - and a read model could only redraw
/// the operations that survived, which is the fixed EAF/LRF/VD diagram problem in another form.
/// </summary>
public sealed class RouteDecisionReadbackTests
{
    private const string RouteCode = "FLEX-ROUTE";
    private static readonly DateTime Due = new(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Plan_version_reproduces_the_effective_route_including_the_step_it_skipped()
    {
        var result = RunPlan();
        Assert.True(result.IsFeasible, string.Join("; ", result.Schedule.Issues.Select(x => x.Message)));

        await using var db = CreateDb();
        var repository = new PlanVersionRepository(db);
        var saved = await repository.SaveAsync(new PersistPlanningRunRequest(
            PlanningRequest(),
            result,
            PlanTriggerType.Manual,
            Due.AddDays(-5)));

        var reloaded = await repository.GetAsync(saved.PlanVersionId);
        var decisions = reloaded!.RouteOperationDecisions;
        Assert.NotNull(decisions);

        // The optional VD the route offers is not required by this grade, so it is not planned - and
        // that fact is now on the record with its reason rather than being an absence.
        var vd = Assert.Single(decisions!, x => x.ProcessOperationType == ProcessOperationType.Vd);
        Assert.Equal(RouteOperationOutcome.SkippedOptional, vd.Outcome);
        Assert.Equal(RequirementDisposition.Optional, vd.RouteDisposition);
        Assert.Equal("OPTIONAL_AND_NOT_REQUIRED", vd.ReasonCode);
        Assert.Equal(RouteCode, vd.RouteCode);
        Assert.Equal(30, vd.RouteSequenceNumber);

        // The steps that did run are on the same record, in route order, so the whole configured chain
        // is reconstructable - not just its surviving half.
        var included = decisions!
            .Where(x => x.Outcome == RouteOperationOutcome.Included)
            .OrderBy(x => x.RouteSequenceNumber)
            .Select(x => x.ProcessOperationType)
            .ToArray();
        Assert.Equal(
            new[] { ProcessOperationType.Eaf, ProcessOperationType.Lrf, ProcessOperationType.Ccm },
            included);
    }

    [Fact]
    public async Task Persisted_operations_carry_their_route_position()
    {
        var result = RunPlan();
        await using var db = CreateDb();
        var repository = new PlanVersionRepository(db);
        var saved = await repository.SaveAsync(new PersistPlanningRunRequest(
            PlanningRequest(),
            result,
            PlanTriggerType.Manual,
            Due.AddDays(-5)));

        var reloaded = await repository.GetAsync(saved.PlanVersionId);

        // An operation that knows only its own type cannot say where in the chain it sat; one that
        // carries its route position can be placed on the configured route without re-deriving it.
        var meltshop = reloaded!.Operations
            .Where(x => x.RouteCode is not null)
            .OrderBy(x => x.RouteSequenceNumber)
            .ToArray();
        Assert.NotEmpty(meltshop);
        Assert.All(meltshop, x => Assert.Equal(RouteCode, x.RouteCode));
        Assert.Equal(new[] { 10, 20, 40 }, meltshop.Select(x => x.RouteSequenceNumber!.Value).ToArray());
    }

    private static PlanningRunResult RunPlan()
    {
        var engine = new PlanningEngine(
            new CampaignPlanningService(),
            new ProductionStructurePlanningService(),
            new FiniteScheduleOptimizer());
        return engine.Run(PlanningRequest());
    }

    private static readonly Resource Eaf = Furnace("PRIMARY-1");
    private static readonly Resource Lrf = SteelResource("LRF-1", ProcessUnitType.Lrf, ResourceType.Refining);
    private static readonly Resource Vd = SteelResource("VD-1", ProcessUnitType.Vd, ResourceType.Refining);
    private static readonly Resource Ccm = SteelResource("CCM-1", ProcessUnitType.Ccm, ResourceType.Caster);
    private static readonly Resource Rhf = SteelResource("RHF-1", ProcessUnitType.ReheatingFurnace, ResourceType.Generic);
    private static readonly Resource Mill = SteelResource("HRM-1", ProcessUnitType.HotRollingMill, ResourceType.RollingMill);

    private static PlanningRunRequest PlanningRequest() => new(
        new[] { Order() },
        Array.Empty<InventoryPosition>(),
        new[] { Eaf, Lrf, Vd, Ccm, Rhf, Mill },
        new[]
        {
            new ResourceCapability { ResourceId = Ccm.Id, RouteCode = RouteCode, GradeCode = "G-FLEX", OutputCrossSectionCode = "150X150", ThroughputMtPerHour = 60m },
            new ResourceCapability { ResourceId = Mill.Id, RouteCode = RouteCode, GradeCode = "G-FLEX", InputCrossSectionCode = "150X150", OutputCrossSectionCode = "HRC", ThroughputMtPerHour = 60m }
        },
        Array.Empty<ResourceCalendar>(),
        Array.Empty<TransitionRule>(),
        Links(),
        new CampaignPlanningPolicy(60m, 50m, 70m, 500m, 1000m),
        new ProductionStructurePlanningPolicy(),
        Due.AddDays(-5),
        Due.AddDays(5),
        10,
        RoutePlanning: new RoutePlanningInput(
            new[]
            {
                RouteOperation(10, ProcessOperationType.Eaf, RequirementDisposition.Required),
                RouteOperation(20, ProcessOperationType.Lrf, RequirementDisposition.Required),
                // Offered by the route, not required by this grade: the step whose absence used to be
                // unexplainable after the fact.
                RouteOperation(30, ProcessOperationType.Vd, RequirementDisposition.Optional),
                RouteOperation(40, ProcessOperationType.Ccm, RequirementDisposition.Required),
                RouteOperation(50, ProcessOperationType.Reheat, RequirementDisposition.Required),
                RouteOperation(60, ProcessOperationType.HotRoll, RequirementDisposition.Required)
            },
            new[]
            {
                new RouteResourceCapability { ResourceId = Mill.Id, RouteCode = RouteCode, ProcessOperationType = ProcessOperationType.HotRoll }
            }));

    private static PlantFlowLink[] Links() =>
    [
        Link(Eaf.Id, Lrf.Id, hot: true),
        Link(Lrf.Id, Vd.Id, hot: true),
        Link(Lrf.Id, Ccm.Id, hot: true),
        Link(Vd.Id, Ccm.Id, hot: true),
        Link(Ccm.Id, Rhf.Id, hot: false),
        Link(Rhf.Id, Mill.Id, hot: false),
        Link(Ccm.Id, Mill.Id, hot: false)
    ];

    private static PlantFlowLink Link(Guid from, Guid to, bool hot) => new()
    {
        FromResourceId = from,
        ToResourceId = to,
        CouplingType = hot ? FlowCouplingType.HotTransfer : FlowCouplingType.Buffered,
        MinimumTransferTime = hot ? TimeSpan.FromMinutes(5) : TimeSpan.Zero,
        SupportsHotTransfer = hot,
        IsEnabled = true
    };

    private static ManufacturingRouteOperation RouteOperation(
        int sequence,
        ProcessOperationType operation,
        RequirementDisposition requirement) => new()
    {
        ManufacturingRouteId = Guid.NewGuid(),
        RouteCode = RouteCode,
        SequenceNumber = sequence,
        ProcessOperationType = operation,
        ReleaseWorkOrderType = operation == ProcessOperationType.Ccm ? WorkOrderType.Casting : WorkOrderType.Steelmaking,
        Requirement = requirement
    };

    private static ProductionOrder Order() => new()
    {
        ProductionOrderNumber = "PO-FLEX-ROUTE",
        DemandSource = DemandSourceType.MakeToOrder,
        MaterialCode = "FG-FLEX",
        GradeCode = "G-FLEX",
        FinalCrossSectionCode = "HRC",
        CasterSectionCode = "150X150",
        RouteCode = RouteCode,
        PlannedQuantityMt = 60m,
        RemainingQuantityMt = 60m,
        RequiredDate = Due,
        Priority = 5,
        Status = ProductionOrderStatus.Planned
    };

    private static Resource Furnace(string code)
    {
        var resource = SteelResource(code, ProcessUnitType.Eaf, ResourceType.Furnace);
        resource.MinimumHeatWeightMt = 50m;
        resource.NominalHeatWeightMt = 60m;
        resource.MaximumHeatWeightMt = 70m;
        return resource;
    }

    private static Resource SteelResource(string code, ProcessUnitType unitType, ResourceType type) => new()
    {
        PlantId = Guid.NewGuid(),
        ProcessStageId = Guid.NewGuid(),
        Code = code,
        Name = code,
        ProcessUnitType = unitType,
        ResourceType = type
    };

    private static ApsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-route-decisions-{Guid.NewGuid():N}")
            .Options;
        return new ApsDbContext(options);
    }
}
