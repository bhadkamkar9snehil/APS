using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

public sealed class RecursiveMaterialLateSupplyTests
{
    private static readonly DateTime ReferenceUtc = new(2026, 8, 18, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Late_matching_leaf_supply_is_persisted_as_late_not_as_on_time_coverage()
    {
        var poId = Guid.NewGuid();
        var receiptUtc = ReferenceUtc.AddDays(5);
        var session = new UnifiedTimePhasedMaterialCoverageSession(
            ReferenceUtc,
            new[]
            {
                new InventoryPosition
                {
                    MaterialCode = "ORE",
                    GradeCode = "",
                    CrossSectionCode = "",
                    Stage = InventoryStage.RawMaterial,
                    LocationCode = "RM-YARD",
                    QualityStatus = MaterialQualityStatus.Available,
                    AvailableQuantityMt = 0m,
                    ConfirmedIncomingQuantityMt = 100m,
                    AvailableFromUtc = receiptUtc
                }
            },
            Array.Empty<MaterialSpecification>());
        var engine = new RecursiveMaterialRequirementEngine();

        var result = engine.Explode(new RecursiveMaterialRequirementRequest(
            new[]
            {
                new MaterialDemandSeed(
                    poId,
                    "ORE",
                    null,
                    "",
                    "",
                    100m,
                    "MT",
                    ReferenceUtc.AddDays(2),
                    1)
            },
            Array.Empty<BillOfMaterial>(),
            Array.Empty<MaterialSpecification>(),
            session));

        var requirement = Assert.Single(result.Requirements);
        Assert.Equal(MaterialRequirementStatus.LateSupply, requirement.Status);
        Assert.Equal(0m, requirement.CoveredQuantity);
        Assert.Equal(100m, requirement.NetRequirementQuantity);
        Assert.Equal(100m, requirement.ShortfallQuantity);
        Assert.Equal(100m, requirement.LateSupplyQuantity);
        Assert.Equal(receiptUtc, requirement.ExpectedFullyAvailableAtUtc);
        Assert.Contains("late", requirement.Explanation!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Late_supply_does_not_suppress_internal_production_when_a_bom_exists()
    {
        var poId = Guid.NewGuid();
        var receiptUtc = ReferenceUtc.AddDays(5);
        var session = new UnifiedTimePhasedMaterialCoverageSession(
            ReferenceUtc,
            new[]
            {
                new InventoryPosition
                {
                    MaterialCode = "BILLET",
                    GradeCode = "G1",
                    CrossSectionCode = "150X150",
                    Stage = InventoryStage.CastIntermediate,
                    LocationCode = "BILLET-YARD",
                    QualityStatus = MaterialQualityStatus.Available,
                    AvailableQuantityMt = 0m,
                    ConfirmedIncomingQuantityMt = 100m,
                    AvailableFromUtc = receiptUtc
                }
            },
            Array.Empty<MaterialSpecification>());
        var bom = new BillOfMaterial
        {
            BomCode = "BOM-BILLET",
            VersionNumber = 1,
            Status = BomStatus.Active,
            EffectiveFromUtc = ReferenceUtc.AddYears(-1),
            OutputMaterialCode = "BILLET",
            OutputQuantity = 1m,
            OutputUom = "MT",
            GradeCode = "G1",
            IsActive = true,
            Components =
            {
                new BillOfMaterialComponent
                {
                    SequenceNumber = 1,
                    ComponentMaterialCode = "LIQUID",
                    ComponentGradeCode = "G1",
                    ComponentCrossSectionCode = "150X150",
                    FlowType = BomFlowType.Input,
                    QuantityPerOutput = 1m,
                    Uom = "MT"
                }
            }
        };
        var engine = new RecursiveMaterialRequirementEngine();

        var result = engine.Explode(new RecursiveMaterialRequirementRequest(
            new[]
            {
                new MaterialDemandSeed(
                    poId,
                    "BILLET",
                    null,
                    "G1",
                    "150X150",
                    100m,
                    "MT",
                    ReferenceUtc.AddDays(2),
                    1)
            },
            new[] { bom },
            Array.Empty<MaterialSpecification>(),
            session));

        var billet = Assert.Single(result.Requirements.Where(x => x.MaterialCode == "BILLET"));
        Assert.Equal(MaterialRequirementStatus.InternalProductionRequired, billet.Status);
        Assert.Equal(100m, billet.InternalProductionQuantity);
        Assert.Equal(100m, billet.LateSupplyQuantity);
        Assert.Equal(receiptUtc, billet.ExpectedFullyAvailableAtUtc);
        Assert.Contains(result.Requirements, x => x.MaterialCode == "LIQUID");
    }
}
