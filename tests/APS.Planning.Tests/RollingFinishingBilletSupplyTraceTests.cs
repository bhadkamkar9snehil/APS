using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class RollingFinishingBilletSupplyTraceTests
{
    [Fact]
    public async Task Rolling_allocation_exposes_billet_supply_trace_with_shortfall_and_reservations()
    {
        await using var db = CreateDb();
        var repository = new PlanVersionRepository(db);
        var now = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

        var po = new ProductionOrder
        {
            ProductionOrderNumber = "PO-TRACE-1",
            DemandSource = DemandSourceType.MakeToOrder,
            MaterialCode = "BILLET-G42",
            GradeCode = "G42",
            FinalCrossSectionCode = "HRC",
            CasterSectionCode = "150X150",
            RouteCode = "ROUTE-1",
            PlannedQuantityMt = 60m,
            RemainingQuantityMt = 60m,
            RequiredDate = now.AddDays(2)
        };

        var rollingPlan = new RollingPlan
        {
            SequenceNumber = 1,
            GradeCode = "G42",
            InputCrossSectionCode = "150X150",
            OutputCrossSectionCode = "HRC",
            RouteCode = "ROUTE-1",
            PlannedQuantityMt = 60m,
            ExistingIntermediateInventoryMt = 40m
        };
        rollingPlan.Allocations.Add(new RollingPlanAllocation
        {
            RollingPlanId = rollingPlan.Id,
            RollingPlan = rollingPlan,
            ProductionOrderId = po.Id,
            ProductionOrder = po,
            PlannedQuantityMt = 60m,
            ExistingIntermediateInventoryMt = 40m
        });

        var emptyCampaignPlan = new CampaignPlanningResult(
            Array.Empty<Campaign>(),
            Array.Empty<ProductionOrder>(),
            new Dictionary<Guid, decimal> { [po.Id] = 60m },
            new Dictionary<Guid, decimal> { [po.Id] = 0m },
            new Dictionary<Guid, decimal> { [po.Id] = 40m },
            Array.Empty<PlanningInventoryAllocation>());

        var structure = new ProductionStructurePlanningResult(
            Array.Empty<CastSequence>(),
            new[] { rollingPlan },
            Array.Empty<PlannedBilletSupply>(),
            Array.Empty<FiniteScheduleTask>(),
            Array.Empty<PlanningIssue>());

        var requirement = new MaterialRequirement
        {
            RequirementKey = "REQ-BILLET-1",
            SourceType = MaterialRequirementSourceType.RollingPlan,
            SourceEntityId = rollingPlan.Id,
            ProductionOrderId = po.Id,
            MaterialCode = "BILLET-G42",
            GradeCode = "G42",
            CrossSectionCode = "150X150",
            RequiredQuantityMt = 60m,
            RequiredAtUtc = now.AddDays(1),
            Status = MaterialRequirementStatus.Shortfall,
            ShortfallQuantityMt = 20m,
            Explanation = "No qualified billet known for the uncovered 20 MT."
        };
        var reservation = new MaterialSupplyReservation
        {
            ProductionOrderId = po.Id,
            GradeCode = "G42",
            CrossSectionCode = "150X150",
            InventoryStage = InventoryStage.CastIntermediate,
            SupplyReference = "YARD-LOT-1",
            LocationCode = "YARD",
            QuantityMt = 40m,
            AvailableFromUtc = now,
            Status = MaterialReservationStatus.Reserved
        };
        var materialPlan = new MaterialPlanningResult(
            new[] { reservation },
            Array.Empty<ScheduledMaterialEvent>(),
            Array.Empty<MaterialBalanceEvent>(),
            Array.Empty<PlanningIssue>(),
            new[] { requirement },
            Array.Empty<MaterialSupplyRequirement>());

        var planningResult = new PlanningRunResult(
            Guid.NewGuid(),
            now,
            emptyCampaignPlan,
            structure,
            new FiniteScheduleResult("Optimal", true, 0, Array.Empty<FiniteScheduleAssignment>(), Array.Empty<PlanningIssue>()),
            true,
            Array.Empty<PlanningTaskIdentity>(),
            MaterialPlan: materialPlan);

        var planningRequest = new PlanningRunRequest(
            new[] { po },
            Array.Empty<InventoryPosition>(),
            Array.Empty<Resource>(),
            Array.Empty<ResourceCapability>(),
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            Array.Empty<PlantFlowLink>(),
            new CampaignPlanningPolicy(60m, 50m, 70m, 500m, 1000m),
            new ProductionStructurePlanningPolicy(),
            now,
            now.AddDays(7));

        await repository.SaveAsync(new PersistPlanningRunRequest(
            planningRequest, planningResult, PlanTriggerType.Manual, now, "Test billet supply trace"));

        var queryService = new PlannerWorkspaceQueryService(db, repository);
        var workspace = await queryService.GetRollingFinishingAsync(planningResult.PlanVersionId);

        var plan = Assert.Single(workspace!.RollingPlans);
        var allocation = Assert.Single(plan.Allocations);
        var trace = allocation.SupplyTrace;
        Assert.NotNull(trace);
        Assert.Equal(MaterialRequirementStatus.Shortfall, trace!.Status);
        Assert.Equal(20m, trace.ShortfallQuantityMt);
        Assert.Equal("No qualified billet known for the uncovered 20 MT.", trace.Explanation);
        var source = Assert.Single(trace.Sources);
        Assert.Equal("YARD-LOT-1", source.SupplyReference);
        Assert.Equal(40m, source.QuantityMt);
    }

    private static ApsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-billet-trace-{Guid.NewGuid():N}")
            .Options;
        return new ApsDbContext(options);
    }
}
