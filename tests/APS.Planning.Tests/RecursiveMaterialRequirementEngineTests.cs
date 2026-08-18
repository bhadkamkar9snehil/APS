using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

public sealed class RecursiveMaterialRequirementEngineTests
{
    private static readonly DateTime Need = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Deep_configured_chain_explodes_to_raw_material_leaves_without_fixed_depth()
    {
        var engine = new RecursiveMaterialRequirementEngine();
        var poId = Guid.NewGuid();
        var boms = new[]
        {
            Bom("FG", Input("BILLET", 1m)),
            Bom("BILLET", Input("LIQUID", 1.05m)),
            Bom("LIQUID", Input("HOT-METAL", 0.72m), Input("SCRAP", 0.30m), Input("ALLOY", 0.02m)),
            Bom("HOT-METAL", Input("BURDEN", 1.2m)),
            Bom("BURDEN", Input("ORE", 0.75m), Input("COKE", 0.20m), Input("COAL", 0.05m))
        };

        var result = engine.Explode(Request(Seed(poId, "FG", 100m), boms));

        Assert.False(result.HasErrors);
        Assert.Contains(result.Requirements, x => x.MaterialCode == "FG" && x.Status == MaterialRequirementStatus.InternalProductionRequired);
        Assert.Contains(result.Requirements, x => x.MaterialCode == "BILLET" && x.ParentRequirementId.HasValue);
        Assert.Contains(result.Requirements, x => x.MaterialCode == "LIQUID");
        Assert.Contains(result.Requirements, x => x.MaterialCode == "HOT-METAL");
        Assert.Contains(result.Requirements, x => x.MaterialCode == "BURDEN");
        Assert.Contains(result.Requirements, x => x.MaterialCode == "ORE" && x.Status == MaterialRequirementStatus.NotManufacturableHere);
        Assert.Contains(result.Requirements, x => x.MaterialCode == "COKE" && x.Status == MaterialRequirementStatus.NotManufacturableHere);
        Assert.Contains(result.Requirements, x => x.MaterialCode == "COAL" && x.Status == MaterialRequirementStatus.NotManufacturableHere);
        Assert.All(result.Requirements, x => Assert.Contains("[MT]", x.RequirementPath));
    }

    [Fact]
    public void Full_intermediate_coverage_stops_explosion_below_covered_node()
    {
        var engine = new RecursiveMaterialRequirementEngine();
        var poId = Guid.NewGuid();
        var inventory = new[]
        {
            Inventory("BILLET", 100m)
        };
        var boms = new[]
        {
            Bom("FG", Input("BILLET", 1m)),
            Bom("BILLET", Input("LIQUID", 1m)),
            Bom("LIQUID", Input("SCRAP", 1m))
        };

        var result = engine.Explode(Request(Seed(poId, "FG", 100m), boms, new InventorySnapshotMaterialCoverageSession(inventory)));

        var billet = Assert.Single(result.Requirements.Where(x => x.MaterialCode == "BILLET"));
        Assert.Equal(MaterialRequirementStatus.Covered, billet.Status);
        Assert.Equal(100m, billet.CoveredQuantityMt);
        Assert.DoesNotContain(result.Requirements, x => x.MaterialCode == "LIQUID");
        Assert.DoesNotContain(result.Requirements, x => x.MaterialCode == "SCRAP");
    }

    [Fact]
    public void Partial_intermediate_coverage_explodes_only_uncovered_remainder()
    {
        var engine = new RecursiveMaterialRequirementEngine();
        var poId = Guid.NewGuid();
        var boms = new[]
        {
            Bom("FG", Input("BILLET", 1m)),
            Bom("BILLET", Input("LIQUID", 1m))
        };

        var result = engine.Explode(Request(
            Seed(poId, "FG", 100m),
            boms,
            new InventorySnapshotMaterialCoverageSession(new[] { Inventory("BILLET", 30m) })));

        var billet = Assert.Single(result.Requirements.Where(x => x.MaterialCode == "BILLET"));
        var liquid = Assert.Single(result.Requirements.Where(x => x.MaterialCode == "LIQUID"));
        Assert.Equal(100m, billet.GrossQuantity);
        Assert.Equal(30m, billet.CoveredQuantityMt);
        Assert.Equal(70m, billet.NetRequirementQuantity);
        Assert.Equal(70m, billet.InternalProductionQuantity);
        Assert.Equal(70m, liquid.GrossQuantity);
    }

    [Fact]
    public void Yield_scrap_and_required_at_offset_are_applied_to_component_requirement()
    {
        var engine = new RecursiveMaterialRequirementEngine();
        var poId = Guid.NewGuid();
        var component = Input("RAW", 1m, yieldPct: 90m, requiredAtOffsetMinutes: 120);
        var result = engine.Explode(Request(Seed(poId, "FG", 90m), new[] { Bom("FG", component) }));

        var raw = Assert.Single(result.Requirements.Where(x => x.MaterialCode == "RAW"));
        Assert.Equal(100m, decimal.Round(raw.GrossQuantity, 4));
        Assert.Equal(90m, raw.EffectiveYieldPct);
        Assert.Equal(10m, raw.EffectiveScrapPct);
        Assert.Equal(Need.AddMinutes(-120), raw.RequiredAtUtc);
        Assert.Equal("BOM_COMPONENT_OFFSET", raw.TimingBasisCode);
    }

    [Fact]
    public void Byproduct_is_auditable_projected_output_and_is_not_recursively_consumed()
    {
        var engine = new RecursiveMaterialRequirementEngine();
        var poId = Guid.NewGuid();
        var byproduct = new BillOfMaterialComponent
        {
            SequenceNumber = 2,
            ComponentMaterialCode = "SLAG",
            FlowType = BomFlowType.Byproduct,
            QuantityPerOutput = 0.12m,
            Uom = "MT"
        };
        var boms = new[]
        {
            Bom("LIQUID", Input("SCRAP", 1m), byproduct),
            Bom("SLAG", Input("SHOULD-NOT-EXPLODE", 1m))
        };

        var result = engine.Explode(Request(Seed(poId, "LIQUID", 100m), boms));

        var slag = Assert.Single(result.Requirements.Where(x => x.MaterialCode == "SLAG"));
        Assert.Equal(BomFlowType.Byproduct, slag.FlowType);
        Assert.Equal(MaterialRequirementStatus.ProjectedOutput, slag.Status);
        Assert.Equal(12m, slag.ProducedQuantity);
        Assert.DoesNotContain(result.Requirements, x => x.MaterialCode == "SHOULD-NOT-EXPLODE");
    }

    [Fact]
    public void Bom_cycle_returns_domain_diagnostic_instead_of_recursing_forever()
    {
        var engine = new RecursiveMaterialRequirementEngine();
        var poId = Guid.NewGuid();
        var result = engine.Explode(Request(
            Seed(poId, "A", 10m),
            new[]
            {
                Bom("A", Input("B", 1m)),
                Bom("B", Input("C", 1m)),
                Bom("C", Input("A", 1m))
            }));

        Assert.True(result.HasErrors);
        var issue = Assert.Single(result.Issues.Where(x => x.Code == "BOM_CYCLE_DETECTED"));
        Assert.Contains("A[MT] -> B[MT] -> C[MT] -> A[MT]", issue.Message);
        Assert.Contains(result.Requirements, x => x.MaterialCode == "A" && x.ParentRequirementId.HasValue && x.Status == MaterialRequirementStatus.CycleBlocked);
    }

    [Fact]
    public void Effective_bom_selection_uses_priority_then_specificity_then_version_deterministically()
    {
        var engine = new RecursiveMaterialRequirementEngine();
        var poId = Guid.NewGuid();
        var generic = Bom("FG", Input("GENERIC", 1m));
        generic.BomCode = "BOM-GENERIC";
        generic.VersionNumber = 9;
        generic.SelectionPriority = 5;

        var gradeSpecific = Bom("FG", Input("GRADE-SPECIFIC", 1m));
        gradeSpecific.BomCode = "BOM-GRADE";
        gradeSpecific.VersionNumber = 1;
        gradeSpecific.SelectionPriority = 5;
        gradeSpecific.GradeCode = "G1";

        var higherPriority = Bom("FG", Input("PRIORITY", 1m));
        higherPriority.BomCode = "BOM-PRIORITY";
        higherPriority.SelectionPriority = 10;

        var result = engine.Explode(new RecursiveMaterialRequirementRequest(
            new[] { Seed(poId, "FG", 10m) with { GradeCode = "G1" } },
            new[] { generic, gradeSpecific, higherPriority },
            Array.Empty<MaterialSpecification>(),
            new NoMaterialCoverageSession()));

        var root = Assert.Single(result.Roots);
        Assert.Equal("BOM-PRIORITY", root.SelectedBomCode);
        Assert.Contains(result.Requirements, x => x.MaterialCode == "PRIORITY");
        Assert.DoesNotContain(result.Requirements, x => x.MaterialCode == "GENERIC");
        Assert.DoesNotContain(result.Requirements, x => x.MaterialCode == "GRADE-SPECIFIC");
    }

    [Fact]
    public void Non_mt_uom_is_preserved_and_never_silently_converted()
    {
        var engine = new RecursiveMaterialRequirementEngine();
        var poId = Guid.NewGuid();
        var bom = Bom("CHEM-BATCH", new BillOfMaterialComponent
        {
            SequenceNumber = 1,
            ComponentMaterialCode = "ADDITIVE",
            FlowType = BomFlowType.Input,
            QuantityPerOutput = 2.5m,
            Uom = "KG"
        });
        bom.OutputUom = "KG";
        bom.OutputQuantity = 1m;

        var result = engine.Explode(Request(Seed(poId, "CHEM-BATCH", 10m, "KG"), new[] { bom }));

        var additive = Assert.Single(result.Requirements.Where(x => x.MaterialCode == "ADDITIVE"));
        Assert.Equal("KG", additive.MaterialUom);
        Assert.Equal(25m, additive.GrossQuantity);
        Assert.Equal(0m, additive.RequiredQuantityMt);
        Assert.Equal(MaterialRequirementStatus.NotManufacturableHere, additive.Status);
    }

    [Fact]
    public void Shared_inventory_pool_is_consumed_once_across_multiple_demand_roots()
    {
        var engine = new RecursiveMaterialRequirementEngine();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var session = new InventorySnapshotMaterialCoverageSession(new[] { Inventory("RAW", 150m) });
        var request = new RecursiveMaterialRequirementRequest(
            new[] { Seed(first, "RAW", 100m), Seed(second, "RAW", 100m) },
            Array.Empty<BillOfMaterial>(),
            Array.Empty<MaterialSpecification>(),
            session);

        var result = engine.Explode(request);

        var roots = result.Roots.OrderBy(x => x.ProductionOrderId).ToArray();
        Assert.Equal(2, roots.Length);
        Assert.Equal(150m, result.CoverageAllocations.Sum(x => x.Quantity));
        Assert.Equal(50m, roots.Sum(x => x.NetRequirementQuantity));
        Assert.Single(roots.Where(x => x.Status == MaterialRequirementStatus.Covered));
        Assert.Single(roots.Where(x => x.Status == MaterialRequirementStatus.NotManufacturableHere));
    }

    private static RecursiveMaterialRequirementRequest Request(
        MaterialDemandSeed seed,
        IReadOnlyCollection<BillOfMaterial> boms,
        IMaterialCoverageSession? coverage = null) =>
        new(new[] { seed }, boms, Array.Empty<MaterialSpecification>(), coverage ?? new NoMaterialCoverageSession());

    private static MaterialDemandSeed Seed(Guid poId, string material, decimal qty, string uom = "MT") =>
        new(poId, material, null, "", "", qty, uom, Need, 1);

    private static BillOfMaterial Bom(string output, params BillOfMaterialComponent[] components)
    {
        var bom = new BillOfMaterial
        {
            BomCode = $"BOM-{output}",
            VersionNumber = 1,
            Status = BomStatus.Active,
            EffectiveFromUtc = Need.AddYears(-1),
            OutputMaterialCode = output,
            OutputQuantity = 1m,
            OutputUom = "MT",
            IsActive = true
        };
        foreach (var component in components)
        {
            component.BillOfMaterialId = bom.Id;
            component.BillOfMaterial = bom;
            bom.Components.Add(component);
        }
        return bom;
    }

    private static BillOfMaterialComponent Input(
        string material,
        decimal qty,
        decimal? yieldPct = null,
        int requiredAtOffsetMinutes = 0) =>
        new()
        {
            SequenceNumber = 1,
            ComponentMaterialCode = material,
            FlowType = BomFlowType.Input,
            QuantityPerOutput = qty,
            Uom = "MT",
            YieldPct = yieldPct,
            RequiredAtOffsetMinutes = requiredAtOffsetMinutes
        };

    private static InventoryPosition Inventory(string material, decimal qty) =>
        new()
        {
            MaterialCode = material,
            GradeCode = "",
            CrossSectionCode = "",
            Stage = InventoryStage.RawMaterial,
            QualityStatus = MaterialQualityStatus.Available,
            AvailableQuantityMt = qty,
            AvailableFromUtc = Need.AddDays(-1)
        };
}
