using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class PlanComparisonTests
{
    [Fact]
    public async Task Compares_plan_versions_by_stable_planning_key()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApsDbContext(options);

        var baseline = Guid.NewGuid();
        var current = Guid.NewGuid();
        var resource1 = Guid.NewGuid();
        var resource2 = Guid.NewGuid();
        var start = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);

        db.PlanVersions.AddRange(
            new PlanVersion { Id = baseline, VersionNumber = "PLAN-1", CreatedOnUtc = start },
            new PlanVersion { Id = current, VersionNumber = "PLAN-2", CreatedOnUtc = start.AddHours(1) });
        db.PlanOperationSnapshots.AddRange(
            Operation(baseline, "CAST:A", resource1, start, PlanOperationType.Casting),
            Operation(baseline, "ROLL:A", resource1, start.AddHours(2), PlanOperationType.HotRolling),
            Operation(current, "CAST:A", resource1, start, PlanOperationType.Casting),
            Operation(current, "ROLL:A", resource2, start.AddHours(2.5), PlanOperationType.HotRolling),
            Operation(current, "ROLL:B", resource2, start.AddHours(4), PlanOperationType.HotRolling));
        await db.SaveChangesAsync();

        var result = await new PlanComparisonService(db).CompareAsync(baseline, current);

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(0, result.RemovedCount);
        Assert.Equal(1, result.MovedCount);
        Assert.Equal(1, result.ResourceChangedCount);
        Assert.Equal(1, result.UnchangedCount);
        Assert.Equal(30, result.MaximumStartMovementMinutes);
        Assert.Contains(result.Operations, x => x.PlanningKey == "ROLL:A" &&
                                                x.ChangeType == PlanOperationChangeType.MovedAndResourceChanged);
    }

    [Fact]
    public async Task Comparison_workspace_projects_both_schedule_footprints_and_resource_loads()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApsDbContext(options);

        var baseline = Guid.NewGuid();
        var current = Guid.NewGuid();
        var resource1 = Resource("CCM-1", ProcessUnitType.Ccm, ResourceType.Caster);
        var resource2 = Resource("RM-1", ProcessUnitType.HotRollingMill, ResourceType.RollingMill);
        var start = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);

        db.Resources.AddRange(resource1, resource2);
        db.PlanVersions.AddRange(
            new PlanVersion { Id = baseline, VersionNumber = "PLAN-1", CreatedOnUtc = start },
            new PlanVersion { Id = current, VersionNumber = "PLAN-2", CreatedOnUtc = start.AddHours(1) });
        db.PlanVersionStates.AddRange(
            State(baseline, null, start, 120),
            State(current, baseline, start.AddHours(1), 90));
        db.PlanOperationSnapshots.AddRange(
            Operation(baseline, "CAST:A", resource1.Id, start, PlanOperationType.Casting),
            Operation(baseline, "ROLL:A", resource1.Id, start.AddHours(2), PlanOperationType.HotRolling),
            Operation(current, "CAST:A", resource1.Id, start, PlanOperationType.Casting),
            Operation(current, "ROLL:A", resource2.Id, start.AddHours(2.5), PlanOperationType.HotRolling),
            Operation(current, "ROLL:B", resource2.Id, start.AddHours(4), PlanOperationType.HotRolling));
        await db.SaveChangesAsync();

        var query = new PlannerWorkspaceQueryService(db, new PlanVersionRepository(db));
        var comparison = await query.GetPlanComparisonAsync(baseline, current);

        Assert.NotNull(comparison);
        Assert.Equal(2, comparison!.BaselineSummary!.ScheduledOperations);
        Assert.Equal(3, comparison.NewPlanSummary!.ScheduledOperations);
        Assert.Equal(2d, comparison.BaselineSummary.ScheduledHours);
        Assert.Equal(3d, comparison.NewPlanSummary.ScheduledHours);
        Assert.Equal(2, comparison.BaselineOperations!.Count);
        Assert.Equal(3, comparison.NewPlanOperations!.Count);

        var casterLoad = Assert.Single(comparison.ResourceLoads!, x => x.ResourceCode == "CCM-1");
        Assert.Equal(2, casterLoad.BaselineOperations);
        Assert.Equal(1, casterLoad.NewOperations);
        var millLoad = Assert.Single(comparison.ResourceLoads!, x => x.ResourceCode == "RM-1");
        Assert.Equal(0, millLoad.BaselineOperations);
        Assert.Equal(2, millLoad.NewOperations);
    }

    private static PlanVersionState State(Guid id, Guid? parent, DateTime reference, long objective) => new()
    {
        PlanVersionId = id,
        ParentPlanVersionId = parent,
        Status = PlanVersionStatus.Feasible,
        Trigger = PlanTriggerType.Manual,
        ReferenceTimeUtc = reference,
        HorizonStartUtc = reference.Date,
        HorizonEndUtc = reference.Date.AddDays(1),
        SolverStatus = "Optimal",
        ObjectiveValue = objective,
        IsActive = true,
        MaterialRequirementsJson = "[]",
        MaterialSupplyRequirementsJson = "[]"
    };

    private static Resource Resource(string code, ProcessUnitType unitType, ResourceType type) => new()
    {
        PlantId = Guid.NewGuid(),
        ProcessStageId = Guid.NewGuid(),
        Code = code,
        Name = code,
        ProcessUnitType = unitType,
        ResourceType = type
    };

    private static PlanOperationSnapshot Operation(
        Guid planVersionId,
        string key,
        Guid resourceId,
        DateTime start,
        PlanOperationType type) => new()
    {
        PlanVersionId = planVersionId,
        PlanningKey = key,
        SourceEntityId = Guid.NewGuid(),
        OperationType = type,
        ResourceId = resourceId,
        StartUtc = start,
        EndUtc = start.AddHours(1),
        QuantityMt = 50m,
        GradeCode = "G1",
        CrossSectionCode = "150X150"
    };
}
