namespace APS.UI.Tests;

public sealed class UserFacingIdentifierContractTests
{
    [Fact]
    public void Planner_surfaces_do_not_expose_internal_identifiers()
    {
        var files = Directory.GetFiles(
            Repo.File("src/APS.UI/Components"),
            "*.razor",
            SearchOption.AllDirectories);

        var forbidden = new[]
        {
            "@selected.EntityId",
            "Ephemeral calculation ID",
            "Material Lot ID",
            "ProductionOrderId.ToString(\"N\")"
        };

        foreach (var file in files)
        {
            var razor = File.ReadAllText(file);
            foreach (var fragment in forbidden)
            {
                Assert.DoesNotContain(fragment, razor, StringComparison.Ordinal);
            }
        }
    }
}
