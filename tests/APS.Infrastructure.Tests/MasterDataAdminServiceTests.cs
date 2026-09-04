using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Infrastructure.Tests;

public sealed class MasterDataAdminServiceTests
{
    [Fact]
    public async Task Resource_calendar_rejects_overlapping_intervals_for_the_same_resource()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"calendar-admin-{Guid.NewGuid():N}")
            .Options;
        await using var db = new ApsDbContext(options);
        var service = new MasterDataAdminService(db);
        var resourceId = Guid.NewGuid();
        var start = new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc);

        await service.CreateAsync(new ResourceCalendar
        {
            ResourceId = resourceId,
            Start = start,
            End = start.AddHours(8),
            IsAvailable = false,
            CapacityFactorPct = 0m,
            ReasonCode = "PLANNED_DOWNTIME"
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new ResourceCalendar
        {
            ResourceId = resourceId,
            Start = start.AddHours(4),
            End = start.AddHours(10),
            IsAvailable = true,
            CapacityFactorPct = 50m,
            ReasonCode = "CAPACITY_DERATE"
        }));

        Assert.Contains("already has a calendar interval", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await db.ResourceCalendars.ToArrayAsync());
    }
}
