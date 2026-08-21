namespace APS.UI.Tests;

public sealed class ThemeCoverageTests
{
    private static readonly string[] FixedShellPalette =
    [
        "bg-white",
        "bg-slate-50",
        "bg-slate-100",
        "text-slate-900",
        "text-slate-800",
        "text-slate-700",
        "text-slate-600",
        "text-slate-500",
        "text-slate-400",
        "border-slate-100",
        "border-slate-200",
        "border-slate-300",
        "divide-slate-100",
        "bg-blue-100",
        "text-blue-700"
    ];

    [Fact]
    public void Active_razor_surfaces_do_not_use_fixed_shell_palette()
    {
        var files = Directory.GetFiles(Repo.File("src/APS.UI/Components"), "*.razor", SearchOption.AllDirectories);
        var violations = files
            .SelectMany(file => FixedShellPalette
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetRelativePath(Repo.File("."), file)}: {token}"))
            .OrderBy(value => value)
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Selected_surfaces_never_use_left_edge_only_accent()
    {
        var files = Directory.GetFiles(Repo.File("src/APS.UI/Components"), "*.razor", SearchOption.AllDirectories);
        var violations = files
            .Where(file =>
            {
                var source = File.ReadAllText(file);
                return source.Contains("border-l", StringComparison.Ordinal) &&
                       source.Contains("accent", StringComparison.Ordinal);
            })
            .Select(file => Path.GetRelativePath(Repo.File("."), file))
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }
}
