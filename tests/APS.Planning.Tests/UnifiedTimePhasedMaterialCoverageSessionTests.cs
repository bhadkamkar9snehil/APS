using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

public sealed class UnifiedTimePhasedMaterialCoverageSessionTests
{
    private static readonly DateTime ReferenceUtc = new(2026, 8, 18, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Shared_inventory_pool_is_consumed_once_across_requirements()
    {
        var session = new UnifiedTimePhasedMaterialCoverageSession(
            ReferenceUtc,
            new[] { Inventory("BILLET", available: 100m) },
            Array.Empty<MaterialSpecification>());

        var first = session.Cover(Request(Guid.NewGuid(), Guid.NewGuid(), "BILLET", 80m, ReferenceUtc.AddHours(1)));
        var second = session.Cover(Request(Guid.NewGuid(), Guid.NewGuid(), "BILLET", 80m, ReferenceUtc.AddHours(2)));

        Assert.Equal(80m, first.CoveredQuantity);
        Assert.Equal(20m, second.CoveredQuantity);
        Assert.Equal(100m, first.Allocations.Sum(x => x.Quantity) + second.Allocations.Sum(x => x.Quantity));
    }

    [Fact]
    public void Future_matching_supply_is_reported_late_but_not_consumed_before_available_time()
    {
        var receiptUtc = ReferenceUtc.AddDays(2);
        var session = new UnifiedTimePhasedMaterialCoverageSession(
            ReferenceUtc,
            new[] { Inventory("BILLET", confirmedIncoming: 100m, availableFromUtc: receiptUtc) },
            Array.Empty<MaterialSpecification>());

        var early = session.Cover(Request(Guid.NewGuid(), Guid.NewGuid(), "BILLET", 100m, ReferenceUtc.AddDays(1)));
        var later = session.Cover(Request(Guid.NewGuid(), Guid.NewGuid(), "BILLET", 100m, ReferenceUtc.AddDays(3)));

        Assert.Equal(0m, early.CoveredQuantity);
        Assert.Equal(100m, early.LateSupplyQuantity);
        Assert.Equal(receiptUtc, early.EarliestLateSupplyUtc);
        Assert.Empty(early.Allocations);

        Assert.Equal(100m, later.CoveredQuantity);
        Assert.Equal(0m, later.LateSupplyQuantity);
        Assert.Equal(100m, later.Allocations.Sum(x => x.Quantity));
    }

    [Fact]
    public void Progressive_receipts_only_cover_requirements_after_each_receipt_time()
    {
        var day2 = ReferenceUtc.AddDays(2);
        var day4 = ReferenceUtc.AddDays(4);
        var session = new UnifiedTimePhasedMaterialCoverageSession(
            ReferenceUtc,
            new[]
            {
                Inventory("BILLET", confirmedIncoming: 40m, availableFromUtc: day2),
                Inventory("BILLET", confirmedIncoming: 60m, availableFromUtc: day4)
            },
            Array.Empty<MaterialSpecification>());

        var day3Need = session.Cover(Request(Guid.NewGuid(), Guid.NewGuid(), "BILLET", 70m, ReferenceUtc.AddDays(3)));
        var day5Need = session.Cover(Request(Guid.NewGuid(), Guid.NewGuid(), "BILLET", 60m, ReferenceUtc.AddDays(5)));

        Assert.Equal(40m, day3Need.CoveredQuantity);
        Assert.Equal(30m, day3Need.LateSupplyQuantity);
        Assert.Equal(day4, day3Need.EarliestLateSupplyUtc);

        Assert.Equal(60m, day5Need.CoveredQuantity);
        Assert.Equal(0m, day5Need.LateSupplyQuantity);
    }

    [Fact]
    public void Generic_stock_cannot_cover_customer_or_process_qualified_requirement()
    {
        var session = new UnifiedTimePhasedMaterialCoverageSession(
            ReferenceUtc,
            new[] { Inventory("BILLET", available: 100m) },
            Array.Empty<MaterialSpecification>());

        var result = session.Cover(Request(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "BILLET",
            100m,
            ReferenceUtc.AddHours(1),
            qualificationCode: "PO-QUAL:TEST"));

        Assert.Equal(0m, result.CoveredQuantity);
        Assert.Empty(result.Allocations);
    }

    [Fact]
    public void Held_or_rejected_stock_is_not_available_to_material_coverage()
    {
        var held = Inventory("BILLET", available: 50m);
        held.QualityStatus = MaterialQualityStatus.Held;
        var rejected = Inventory("BILLET", available: 50m);
        rejected.QualityStatus = MaterialQualityStatus.Rejected;
        var session = new UnifiedTimePhasedMaterialCoverageSession(
            ReferenceUtc,
            new[] { held, rejected },
            Array.Empty<MaterialSpecification>());

        var result = session.Cover(Request(Guid.NewGuid(), Guid.NewGuid(), "BILLET", 100m, ReferenceUtc.AddHours(1)));

        Assert.Equal(0m, result.CoveredQuantity);
        Assert.Empty(result.Allocations);
    }

    private static MaterialCoverageRequest Request(
        Guid requirementId,
        Guid poId,
        string material,
        decimal qty,
        DateTime requiredAtUtc,
        string? qualificationCode = null) =>
        new(
            requirementId,
            poId,
            material,
            null,
            "G1",
            "150X150",
            qty,
            "MT",
            requiredAtUtc,
            null,
            qualificationCode,
            $"FG[MT] -> {material}[MT]");

    private static InventoryPosition Inventory(
        string material,
        decimal available = 0m,
        decimal confirmedIncoming = 0m,
        DateTime? availableFromUtc = null) =>
        new()
        {
            MaterialCode = material,
            GradeCode = "G1",
            CrossSectionCode = "150X150",
            Stage = InventoryStage.CastIntermediate,
            LocationCode = "YARD",
            QualityStatus = MaterialQualityStatus.Available,
            AvailableQuantityMt = available,
            ConfirmedIncomingQuantityMt = confirmedIncoming,
            AvailableFromUtc = availableFromUtc ?? ReferenceUtc
        };
}
