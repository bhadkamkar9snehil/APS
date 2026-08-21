using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class TraceabilityServiceTests
{
    [Fact]
    public async Task Material_lot_trace_is_found_by_business_lot_number()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-trace-{Guid.NewGuid():N}")
            .Options;
        await using var db = new ApsDbContext(options);
        db.MaterialLots.Add(new MaterialLot
        {
            LotNumber = "LOT-2026-0042",
            MaterialCode = "BILLET",
            GradeCode = "G1",
            CrossSectionCode = "150X150",
            QuantityMt = 42m
        });
        await db.SaveChangesAsync();

        var trace = await new TraceabilityService(db)
            .GetMaterialLotTraceByNumberAsync("  LOT-2026-0042  ");

        Assert.NotNull(trace);
        Assert.Equal("LOT-2026-0042", trace.LotNumber);
        Assert.Equal(42m, trace.QuantityMt);
    }
}
