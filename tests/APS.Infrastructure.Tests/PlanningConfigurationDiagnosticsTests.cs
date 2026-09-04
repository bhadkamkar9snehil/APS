using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Infrastructure.Tests;

public sealed class PlanningConfigurationDiagnosticsTests
{
    [Fact]
    public async Task Preflight_reports_route_final_section_mismatch_before_solver_execution()
    {
        await using var db = CreateDb();
        db.ProductionOrders.Add(Order(finalSection: "RND-12", routeCode: "STD-BAR"));
        await db.SaveChangesAsync();

        var masters = Masters(routeOutputSection: "BLT-150SQ");
        var service = new PlanningConfigurationDiagnosticsService(db, new FixedMasterProvider(masters));

        var view = await service.GetAsync();

        var blocker = Assert.Single(view.Diagnostics, x => x.Code == "ROUTE_FINAL_SECTION_MISMATCH");
        Assert.Equal(PlanningConfigurationDiagnosticSeverity.Blocker, blocker.Severity);
        Assert.Contains("RND-12", blocker.Message, StringComparison.Ordinal);
        Assert.Contains("BLT-150SQ", blocker.Message, StringComparison.Ordinal);
        Assert.Equal("/plan/routes", blocker.FixHref);
        Assert.False(view.IsPlanningReady);
    }

    [Fact]
    public async Task Matching_route_endpoint_and_obvious_resource_evidence_are_not_flagged_as_blockers()
    {
        await using var db = CreateDb();
        db.ProductionOrders.Add(Order(finalSection: "RND-12", routeCode: "STD-BAR"));
        await db.SaveChangesAsync();

        var service = new PlanningConfigurationDiagnosticsService(
            db,
            new FixedMasterProvider(Masters(routeOutputSection: "RND-12")));

        var view = await service.GetAsync();

        Assert.DoesNotContain(view.Diagnostics, x => x.Code == "ROUTE_FINAL_SECTION_MISMATCH");
        Assert.DoesNotContain(view.Diagnostics, x => x.Code == "ROUTE_OPERATION_NO_RESOURCE");
        Assert.True(view.IsPlanningReady);
    }

    [Fact]
    public async Task Invalid_resource_calendar_is_a_preflight_blocker()
    {
        await using var db = CreateDb();
        var masters = Masters(routeOutputSection: "RND-12");
        var resource = Assert.Single(masters.Resources);
        var invalid = new ResourceCalendar
        {
            ResourceId = resource.Id,
            Start = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc),
            End = new DateTime(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc),
            IsAvailable = false,
            CapacityFactorPct = 120m,
            ReasonCode = "BAD_TEST_INTERVAL"
        };
        masters = masters with { ResourceCalendars = new[] { invalid } };
        var service = new PlanningConfigurationDiagnosticsService(db, new FixedMasterProvider(masters));

        var view = await service.GetAsync();

        Assert.Contains(view.Diagnostics, x => x.Code == "CALENDAR_INTERVAL_INVALID" && x.Severity == PlanningConfigurationDiagnosticSeverity.Blocker);
        Assert.Contains(view.Diagnostics, x => x.Code == "CALENDAR_CAPACITY_INVALID" && x.Severity == PlanningConfigurationDiagnosticSeverity.Blocker);
    }

    private static ProductionOrder Order(string finalSection, string routeCode) => new()
    {
        ProductionOrderNumber = "MTO-SO-1001-10",
        DemandSource = DemandSourceType.MakeToOrder,
        MaterialCode = "BAR-SAE1008-12",
        GradeCode = "SAE1008",
        FinalCrossSectionCode = finalSection,
        CasterSectionCode = "BLT-150SQ",
        RouteCode = routeCode,
        PlannedQuantityMt = 75m,
        RemainingQuantityMt = 75m,
        RequiredDate = new DateTime(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc),
        Priority = 10,
        Status = ProductionOrderStatus.Planned
    };

    private static PlanningMasterDataSnapshot Masters(string routeOutputSection)
    {
        var plantId = Guid.NewGuid();
        var stageId = Guid.NewGuid();
        var mill = new Resource
        {
            PlantId = plantId,
            ProcessStageId = stageId,
            Code = "RM-1",
            Name = "Rolling mill 1",
            ResourceType = ResourceType.RollingMill,
            ProcessUnitType = ProcessUnitType.HotRollingMill,
            OperatingState = ResourceOperatingState.Available,
            IsActive = true
        };
        var route = new ManufacturingRoute
        {
            RouteCode = "STD-BAR",
            Name = "Standard bar",
            IsActive = true
        };
        var operation = new ManufacturingRouteOperation
        {
            ManufacturingRouteId = route.Id,
            RouteCode = route.RouteCode,
            SequenceNumber = 10,
            ProcessOperationType = ProcessOperationType.HotRoll,
            ReleaseWorkOrderType = WorkOrderType.HotRolling,
            Requirement = RequirementDisposition.Required,
            InputCrossSectionCode = "BLT-150SQ",
            OutputCrossSectionCode = routeOutputSection
        };

        return new PlanningMasterDataSnapshot(
            Array.Empty<Plant>(),
            Array.Empty<ProcessStage>(),
            new[] { mill },
            Array.Empty<ResourceCapability>(),
            Array.Empty<ResourceCalendar>(),
            Array.Empty<PlantFlowLink>(),
            Array.Empty<TransitionRule>(),
            new[] { route },
            new[] { operation },
            Array.Empty<RouteResourceCapability>());
    }

    private static ApsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"planning-preflight-{Guid.NewGuid():N}")
            .Options;
        return new ApsDbContext(options);
    }

    private sealed class FixedMasterProvider(PlanningMasterDataSnapshot snapshot) : IPlanningMasterDataProvider
    {
        public Task<PlanningMasterDataSnapshot> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }
}
