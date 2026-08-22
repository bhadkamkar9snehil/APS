using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class PlanningWorkbenchTimeFenceTests
{
    [Fact]
    public async Task Preview_uses_the_time_fence_policy_carried_by_the_proposal()
    {
        var (db, planId, operation, target) = await SeedAsync();
        await using (db)
        {
            var service = new PlanningWorkbenchCommandService(db, new ThrowingLifecycle());
            var impact = await service.ValidateMoveAsync(new PlanningMoveProposal(
                planId,
                operation.PlanningKey,
                target.Id,
                operation.StartUtc.AddHours(3),
                "PLANNER_SEQUENCE",
                TimeFencePolicy: new PlanningTimeFencePolicy(FrozenMinutes: 300)));

            Assert.False(impact.CanApply);
            Assert.Contains(impact.Findings, x =>
                x.Code == "FROZEN_OPERATION" &&
                x.Severity == PlanningConstraintSeverity.Blocker);
        }
    }

    [Fact]
    public async Task Apply_validates_with_the_same_time_fence_policy_sent_to_replanning()
    {
        var (db, planId, operation, target) = await SeedAsync();
        await using (db)
        {
            var lifecycle = new RecordingLifecycle();
            var service = new PlanningWorkbenchCommandService(db, lifecycle);
            var proposal = new PlanningMoveProposal(
                planId,
                operation.PlanningKey,
                target.Id,
                operation.StartUtc.AddHours(3),
                "PLANNER_SEQUENCE");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyMoveAsync(
                new PlanningMoveApplyRequest(
                    proposal,
                    PlanningRequest(operation.StartUtc),
                    new PlanningTimeFencePolicy(FrozenMinutes: 300))));

            Assert.Contains("frozen time fence", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, lifecycle.ReplanCalls);
        }
    }

    [Fact]
    public async Task Bulk_preview_uses_the_time_fence_policy_carried_by_the_proposal()
    {
        var (db, planId, first, target) = await SeedAsync();
        await using (db)
        {
            var second = new PlanOperationSnapshot
            {
                PlanVersionId = planId,
                PlanningKey = "HEAT:CMP-00001:H02:EAF",
                SourceEntityId = Guid.NewGuid(),
                OperationType = PlanOperationType.Eaf,
                ProcessOperationType = ProcessOperationType.Eaf,
                ResourceId = first.ResourceId,
                StartUtc = first.StartUtc.AddMinutes(30),
                EndUtc = first.EndUtc.AddMinutes(30),
                QuantityMt = 72m,
                GradeCode = "SAE1008",
                CrossSectionCode = "BLT-150SQ"
            };
            db.PlanOperationSnapshots.Add(second);
            db.PlanOperationResourceOptionSnapshots.Add(new PlanOperationResourceOptionSnapshot
            {
                PlanVersionId = planId,
                PlanningKey = second.PlanningKey,
                SourceEntityId = second.SourceEntityId,
                ProcessOperationType = second.ProcessOperationType,
                ResourceId = target.Id,
                DurationMinutes = 60,
                WasSelected = false,
                EligibilityBasisCode = "ROUTE_GRADE_CAPABILITY"
            });
            await db.SaveChangesAsync();

            var service = new PlanningWorkbenchCommandService(db, new ThrowingLifecycle());
            var impact = await service.ValidateBulkMoveAsync(new PlanningBulkMoveProposal(
                planId,
                [
                    new PlanningBulkMoveItem(first.PlanningKey, target.Id, first.StartUtc.AddHours(3)),
                    new PlanningBulkMoveItem(second.PlanningKey, target.Id, second.StartUtc.AddHours(4))
                ],
                "PLANNER_SEQUENCE",
                TimeFencePolicy: new PlanningTimeFencePolicy(FrozenMinutes: 300)));

            Assert.False(impact.CanApply);
            Assert.Equal(2, impact.Items.Count);
            Assert.All(impact.Items, item => Assert.Contains(item.Findings, x => x.Code == "FROZEN_OPERATION"));
        }
    }

    private static async Task<(ApsDbContext Db, Guid PlanId, PlanOperationSnapshot Operation, Resource Target)> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-workbench-fence-{Guid.NewGuid():N}")
            .Options;
        var db = new ApsDbContext(options);
        var planId = Guid.NewGuid();
        var source = Resource("EAF1-A");
        var target = Resource("EAF2-A");
        var start = new DateTime(2026, 8, 21, 8, 0, 0, DateTimeKind.Utc);
        var operation = new PlanOperationSnapshot
        {
            PlanVersionId = planId,
            PlanningKey = "HEAT:CMP-00001:H01:EAF",
            SourceEntityId = Guid.NewGuid(),
            OperationType = PlanOperationType.Eaf,
            ProcessOperationType = ProcessOperationType.Eaf,
            ResourceId = source.Id,
            StartUtc = start,
            EndUtc = start.AddHours(1),
            QuantityMt = 70m,
            GradeCode = "SAE1008",
            CrossSectionCode = "BLT-150SQ"
        };

        db.PlanVersions.Add(new PlanVersion { Id = planId, VersionNumber = "PLAN-FENCE", CreatedOnUtc = start });
        db.PlanVersionStates.Add(new PlanVersionState
        {
            PlanVersionId = planId,
            Status = PlanVersionStatus.Feasible,
            ReferenceTimeUtc = start.AddHours(-4),
            HorizonStartUtc = start.AddHours(-4),
            HorizonEndUtc = start.AddDays(7),
            IsActive = true
        });
        db.Resources.AddRange(source, target);
        db.PlanOperationSnapshots.Add(operation);
        db.PlanOperationResourceOptionSnapshots.Add(new PlanOperationResourceOptionSnapshot
        {
            PlanVersionId = planId,
            PlanningKey = operation.PlanningKey,
            SourceEntityId = operation.SourceEntityId,
            ProcessOperationType = operation.ProcessOperationType,
            ResourceId = target.Id,
            DurationMinutes = 60,
            WasSelected = false,
            EligibilityBasisCode = "ROUTE_GRADE_CAPABILITY"
        });
        await db.SaveChangesAsync();
        return (db, planId, operation, target);
    }

    private static Resource Resource(string code) => new()
    {
        Id = Guid.NewGuid(),
        PlantId = Guid.NewGuid(),
        ProcessStageId = Guid.NewGuid(),
        Code = code,
        Name = code,
        ResourceType = ResourceType.Furnace,
        ProcessUnitType = ProcessUnitType.Eaf,
        OperatingState = ResourceOperatingState.Available,
        SchedulingMode = ResourceSchedulingMode.Disjunctive
    };

    private static PlanningCalculationRequest PlanningRequest(DateTime start) => new(
        new PlanningDemandSelection(),
        new CampaignPlanningPolicy(70m, 60m, 80m, 700m, 800m),
        new ProductionStructurePlanningPolicy(),
        start,
        start.AddDays(2));

    private sealed class ThrowingLifecycle : IPlanningLifecycleService
    {
        public Task<PersistedPlanningRunResult> CalculateAsync(
            PlanningCalculationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PersistedPlanningRunResult> ReplanAsync(
            Guid baselinePlanVersionId,
            PlanningRecalculationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingLifecycle : IPlanningLifecycleService
    {
        public int ReplanCalls { get; private set; }

        public Task<PersistedPlanningRunResult> CalculateAsync(
            PlanningCalculationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PersistedPlanningRunResult> ReplanAsync(
            Guid baselinePlanVersionId,
            PlanningRecalculationRequest request,
            CancellationToken cancellationToken = default)
        {
            ReplanCalls++;
            return Task.FromResult<PersistedPlanningRunResult>(null!);
        }
    }
}
