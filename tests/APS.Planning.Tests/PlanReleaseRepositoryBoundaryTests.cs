using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class PlanReleaseRepositoryBoundaryTests
{
    [Fact]
    public async Task Repository_rejects_caller_payload_for_already_released_plan()
    {
        await using var db = NewDb();
        var planId = Guid.NewGuid();
        db.PlanVersions.Add(new PlanVersion
        {
            Id = planId,
            VersionNumber = "PLAN-RELEASED",
            IsReleased = true
        });
        db.PlanVersionStates.Add(new PlanVersionState
        {
            PlanVersionId = planId,
            Status = PlanVersionStatus.Released,
            IsActive = true,
            ReferenceTimeUtc = DateTime.UtcNow,
            HorizonStartUtc = DateTime.UtcNow,
            HorizonEndUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var repository = new PlanReleaseRepository(db);
        var fabricated = new PlanRelease(
            planId,
            new[]
            {
                new WorkOrder
                {
                    WorkOrderNumber = "FABRICATED",
                    WorkOrderType = WorkOrderType.Finishing,
                    MaterialCode = "X",
                    GradeCode = "X",
                    CrossSectionCode = "X"
                }
            },
            Array.Empty<ScheduledOperation>());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.PersistAsync(fabricated));

        Assert.Contains("already released", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.WorkOrders.ToArrayAsync());
    }

    private static ApsDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-release-boundary-{Guid.NewGuid():N}")
            .Options;
        return new ApsDbContext(options);
    }
}
