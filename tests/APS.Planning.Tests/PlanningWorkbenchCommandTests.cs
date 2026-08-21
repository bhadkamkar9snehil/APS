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
