using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using APS.Planning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class ReplanningTests
{
    [Fact]
    public void Planning_projects_each_heat_to_four_strand_material_units()
    {
        var fixture = NewFixture();
        var result = fixture.Engine.Run(fixture.Request);

        Assert.True(result.IsFeasible, string.Join("; ", result.Schedule.Issues.Select(x => x.Message)));
        var heatCount = result.CampaignPlan.Campaigns.Sum(x => x.Heats.Count);
        Assert.NotNull(result.ProductionStructure.PlannedStrandMaterialUnits);
        var units = result.ProductionStructure.PlannedStrandMaterialUnits!;

        Assert.Equal(heatCount * 4, units.Count);
        Assert.All(units, unit => Assert.Contains(result.Schedule.Assignments, a => a.TaskId == unit.AvailabilityTaskId));
        Assert.Equal(heatCount, result.Schedule.Assignments.Count(a =>
            result.ProductionStructure.SchedulingTasks.Single(t => t.TaskId == a.TaskId).TaskType == FiniteScheduleTaskType.Casting));
    }

    [Fact]
    public void Fresh_rolling_can_start_from_earlier_heat_before_later_heat_finishes()
    {
        var fixture = NewFixture();
        var result = fixture.Engine.Run(fixture.Request);
        Assert.True(result.IsFeasible, string.Join("; ", result.Schedule.Issues.Select(x => x.Message)));

        var castingTaskIds = result.ProductionStructure.SchedulingTasks
            .Where(x => x.TaskType == FiniteScheduleTaskType.Casting)
            .Select(x => x.TaskId)
            .ToHashSet();
        var casting = result.Schedule.Assignments
            .Where(x => castingTaskIds.Contains(x.TaskId))
            .OrderBy(x => x.StartUtc)
            .ToArray();

        var freshPlan = result.ProductionStructure.RollingPlans.Single(x => x.FreshSteelQuantityMt > 0m);
        var freshRolling = result.Schedule.Assignments
            .Where(x => x.SourceEntityId == freshPlan.Id)
            .OrderBy(x => x.StartUtc)
            .ToArray();

        Assert.True(casting.Length >= 2);
        Assert.True(freshRolling.Length >= 2);
        Assert.True(freshRolling[0].StartUtc < casting[^1].EndUtc);
    }

    [Fact]
    public void Frozen_replan_preserves_matching_operation_start_and_resource()
    {
        var fixture = NewFixture();
        var baseline = fixture.Engine.Run(fixture.Request);
        Assert.True(baseline.IsFeasible);

        var baselineIdentities = baseline.TaskIdentities!.ToDictionary(x => x.TaskId);
        var baselineOperations = baseline.Schedule.Assignments.Select(a =>
        {
            var identity = baselineIdentities[a.TaskId];
            return new BaselinePlanOperation(
                identity.PlanningKey,
                a.ResourceId,
                a.StartUtc,
                a.EndUtc,
                identity.TaskType);
        }).ToArray();

        var replanned = fixture.Engine.Run(fixture.Request with
        {
            ReplanContext = new PlanningReplanContext(
                baseline.PlanVersionId,
                fixture.Request.HorizonStartUtc,
                new PlanningTimeFencePolicy(FrozenMinutes: 600, SlushyMinutes: 0),
                baselineOperations)
        });

        Assert.True(replanned.IsFeasible, string.Join("; ", replanned.Schedule.Issues.Select(x => x.Message)));

        var replanIdentityByTask = replanned.TaskIdentities!.ToDictionary(x => x.TaskId);
        var baselineByKey = baselineOperations.ToDictionary(x => x.PlanningKey);
        foreach (var assignment in replanned.Schedule.Assignments)
        {
            var key = replanIdentityByTask[assignment.TaskId].PlanningKey;
            if (!baselineByKey.TryGetValue(key, out var old)) continue;
            Assert.Equal(old.ResourceId, assignment.ResourceId);
            Assert.Equal(old.StartUtc, assignment.StartUtc);
        }
    }

    [Fact]
    public async Task Persisted_plan_can_supply_baseline_operations_for_next_replan()
    {
        var fixture = NewFixture();
        var result = fixture.Engine.Run(fixture.Request);
        Assert.True(result.IsFeasible);

        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApsDbContext(options);
        var repository = new PlanVersionRepository(db);

        var saved = await repository.SaveAsync(new PersistPlanningRunRequest(
            fixture.Request,
            result,
            PlanTriggerType.Manual,
            fixture.Request.HorizonStartUtc,
            "test baseline"));
        var baseline = await repository.GetBaselineOperationsAsync(saved.PlanVersionId);

        Assert.Equal(result.Schedule.Assignments.Count, baseline.Count);
        Assert.NotEmpty(await db.PlanMaterialUnitSnapshots.ToListAsync());
        Assert.NotEmpty(await db.PlanInventoryAllocationSnapshots.ToListAsync());
        Assert.True(saved.IsActive);
    }

    private static PlanningFixture NewFixture()
    {
        var po = new ProductionOrder
        {
            ProductionOrderNumber = "PO-REPLAN-1",
            DemandSource = DemandSourceType.MakeToOrder,
            MaterialCode = "FG-16",
            GradeCode = "G1",
            GradeSequenceClassCode = "SEQ-A",
            FinalCrossSectionCode = "16MM",
            CasterSectionCode = "150X150",
            RouteCode = "SMS-RM",
            PlannedQuantityMt = 100m,
            RemainingQuantityMt = 100m,
            RequiredDate = new DateTime(2026, 8, 22),
            Priority = 2
        };
        var plantId = Guid.NewGuid();
        var caster = new Resource
        {
            PlantId = plantId,
            ProcessStageId = Guid.NewGuid(),
            Code = "CCM-1",
            Name = "CCM-1",
            ResourceType = ResourceType.Caster,
            StrandCount = 4
        };
        var mill = new Resource
        {
            PlantId = plantId,
            ProcessStageId = Guid.NewGuid(),
            Code = "RM-1",
            Name = "RM-1",
            ResourceType = ResourceType.RollingMill
        };
        var capabilities = new[]
        {
            new ResourceCapability
            {
                ResourceId = caster.Id,
                GradeCode = "G1",
                OutputCrossSectionCode = "150X150",
                RouteCode = "SMS-RM",
                ThroughputMtPerHour = 60m
            },
            new ResourceCapability
            {
                ResourceId = mill.Id,
                GradeCode = "G1",
                InputCrossSectionCode = "150X150",
                OutputCrossSectionCode = "16MM",
                RouteCode = "SMS-RM",
                ThroughputMtPerHour = 50m
            }
        };
        var links = new[]
        {
            new PlantFlowLink
            {
                FromResourceId = caster.Id,
                ToResourceId = mill.Id,
                CouplingType = FlowCouplingType.HotTransfer,
                MinimumTransferTime = TimeSpan.FromMinutes(10),
                MaximumTransferTime = TimeSpan.FromMinutes(300),
                SupportsHotTransfer = true
            }
        };
        var horizonStart = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);
        var inventory = new[]
        {
            new InventoryPosition
            {
                MaterialCode = "BILLET-G1",
                GradeCode = "G1",
                CrossSectionCode = "150X150",
                Stage = InventoryStage.CastIntermediate,
                AvailableQuantityMt = 10m
            }
        };
        var request = new PlanningRunRequest(
            new[] { po },
            inventory,
            new[] { caster, mill },
            capabilities,
            Array.Empty<ResourceCalendar>(),
            Array.Empty<TransitionRule>(),
            links,
            new CampaignPlanningPolicy(50m, 40m, 55m, 250m, 300m),
            new ProductionStructurePlanningPolicy(MaximumHeatsPerCastSequence: 8),
            horizonStart,
            horizonStart.AddHours(12),
            5);

        return new PlanningFixture(
            new PlanningEngine(
                new CampaignPlanningService(),
                new ProductionStructurePlanningService(),
                new FiniteScheduleOptimizer()),
            request);
    }

    private sealed record PlanningFixture(PlanningEngine Engine, PlanningRunRequest Request);
}
