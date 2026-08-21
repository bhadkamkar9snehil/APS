namespace APS.UI.Tests;

public sealed class RepositoryBoundaryTests
{
    [Fact]
    public void Active_tree_contains_only_the_dotnet_product_implementation()
    {
        var retiredPaths = new[]
        {
            "engine",
            "data",
            "scenarios",
            "simulation",
            "aps-ui",
            "ui_design",
            "archive",
            "xaps_application_api.py",
            "aps_functions.py",
            "run_all.py",
            "APS_BF_SMS_RM.xlsx",
            "requirements-excel-api.txt",
            "package.json",
            "VERSION"
        };

        foreach (var path in retiredPaths)
        {
            Assert.False(
                File.Exists(Repo.File(path)) || Directory.Exists(Repo.File(path)),
                $"Retired implementation path returned: {path}");
        }
    }
}
