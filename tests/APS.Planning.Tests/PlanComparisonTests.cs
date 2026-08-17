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
