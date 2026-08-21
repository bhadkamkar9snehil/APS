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

        foreach (var label in new[] { "File", "Plan", "View", "Analyze", "Execute", "Configure", "Help" })
            Assert.Contains($"Label=\"{label}\"", menu);
    }

    [Fact]
    public void Workbench_gives_the_gantt_full_space_and_uses_overlay_drawers()
    {
        var page = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor"));

        Assert.Contains("aps-gantt-lanes", page);
        Assert.Contains("--aps-visible-lanes", page);
        Assert.Contains("absolute inset-y-0 left-0", page);
        Assert.Contains("absolute inset-y-0 right-0", page);
        Assert.DoesNotContain("BodyGridClass", page);
        Assert.DoesNotContain("grid-cols-[16rem_minmax(0,1fr)_20rem]", page);
    }
}
