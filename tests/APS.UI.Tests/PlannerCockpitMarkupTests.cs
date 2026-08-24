namespace APS.UI.Tests;

public sealed class PlannerCockpitMarkupTests
{
    [Fact]
    public void Desktop_shell_has_menu_bar_without_sidebar_or_footer()
    {
        var layout = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/MainLayout.razor"));
        var menu = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/DesktopMenuBar.razor"));

        Assert.Contains("<DesktopMenuBar", layout);
        Assert.DoesNotContain("<aside", layout);
        Assert.DoesNotContain("<footer", layout);

        foreach (var label in new[] { "Workspaces", "Configure", "Appearance", "Help" })
            Assert.Contains($"Label=\"{label}\"", menu);
    }

    [Fact]
    public void Workbench_gives_the_gantt_full_space_and_uses_overlay_drawers()
    {
        var page = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor"));
        var gantt = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/WorkbenchGantt.razor"));

        Assert.Contains("<WorkbenchGantt", page);
        Assert.Contains("--aps-gantt-row-height", gantt);
        Assert.Contains("data-gantt-scroll", gantt);
        Assert.Contains("absolute inset-y-0 left-0", page);
        Assert.Contains("absolute inset-y-0 right-0", page);
        Assert.DoesNotContain("BodyGridClass", page);
        Assert.DoesNotContain("grid-cols-[16rem_minmax(0,1fr)_20rem]", page);
    }

    [Fact]
    public void Cockpit_keeps_drawer_and_analysis_ownership_without_a_command_bridge()
    {
        var state = File.ReadAllText(Repo.File("src/APS.UI/State/PlannerCockpitState.cs"));
        var page = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor"));

        Assert.Contains("OpenDrawer", state);
        Assert.Contains("AnalysisDockOpen", state);
        Assert.Contains("AnalysisView", state);
        Assert.DoesNotContain("PlannerCockpitCommand", state);
        Assert.DoesNotContain("CommandRequested", state);
        Assert.DoesNotContain("RequestCommand", state);
        Assert.DoesNotContain("CommandRequested", page);
        Assert.DoesNotContain("OnCockpitCommandRequested", page);
    }
}
