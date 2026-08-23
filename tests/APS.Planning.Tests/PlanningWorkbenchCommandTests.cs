using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class PlanningWorkbenchCommandTests
{
    [Fact]
    public async Task Validates_an_eligible_non_conflicting_move_as_applicable()
    {
        var (db, planId, operation, target) = await SeedAsync();
        await using (db)
        {
            var service = new PlanningWorkbenchCommandService(db, new UnusedLifecycle());
            var result = await service.ValidateMoveAsync(new PlanningMoveProposal(
                planId,
                operation.PlanningKey,
                target.Id,
                operation.StartUtc.AddHours(3),
                "PLANNER_SEQUENCE"));

            Assert.True(result.CanApply);
            Assert.DoesNotContain(result.Findings, x => x.Severity == PlanningConstraintSeverity.Blocker);
            Assert.Equal("EAF2-A", result.TargetResourceCode);
            Assert.Equal(operation.StartUtc.AddHours(3), result.TargetStartUtc);
            Assert.Equal(operation.StartUtc.AddHours(4), result.TargetEndUtc);
        }
    }

    [Fact]
    public async Task Flags_an_overlap_for_solver_repair_without_blocking_the_proposal()
    {
        var (db, planId, operation, target) = await SeedAsync();
        await using (db)
        {
            db.PlanOperationSnapshots.Add(new PlanOperationSnapshot
            {
                PlanVersionId = planId,
                PlanningKey = "HEAT:CMP-00002:H01:EAF",
                SourceEntityId = Guid.NewGuid(),
                OperationType = PlanOperationType.Eaf,
                ProcessOperationType = ProcessOperationType.Eaf,
                ResourceId = target.Id,
                StartUtc = operation.StartUtc.AddHours(3.5),
                EndUtc = operation.StartUtc.AddHours(4.5),
                QuantityMt = 70m,
                GradeCode = "SAE1018",
                CrossSectionCode = "BLT-150SQ"
            });
            await db.SaveChangesAsync();

            var service = new PlanningWorkbenchCommandService(db, new UnusedLifecycle());
            var result = await service.ValidateMoveAsync(new PlanningMoveProposal(
                planId,
                operation.PlanningKey,
                target.Id,
                operation.StartUtc.AddHours(3),
                "PLANNER_SEQUENCE"));

            Assert.True(result.CanApply);
            Assert.Contains(result.Findings, x =>
                x.Code == "RESOURCE_REPAIR_REQUIRED" &&
                x.Severity == PlanningConstraintSeverity.Warning);
        }
    }

    [Theory]
    [InlineData(OperationExecutionStatus.Running)]
    [InlineData(OperationExecutionStatus.Completed)]
    public async Task Rejects_execution_protected_operations_even_when_called_outside_the_ui(
        OperationExecutionStatus executionStatus)
    {
        var (db, planId, operation, target) = await SeedAsync();
        await using (db)
        {
            operation.ExecutionStatus = executionStatus;
            await db.SaveChangesAsync();
            var service = new PlanningWorkbenchCommandService(db, new UnusedLifecycle());

            var result = await service.ValidateMoveAsync(new PlanningMoveProposal(
                planId,
                operation.PlanningKey,
                target.Id,
                operation.StartUtc.AddHours(3),
                "PLANNER_SEQUENCE"));

            Assert.False(result.CanApply);
            Assert.Contains(result.Findings, x =>
                x.Code == "EXECUTION_STATE_PROTECTED" &&
                x.Severity == PlanningConstraintSeverity.Blocker);
        }
    }

    [Fact]
    public async Task Bulk_move_validates_every_item_and_replans_once_with_all_overrides()
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
                StartUtc = first.StartUtc.AddHours(1),
                EndUtc = first.EndUtc.AddHours(1),
                QuantityMt = 72m,
                GradeCode = "SAE1008",
                CrossSectionCode = "BLT-150SQ"
            };
            var source = await db.Resources.SingleAsync(x => x.Id == first.ResourceId);
            db.PlanOperationSnapshots.Add(second);
            db.PlanOperationResourceOptionSnapshots.AddRange(
                Option(planId, second, source, true),
                Option(planId, second, target, false));
            await db.SaveChangesAsync();
            var lifecycle = new RecordingLifecycle();
            var service = new PlanningWorkbenchCommandService(db, lifecycle);
            var proposal = new PlanningBulkMoveProposal(
                planId,
                [
                    new PlanningBulkMoveItem(first.PlanningKey, target.Id, first.StartUtc.AddHours(4)),
                    new PlanningBulkMoveItem(second.PlanningKey, target.Id, second.StartUtc.AddHours(4))
                ],
                "PLANNER_SEQUENCE");

            var impact = await service.ValidateBulkMoveAsync(proposal);
            var applied = await service.ApplyBulkMoveAsync(new PlanningBulkMoveApplyRequest(
                proposal,
                PlanningRequest(first.StartUtc),
                new PlanningTimeFencePolicy()));

            Assert.True(impact.CanApply);
            Assert.Equal(2, impact.Items.Count);
            Assert.Equal(2, applied.Impact.Items.Count);
            Assert.Equal(1, lifecycle.ReplanCalls);
            Assert.Equal(planId, lifecycle.BaselinePlanVersionId);
            Assert.Equal(2, lifecycle.Request!.ScheduleOverrides!.Count);
            Assert.Equal(
                [first.PlanningKey, second.PlanningKey],
                lifecycle.Request.ScheduleOverrides.Select(x => x.PlanningKey));
        }
    }

    [Fact]
    public async Task Bulk_move_rejects_duplicate_operations_before_any_replan()
    {
        var (db, planId, operation, target) = await SeedAsync();
        await using (db)
        {
            var lifecycle = new RecordingLifecycle();
            var service = new PlanningWorkbenchCommandService(db, lifecycle);
            var proposal = new PlanningBulkMoveProposal(
                planId,
                [
                    new PlanningBulkMoveItem(operation.PlanningKey, target.Id, operation.StartUtc.AddHours(3)),
                    new PlanningBulkMoveItem(operation.PlanningKey, target.Id, operation.StartUtc.AddHours(4))
                ],
                "PLANNER_SEQUENCE");

            var impact = await service.ValidateBulkMoveAsync(proposal);

            Assert.False(impact.CanApply);
            Assert.Contains(impact.Findings, x => x.Code == "BULK_DUPLICATE_OPERATION");
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyBulkMoveAsync(
                new PlanningBulkMoveApplyRequest(
                    proposal,
                    PlanningRequest(operation.StartUtc),
                    new PlanningTimeFencePolicy())));
            Assert.Equal(0, lifecycle.ReplanCalls);
        }
    }

    [Fact]
    public async Task Bulk_move_rejects_internal_overlap_on_a_disjunctive_target()
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
                StartUtc = first.StartUtc.AddHours(1),
                EndUtc = first.EndUtc.AddHours(1),
                QuantityMt = 72m,
                GradeCode = "SAE1008",
                CrossSectionCode = "BLT-150SQ"
            };
            var source = await db.Resources.SingleAsync(x => x.Id == first.ResourceId);
            db.PlanOperationSnapshots.Add(second);
            db.PlanOperationResourceOptionSnapshots.AddRange(
                Option(planId, second, source, true),
                Option(planId, second, target, false));
            await db.SaveChangesAsync();
            var service = new PlanningWorkbenchCommandService(db, new UnusedLifecycle());

            var impact = await service.ValidateBulkMoveAsync(new PlanningBulkMoveProposal(
                planId,
                [
                    new PlanningBulkMoveItem(first.PlanningKey, target.Id, first.StartUtc.AddHours(4)),
                    new PlanningBulkMoveItem(second.PlanningKey, target.Id, first.StartUtc.AddHours(4.5))
                ],
                "PLANNER_SEQUENCE"));

            Assert.False(impact.CanApply);
            Assert.Contains(impact.Findings, x => x.Code == "BULK_TARGET_CONFLICT");
        }
    }

    private static async Task<(ApsDbContext Db, Guid PlanId, PlanOperationSnapshot Operation, Resource Target)> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
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

        db.PlanVersions.Add(new PlanVersion { Id = planId, VersionNumber = "PLAN-01", CreatedOnUtc = start });
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
        db.PlanOperationResourceOptionSnapshots.AddRange(
            Option(planId, operation, source, true),
            Option(planId, operation, target, false));
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

    private static PlanOperationResourceOptionSnapshot Option(
        Guid planId,
        PlanOperationSnapshot operation,
        Resource resource,
        bool selected) => new()
    {
        PlanVersionId = planId,
        PlanningKey = operation.PlanningKey,
        SourceEntityId = operation.SourceEntityId,
        ProcessOperationType = operation.ProcessOperationType,
        ResourceId = resource.Id,
        DurationMinutes = 60,
        WasSelected = selected,
        EligibilityBasisCode = "ROUTE_GRADE_CAPABILITY"
    };

    private sealed class UnusedLifecycle : IPlanningLifecycleService
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
        public Guid? BaselinePlanVersionId { get; private set; }
        public PlanningRecalculationRequest? Request { get; private set; }

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
            BaselinePlanVersionId = baselinePlanVersionId;
            Request = request;
            return Task.FromResult<PersistedPlanningRunResult>(null!);
        }
    }
}
