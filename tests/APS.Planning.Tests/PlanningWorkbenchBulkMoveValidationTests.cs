using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class PlanningWorkbenchBulkMoveValidationTests
{
    [Fact]
    public async Task Bulk_move_ignores_other_selected_members_old_placements()
    {
        var (db, planId, first, second, target) = await SeedTwoOperationsAsync(withDependency: false);
        await using (db)
        {
            var service = new PlanningWorkbenchCommandService(db, new UnusedLifecycle());
            var proposal = new PlanningBulkMoveProposal(
                planId,
                [
                    // First deliberately moves into the second operation's old 12:00-13:00 slot.
                    new PlanningBulkMoveItem(first.PlanningKey, target.Id, first.StartUtc.AddHours(4)),
                    new PlanningBulkMoveItem(second.PlanningKey, target.Id, first.StartUtc.AddHours(6))
                ],
                "ATOMIC_SHIFT");

            var impact = await service.ValidateBulkMoveAsync(proposal);

            Assert.True(impact.CanApply);
            Assert.DoesNotContain(impact.Findings, x => x.Code is
                "FROZEN_RESOURCE_CONFLICT" or
                "RESOURCE_REPAIR_REQUIRED" or
                "FROZEN_PREDECESSOR_CONFLICT" or
                "PREDECESSOR_REPAIR_REQUIRED");
        }
    }

    [Fact]
    public async Task Bulk_move_blocks_invalid_final_predecessor_order()
    {
        var (db, planId, first, second, target) = await SeedTwoOperationsAsync(withDependency: true);
        await using (db)
        {
            var service = new PlanningWorkbenchCommandService(db, new UnusedLifecycle());
            var proposal = new PlanningBulkMoveProposal(
                planId,
                [
                    new PlanningBulkMoveItem(first.PlanningKey, target.Id, first.StartUtc.AddHours(7)),
                    new PlanningBulkMoveItem(second.PlanningKey, target.Id, first.StartUtc.AddHours(6))
                ],
                "ATOMIC_SHIFT");

            var impact = await service.ValidateBulkMoveAsync(proposal);

            Assert.False(impact.CanApply);
            Assert.Contains(impact.Findings, x =>
                x.Code == "BULK_PREDECESSOR_CONFLICT" &&
                x.Severity == PlanningConstraintSeverity.Blocker);
        }
    }

    private static async Task<(ApsDbContext Db, Guid PlanId, PlanOperationSnapshot First, PlanOperationSnapshot Second, Resource Target)>
        SeedTwoOperationsAsync(bool withDependency)
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-bulk-final-{Guid.NewGuid():N}")
            .Options;
        var db = new ApsDbContext(options);
        var planId = Guid.NewGuid();
        var source = Resource("EAF1-A");
        var target = Resource("EAF2-A");
        var start = new DateTime(2026, 8, 21, 8, 0, 0, DateTimeKind.Utc);
        var first = Operation(planId, "HEAT:CMP-00001:H01:EAF", source.Id, start);
        var second = Operation(planId, "HEAT:CMP-00001:H02:EAF", target.Id, start.AddHours(4));
        if (withDependency)
            second.PredecessorPlanningKeysJson = $"[\"{first.PlanningKey}\"]";

        db.PlanVersions.Add(new PlanVersion { Id = planId, VersionNumber = "PLAN-BULK", CreatedOnUtc = start });
        db.PlanVersionStates.Add(new PlanVersionState
        {
            PlanVersionId = planId,
            Status = PlanVersionStatus.Feasible,
            ReferenceTimeUtc = start.AddHours(-4),
            HorizonStartUtc = start.AddHours(-4),
            HorizonEndUtc = start.AddDays(2),
            IsActive = true
        });
        db.Resources.AddRange(source, target);
        db.PlanOperationSnapshots.AddRange(first, second);
        db.PlanOperationResourceOptionSnapshots.AddRange(
            Option(planId, first, source, true),
            Option(planId, first, target, false),
            Option(planId, second, target, true));
        await db.SaveChangesAsync();
        return (db, planId, first, second, target);
    }

    private static PlanOperationSnapshot Operation(Guid planId, string key, Guid resourceId, DateTime start) => new()
    {
        PlanVersionId = planId,
        PlanningKey = key,
        SourceEntityId = Guid.NewGuid(),
        OperationType = PlanOperationType.Eaf,
        ProcessOperationType = ProcessOperationType.Eaf,
        ResourceId = resourceId,
        StartUtc = start,
        EndUtc = start.AddHours(1),
        QuantityMt = 70m,
        GradeCode = "SAE1008",
        CrossSectionCode = "BLT-150SQ"
    };

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
}
