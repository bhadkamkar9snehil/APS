using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class PlanVersionAuditTests
{
    [Fact]
    public async Task Plan_version_round_trips_resource_alternatives_dispatch_revision_and_material_audit()
    {
        await using var db = CreateDb();
        var repository = new PlanVersionRepository(db);
        var now = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var planId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var oldLrf = Guid.NewGuid();
        var selectedLrf = Guid.NewGuid();
        var planningKey = "HEAT:H100:LRF";

        var task = new FiniteScheduleTask(
            taskId,
            sourceId,
            FiniteScheduleTaskType.Lrf,
            "LRF H100",
            "G42",
            "150X150",
            50m,
            now,
            now.AddHours(1),
            10,
            new[] { new FiniteScheduleResourceOption(selectedLrf, 30, 5, "ROUTE_GRADE_CAPABILITY") },
            Array.Empty<FiniteScheduleDependency>(),
            ProcessOperationType.Lrf);
        var assignment = new FiniteScheduleAssignment(taskId, sourceId, selectedLrf, now, now.AddMinutes(30));

        var emptyCampaignPlan = new CampaignPlanningResult(
            Array.Empty<Campaign>(),
            Array.Empty<ProductionOrder>(),
            new Dictionary<Guid, decimal>(),
            new Dictionary<Guid, decimal>(),
            new Dictionary<Guid, decimal>(),
            Array.Empty<PlanningInventoryAllocation>(),
            SourcingAlternatives: new[]
            {
                new PlanningSupplyAlternative(
                    Guid.NewGuid(), MaterialSupplyActionType.Make, true, true, true,
                    50m, 50m, 0m, now, null, 0, "MAKE-RULE")
            });
        var structure = new ProductionStructurePlanningResult(
            Array.Empty<CastSequence>(),
            Array.Empty<RollingPlan>(),
            Array.Empty<PlannedBilletSupply>(),
            new[] { task },
            Array.Empty<PlanningIssue>());
        var schedule = new FiniteScheduleResult(
            "Optimal", true, 10, new[] { assignment }, Array.Empty<PlanningIssue>());

        var requirement = new MaterialRequirement
        {
            RequirementKey = "REQ-1",
            SourceType = MaterialRequirementSourceType.ProcessOperation,
            SourceEntityId = taskId,
            MaterialCode = "BILLET-G42",
            GradeCode = "G42",
            CrossSectionCode = "150X150",
            RequiredQuantityMt = 50m,
            RequiredAtUtc = now,
            Status = MaterialRequirementStatus.PlannedAvailable
        };
        var supply = new MaterialSupplyRequirement
        {
            MaterialRequirementId = requirement.Id,
            MaterialCode = "BILLET-G42",
            GradeCode = "G42",
            CrossSectionCode = "150X150",
            ActionType = MaterialSupplyActionType.Make,
            QuantityMt = 50m,
            RequiredReceiptUtc = now
        };
        var materialPlan = new MaterialPlanningResult(
            Array.Empty<MaterialSupplyReservation>(),
            Array.Empty<ScheduledMaterialEvent>(),
            new[]
            {
                new MaterialBalanceEvent
                {
                    EventType = MaterialBalanceEventType.PlannedProductionReceipt,
                    MaterialPoolKey = "POOL-G42",
                    GradeCode = "G42",
                    CrossSectionCode = "150X150",
                    QuantityDeltaMt = 50m,
                    EffectiveAtUtc = now
                }
            },
            Array.Empty<PlanningIssue>(),
            new[] { requirement },
            new[] { supply });

        var planningResult = new PlanningRunResult(
            planId,
            now,
            emptyCampaignPlan,
            structure,
            schedule,
            true,
            new[] { new PlanningTaskIdentity(taskId, sourceId, planningKey, FiniteScheduleTaskType.Lrf) },
            Guid.NewGuid(),
            ResourceAlternatives: new[]
            {
                new PlanningOperationResourceAlternative(taskId, sourceId, planningKey, ProcessOperationType.Lrf,
                    oldLrf, 30, 0, false, "ROUTE_GRADE_CAPABILITY"),
                new PlanningOperationResourceAlternative(taskId, sourceId, planningKey, ProcessOperationType.Lrf,
                    selectedLrf, 30, 5, true, "ROUTE_GRADE_CAPABILITY")
            },
            MaterialPlan: materialPlan);

        var planningRequest = new PlanningRunRequest(
            Array.Empty<ProductionOrder>(),
            Array.Empty<InventoryPosition>(),
            Array.Empty<Resource>(),
            Array.Empty<ResourceCapability>(),
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            Array.Empty<PlantFlowLink>(),
            new CampaignPlanningPolicy(60m, 50m, 70m, 500m, 1000m),
            new ProductionStructurePlanningPolicy(),
            now,
            now.AddDays(7),
            ReplanContext: new PlanningReplanContext(
                planningResult.BaselinePlanVersionId!.Value,
                now,
                new PlanningTimeFencePolicy(),
                new[]
                {
                    new BaselinePlanOperation(planningKey, oldLrf, now, now.AddMinutes(30), FiniteScheduleTaskType.Lrf)
                },
                new[]
                {
                    new OperationResourceOverride(planningKey, selectedLrf, ReasonCode: "PRIMARY_LRF_UNAVAILABLE")
                }));

        var saved = await repository.SaveAsync(new PersistPlanningRunRequest(
            planningRequest,
            planningResult,
            PlanTriggerType.OperationalRedispatch,
            now,
            "Test LRF redispatch"));

        Assert.Equal(2, saved.ResourceAlternatives!.Count);
        var selected = Assert.Single(saved.ResourceAlternatives!, x => x.ResourceId == selectedLrf);
        Assert.True(selected.WasSelected);
        Assert.Equal("ROUTE_GRADE_CAPABILITY", selected.EligibilityBasisCode);

        var revision = Assert.Single(saved.DispatchRevisions!);
        Assert.Equal(oldLrf, revision.PreviousResourceId);
        Assert.Equal(selectedLrf, revision.RevisedResourceId);
        Assert.Equal("PRIMARY_LRF_UNAVAILABLE", revision.ReasonCode);

        Assert.Single(saved.MaterialRequirements!);
        Assert.Single(saved.MaterialSupplyRequirements!);
        Assert.Single(saved.MaterialLedger!);
        Assert.Single(saved.SourcingAlternatives!);

        Assert.Equal(2, await db.PlanOperationResourceOptionSnapshots.CountAsync());
        Assert.Single(await db.OperationDispatchRevisions.ToListAsync());
    }

    private static ApsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-plan-audit-{Guid.NewGuid():N}")
            .Options;
        return new ApsDbContext(options);
    }
}
