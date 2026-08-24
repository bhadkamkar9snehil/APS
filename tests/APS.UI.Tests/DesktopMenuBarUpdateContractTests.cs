namespace APS.UI.Tests;

public sealed class DesktopMenuBarUpdateContractTests
{
    [Fact]
    public void Desktop_menu_groups_global_navigation_and_appearance_separately()
    {
        var menu = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/DesktopMenuBar.razor"));
        var workspaces = MenuBlock(menu, "Workspaces");
        var configure = MenuBlock(menu, "Configure");
        var appearance = MenuBlock(menu, "Appearance");
        var help = MenuBlock(menu, "Help");

        Assert.Equal(
            new[] { "Workspaces", "Configure", "Appearance", "Help" },
            DesktopMenuLabels(menu));

        Assert.Equal(
            new[]
            {
                "/|Planning Workbench", "/plan/versions|Plans and scenarios", "/plan/campaigns|Campaign register",
                "/plan/demand|Demand and supply", "/plan/inventory|Inventory", "/plan/material-flow|Material flow",
                "/operate/work-orders|Work order register", "/plan/steelmaking|Steelmaking and casting",
                "/plan/rolling|Rolling and finishing", "/operate/traceability|Traceability"
            },
            MenuLinks(workspaces));
        Assert.Equal(new[] { "/plan/master-data|Master data" }, MenuLinks(configure));
        Assert.Equal(Enumerable.Repeat("MenuLink", 10), MenuComponentTypes(workspaces));
        Assert.Equal(new[] { "MenuLink" }, MenuComponentTypes(configure));
        Assert.Empty(MenuLinks(appearance));
        Assert.Empty(MenuLinks(help));
        Assert.Equal(
            new[] { "System appearance", "Light appearance", "Dark appearance" },
            MenuButtons(appearance));
        Assert.Equal(Enumerable.Repeat("MenuButton", 3), MenuComponentTypes(appearance));
        Assert.Equal(new[] { "MenuButton", "MenuButton" }, MenuComponentTypes(help));
    }

    [Fact]
    public void Desktop_menu_excludes_workbench_commands_and_right_aligned_brand_text()
    {
        var menu = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/DesktopMenuBar.razor"));

        foreach (var removedLabel in new[]
                 {
                     "New planning scenario", "Optimize schedule", "Validate plan", "Release plan", "Demand queue",
                     "Planner inspector", "Analysis dock", "Control overview", "Exceptions and capacity",
                     "Scenario comparison", "Execution monitor", "Create recovery scenario"
                 })
            Assert.DoesNotContain($"Label=\"{removedLabel}\"", menu);

        Assert.DoesNotContain("ml-auto px-2", menu);
        Assert.DoesNotContain(">APS<", menu);
        Assert.DoesNotContain("Cockpit.", menu);
        Assert.DoesNotContain("PlannerCockpitCommand", menu);
        Assert.DoesNotContain("PlannerAnalysisView", menu);
    }

    [Fact]
    public void Menu_bar_owns_update_status_subscription_and_cleanup()
    {
        var razor = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/DesktopMenuBar.razor"));

        Assert.Contains("@implements IDisposable", razor);
        Assert.Contains("Updates.Changed += OnUpdatesChanged", razor);
        Assert.Contains("InvokeAsync(StateHasChanged)", razor);
        Assert.Contains("Updates.Changed -= OnUpdatesChanged", razor);
    }

    [Fact]
    public void Help_menu_preserves_complete_update_lifecycle()
    {
        var razor = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/DesktopMenuBar.razor"));
        var help = MenuBlock(razor, "Help");

        Assert.Contains("APS Planner v@(AppVersion)", help);
        Assert.Contains("UpdatePhase.Available", help);
        Assert.Contains("Updates.DownloadAsync()", help);
        Assert.Contains("UpdatePhase.Downloading", help);
        Assert.Contains("DownloadProgress", help);
        Assert.Contains("UpdatePhase.ReadyToRestart", help);
        Assert.Contains("Updates.RestartAndApply", help);
        Assert.Contains("UpdatePhase.Failed", help);
        Assert.Contains("FailureCode", help);
    }

    private static string MenuBlock(string markup, string label)
    {
        const string end = "</DesktopMenu>";
        var start = markup.IndexOf($"<DesktopMenu Label=\"{label}\">", StringComparison.Ordinal);

        Assert.True(start >= 0, $"Menu block '{label}' was not found.");
        var endIndex = markup.IndexOf(end, start, StringComparison.Ordinal);
        Assert.True(endIndex >= 0, $"Menu block '{label}' was not closed.");

        return markup[start..(endIndex + end.Length)];
    }

    private static string[] MenuLinks(string block) =>
        System.Text.RegularExpressions.Regex.Matches(block, "<MenuLink Href=\\\"([^\\\"]+)\\\" Label=\\\"([^\\\"]+)\\\"")
            .Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}")
            .ToArray();

    private static string[] MenuButtons(string block) =>
        System.Text.RegularExpressions.Regex.Matches(block, "<MenuButton Label=\\\"([^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();

    private static string[] DesktopMenuLabels(string markup) =>
        System.Text.RegularExpressions.Regex.Matches(markup, "<DesktopMenu Label=\\\"([^\\\"]+)\\\">")
            .Select(match => match.Groups[1].Value)
            .ToArray();

    private static string[] MenuComponentTypes(string block) =>
        System.Text.RegularExpressions.Regex.Matches(block, "<(Menu(?:Link|Button))\\b")
            .Select(match => match.Groups[1].Value)
            .ToArray();
}
