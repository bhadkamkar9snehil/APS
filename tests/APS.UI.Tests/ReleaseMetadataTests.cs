using System.Xml.Linq;

namespace APS.UI.Tests;

public sealed class ReleaseMetadataTests
{
    [Fact]
    public void Desktop_project_is_the_only_version_authority()
    {
        Assert.False(File.Exists(Repo.File("VERSION")));

        var project = XDocument.Load(Repo.File("src/APS.DesktopHost/APS.DesktopHost.csproj"));
        Assert.Equal("0.3.0", project.Descendants("Version").Single().Value);
    }

    [Fact]
    public void Visual_companion_state_is_ignored()
    {
        var ignore = File.ReadAllText(Repo.File(".gitignore"));

        Assert.Contains(".superpowers/", ignore);
    }
}
