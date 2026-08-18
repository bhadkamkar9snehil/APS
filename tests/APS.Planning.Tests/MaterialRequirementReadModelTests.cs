using APS.Application;
using APS.Domain;
using Xunit;

namespace APS.Planning.Tests;

public sealed class MaterialRequirementReadModelTests
{
    [Fact]
    public void Flat_and_tree_views_preserve_parent_path_and_production_order_lineage()
    {
        var planVersionId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var root = Requirement(planVersionId, poId, "FG", null, "FG[MT]");
        var billet = Requirement(planVersionId, poId, "BILLET", root.Id, "FG[MT] -> BILLET[MT]");
        var liquid = Requirement(planVersionId, poId, "LIQUID", billet.Id, "FG[MT] -> BILLET[MT] -> LIQUID[MT]");
        var scrap = Requirement(planVersionId, poId, "SCRAP", liquid.Id, "FG[MT] -> BILLET[MT] -> LIQUID[MT] -> SCRAP[MT]");

        var view = MaterialRequirementReadModelBuilder.Build(
            planVersionId,
            new[] { scrap, liquid, root, billet });

        Assert.Equal(planVersionId, view.PlanVersionId);
        Assert.Equal(4, view.RequirementCount);
        Assert.Equal(4, view.Flattened.Count);
        var treeRoot = Assert.Single(view.Roots);
        Assert.Equal("FG", treeRoot.Requirement.MaterialCode);
        var treeBillet = Assert.Single(treeRoot.Children);
        var treeLiquid = Assert.Single(treeBillet.Children);
        var treeScrap = Assert.Single(treeLiquid.Children);
        Assert.Equal("SCRAP", treeScrap.Requirement.MaterialCode);
        Assert.Equal(poId, treeScrap.Requirement.ProductionOrderId);
        Assert.Equal("FG[MT] -> BILLET[MT] -> LIQUID[MT] -> SCRAP[MT]", treeScrap.Requirement.RequirementPath);
    }

    [Fact]
    public void Orphaned_historical_node_is_promoted_to_root_instead_of_hidden()
    {
        var planVersionId = Guid.NewGuid();
        var orphan = Requirement(planVersionId, Guid.NewGuid(), "ORE", Guid.NewGuid(), "FG[MT] -> ORE[MT]");

        var view = MaterialRequirementReadModelBuilder.Build(planVersionId, new[] { orphan });

        var root = Assert.Single(view.Roots);
        Assert.Equal(orphan.Id, root.Requirement.Id);
        Assert.Empty(root.Children);
    }

    private static MaterialRequirement Requirement(
        Guid planVersionId,
        Guid productionOrderId,
        string materialCode,
        Guid? parentId,
        string path) =>
        new()
        {
            PlanVersionId = planVersionId,
            RequirementKey = $"REQ-{materialCode}",
            ParentRequirementId = parentId,
            RequirementPath = path,
            SourceType = parentId.HasValue ? MaterialRequirementSourceType.BomComponent : MaterialRequirementSourceType.ProductionOrder,
            SourceEntityId = Guid.NewGuid(),
            ProductionOrderId = productionOrderId,
            MaterialCode = materialCode,
            GradeCode = "G1",
            CrossSectionCode = "",
            MaterialUom = "MT",
            GrossQuantity = 1m,
            NetRequirementQuantity = 1m,
            RequiredQuantityMt = 1m,
            RequiredAtUtc = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc),
            Priority = 1,
            Status = MaterialRequirementStatus.NotManufacturableHere
        };
}
