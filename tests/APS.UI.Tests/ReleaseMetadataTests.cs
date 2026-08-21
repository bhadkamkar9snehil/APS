using System.Xml.Linq;

namespace APS.UI.Tests;

public sealed class ReleaseMetadataTests
{
    [Fact]
    public void Desktop_project_is_the_only_version_authority()
    {
        Assert.False(File.Exists(Repo.File("VERSION")));

        var project = XDocument.Load(Repo.File("src/APS.DesktopHost/APS.DesktopHost.csproj"));
        Assert.Equal("0.3.1", project.Descendants("Version").Single().Value);
    }

    [Fact]
    public void Visual_companion_state_is_ignored()
    {
        var ignore = File.ReadAllText(Repo.File(".gitignore"));

        Assert.Contains(".superpowers/", ignore);
    }

    [Fact]
    public void Release_pipeline_isolates_each_version_from_historical_packages()
    {
        var pipeline = File.ReadAllText(Repo.File("build/release.ps1"));
        var ignore = File.ReadAllText(Repo.File(".gitignore"));

        Assert.Contains("build/Releases/$Version", pipeline);
        Assert.Contains("Remove-Item -Recurse -Force $releasesDir", pipeline);
        Assert.True(
            pipeline.IndexOf("$releasesDir =", StringComparison.Ordinal) >
            pipeline.IndexOf("if (-not $Version)", StringComparison.Ordinal));
        Assert.Contains("build/*", ignore);
        Assert.Contains("!build/release.ps1", ignore);
    }
}
