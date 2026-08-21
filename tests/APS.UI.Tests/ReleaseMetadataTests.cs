using System.Xml.Linq;

namespace APS.UI.Tests;

public sealed class ReleaseMetadataTests
{
    [Fact]
    public void Desktop_and_legacy_versions_are_intentionally_distinct()
    {
        Assert.Equal("0.10.0", File.ReadAllText(Repo.File("VERSION")).Trim());

        var project = XDocument.Load(Repo.File("src/APS.DesktopHost/APS.DesktopHost.csproj"));
        Assert.Equal("0.2.5", project.Descendants("Version").Single().Value);
    }

    [Fact]
    public void Visual_companion_state_is_ignored()
    {
        var ignore = File.ReadAllText(Repo.File(".gitignore"));

        Assert.Contains(".superpowers/", ignore);
    }
}
