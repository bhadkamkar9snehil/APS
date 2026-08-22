using System.Xml.Linq;
using Xunit;

namespace APS.Architecture.Tests;

public sealed class RepositoryTestArchitectureTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Theory]
    [MemberData(nameof(ProductionProjectRules))]
    public void Production_project_dependencies_match_the_intended_layering(
        string projectPath,
        string[] expectedReferences)
    {
        var project = XDocument.Load(RepoPath(projectPath));
        var projectDirectory = Path.GetDirectoryName(RepoPath(projectPath))!;

        var actual = project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(reference => NormalizeReference(projectDirectory, reference!))
            .OrderBy(reference => reference, StringComparer.Ordinal)
            .ToArray();

        var expected = expectedReferences
            .OrderBy(reference => reference, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Every_test_project_is_registered_in_the_solution()
    {
        var solution = XDocument.Load(RepoPath("APS.slnx"));
        var registered = solution.Descendants("Project")
            .Select(element => element.Attribute("Path")?.Value?.Replace('\\', '/'))
            .Where(path => path is not null && path.StartsWith("tests/", StringComparison.Ordinal))
            .Cast<string>()
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var testsRoot = Path.Combine(RepoRoot, "tests");
        var discovered = Directory.EnumerateFiles(testsRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !PathContainsDirectory(path, "bin") && !PathContainsDirectory(path, "obj"))
            .Select(path => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(discovered, registered);
    }

    [Fact]
    public void Release_pipeline_runs_the_solution_test_gate()
    {
        var script = File.ReadAllText(RepoPath("build/release.ps1"));

        Assert.Contains("APS.slnx", script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "tests/APS.Planning.Tests/APS.Planning.Tests.csproj",
            script,
            StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> ProductionProjectRules()
    {
        yield return Rule("src/APS.Domain/APS.Domain.csproj");
        yield return Rule(
            "src/APS.Application/APS.Application.csproj",
            "src/APS.Domain/APS.Domain.csproj");
        yield return Rule(
            "src/APS.Planning/APS.Planning.csproj",
            "src/APS.Domain/APS.Domain.csproj",
            "src/APS.Application/APS.Application.csproj");
        yield return Rule(
            "src/APS.Infrastructure/APS.Infrastructure.csproj",
            "src/APS.Domain/APS.Domain.csproj",
            "src/APS.Application/APS.Application.csproj",
            "src/APS.Planning/APS.Planning.csproj");
        yield return Rule(
            "src/APS.UI/APS.UI.csproj",
            "src/APS.Domain/APS.Domain.csproj",
            "src/APS.Application/APS.Application.csproj");
        yield return Rule(
            "src/APS.Service/APS.Service.csproj",
            "src/APS.Application/APS.Application.csproj",
            "src/APS.Planning/APS.Planning.csproj",
            "src/APS.Infrastructure/APS.Infrastructure.csproj",
            "src/APS.UI/APS.UI.csproj");
        yield return Rule(
            "src/APS.DesktopHost/APS.DesktopHost.csproj",
            "src/APS.Application/APS.Application.csproj",
            "src/APS.Infrastructure/APS.Infrastructure.csproj",
            "src/APS.UI/APS.UI.csproj");
    }

    private static object[] Rule(string projectPath, params string[] expectedReferences) =>
        [projectPath, expectedReferences];

    private static string NormalizeReference(string projectDirectory, string reference)
    {
        var fullPath = Path.GetFullPath(Path.Combine(projectDirectory, reference));
        return Path.GetRelativePath(RepoRoot, fullPath).Replace('\\', '/');
    }

    private static bool PathContainsDirectory(string path, string directoryName)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains(directoryName, StringComparer.OrdinalIgnoreCase);
    }

    private static string RepoPath(string relativePath) =>
        Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "APS.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate APS.slnx from the test output directory.");
    }
}
