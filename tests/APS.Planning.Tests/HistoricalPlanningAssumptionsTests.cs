using System.Text.Json;
using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class HistoricalPlanningAssumptionsTests
{
    [Fact]
    public async Task Persisted_workbench_capacity_is_immutable_when_live_resource_and_calendar_change()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-historical-capacity-{Guid.NewGuid():N}")
            .Options;
        await using var db = new ApsDbContext(options);

        var planId = Guid.NewGuid();
        var plantId = Guid.NewGuid();
        var stageId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var start = new DateTime(2026, 8, 23, 8, 0, 0, DateTimeKind.Utc);

        var assumptions = new PlanningAssumptions(
            ScenarioCode: null,
            CampaignObjectiveWeights.Default,
            Array.Empty<CampaignCompositionDecision>(),
            new[]
            {
                new ResourceSchedulingAssumption(
                    resourceId,
                    "RHF-1",
                    ResourceSchedulingMode.Cumulative,
                    ResourceCapacityBasis.Slots,
                    4m,
                    80m,
                    AppliesSequenceRules: false,
                    ResourceOperatingState.Available)
            },
            ResourceCalendars: new[]
            {
                new ResourceCalendarAssumption(
                    resourceId,
                    start.AddHours(1),
                    start.AddHours(2),
                    IsAvailable: false,
                    CapacityFactorPct: 0m,
                    ReasonCode: "SNAPSHOT_MAINTENANCE")
            });

        db.PlanVersions.Add(new PlanVersion
        {
            Id = planId,
            VersionNumber = "PLAN-HISTORICAL-CAPACITY",
            CreatedOnUtc = start
        });
        db.PlanVersionStates.Add(new PlanVersionState
        {
            PlanVersionId = planId,
            Status = PlanVersionStatus.Feasible,
            Trigger = PlanTriggerType.Manual,
            ReferenceTimeUtc = start,
            HorizonStartUtc = start,
            HorizonEndUtc = start.AddHours(3),
            SolverStatus = "Optimal",
            IsActive = true,
            PlanningAssumptionsJson = JsonSerializer.Serialize(
                assumptions,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        db.Plants.Add(new Plant
        {
            Id = plantId,
            Code = "ROLL",
            Name = "Rolling Mill"
        });
        db.ProcessStages.Add(new ProcessStage
        {
            Id = stageId,
            PlantId = plantId,
            Code = "RHF",
            Name = "Reheating Furnace",
            ProcessOperationType = ProcessOperationType.Reheat,
            SequenceNumber = 10
        });
        var resource = new Resource
        {
            Id = resourceId,
            PlantId = plantId,
            ProcessStageId = stageId,
            Code = "RHF-1",
            Name = "Reheating Furnace 1",
            ResourceType = ResourceType.Furnace,
            ProcessUnitType = ProcessUnitType.ReheatingFurnace,
            OperatingState = ResourceOperatingState.Available,
            SchedulingMode = ResourceSchedulingMode.Cumulative,
            CapacityBasis = ResourceCapacityBasis.Slots,
            NominalConcurrentCapacity = 4m,
            CapacityFactorPct = 80m
        };
        db.Resources.Add(resource);
        var liveCalendar = new ResourceCalendar
        {
            ResourceId = resourceId,
            Start = start.AddHours(1),
            End = start.AddHours(2),
            IsAvailable = false,
            CapacityFactorPct = 0m,
            ReasonCode = "SNAPSHOT_MAINTENANCE"
        };
        db.ResourceCalendars.Add(liveCalendar);
        db.PlanOperationSnapshots.Add(new PlanOperationSnapshot
        {
            PlanVersionId = planId,
            PlanningKey = "PO:WO-100:RHF",
            SourceEntityId = Guid.NewGuid(),
            OperationType = PlanOperationType.Reheating,
            ProcessOperationType = ProcessOperationType.Reheat,
            ResourceId = resourceId,
            StartUtc = start,
            EndUtc = start.AddHours(1),
            QuantityMt = 10m,
            GradeCode = "SAE1008",
            CrossSectionCode = "BLT-150SQ"
        });
        await db.SaveChangesAsync();

        var service = new PlannerWorkspaceQueryService(db, new PlanVersionRepository(db));
        var before = await service.GetPlanningWorkbenchAsync(planId);
        Assert.NotNull(before);
        var beforeCapacity = before.CapacityBuckets.OrderBy(x => x.ResourceId).ThenBy(x => x.StartUtc).ToArray();
        var beforeCalendars = before.ResourceCalendarIntervals.OrderBy(x => x.ResourceId).ThenBy(x => x.StartUtc).ToArray();
        Assert.NotEmpty(beforeCapacity);
        Assert.Single(beforeCalendars);
        Assert.All(beforeCalendars, x => Assert.Equal("PlanAssumptionSnapshot", x.Source));
        Assert.All(beforeCapacity, x => Assert.Equal(ResourceSchedulingMode.Cumulative, x.SchedulingMode));
        Assert.All(beforeCapacity, x => Assert.Equal(PlanningCapacityBasis.Slots, x.Basis));

        // Simulate the plant master changing after the Plan Version was cut. A historical read must
        // remain a view of the solved plan, not reinterpret it using today's resource configuration.
        resource.SchedulingMode = ResourceSchedulingMode.Disjunctive;
        resource.CapacityBasis = ResourceCapacityBasis.MassEquivalentMt;
        resource.NominalConcurrentCapacity = 1m;
        resource.CapacityFactorPct = 10m;
        resource.OperatingState = ResourceOperatingState.Breakdown;
        liveCalendar.IsAvailable = true;
        liveCalendar.CapacityFactorPct = 100m;
        liveCalendar.ReasonCode = "LIVE_MASTER_CHANGED";
        await db.SaveChangesAsync();

        var after = await service.GetPlanningWorkbenchAsync(planId);
        Assert.NotNull(after);
        var afterCapacity = after.CapacityBuckets.OrderBy(x => x.ResourceId).ThenBy(x => x.StartUtc).ToArray();
        var afterCalendars = after.ResourceCalendarIntervals.OrderBy(x => x.ResourceId).ThenBy(x => x.StartUtc).ToArray();

        Assert.Equal(beforeCapacity, afterCapacity);
        Assert.Equal(beforeCalendars, afterCalendars);
        Assert.DoesNotContain(after.Exceptions, x =>
            x.Kind == PlanningWorkbenchExceptionKind.ResourceUnavailable &&
            x.Entity?.Id == resourceId);
    }
}
