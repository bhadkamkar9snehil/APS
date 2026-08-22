namespace APS.UI.Tests;

public sealed class LayoutThemeContractTests
{
    [Fact]
    public void Main_layout_uses_icon_and_single_APS_brand()
    {
        var razor = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/DesktopMenuBar.razor"));

        Assert.Contains("app-icon.png", razor);
        Assert.Contains(">APS<", razor);
        Assert.DoesNotContain("Steel planning system", razor);
        Assert.DoesNotContain(">A</div>", razor);
    }

    [Fact]
    public void Desktop_menu_exposes_supported_appearance_modes()
    {
        var menu = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/DesktopMenuBar.razor"));

        foreach (var label in new[] { "System appearance", "Light appearance", "Dark appearance" })
            Assert.Contains(label, menu);
        Assert.DoesNotContain("AppearancePopover", menu);
    }

    [Fact]
    public void Desktop_menu_links_use_framework_navigation()
    {
        var link = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/MenuLink.razor"));

        Assert.Contains("<NavLink", link);
        Assert.DoesNotContain("border-l", link);
    }

    [Fact]
    public void Desktop_window_chrome_uses_neutral_caption_colors()
    {
        var chrome = File.ReadAllText(Repo.File("src/APS.DesktopHost/NativeWindowTheme.cs"));
        var window = File.ReadAllText(Repo.File("src/APS.DesktopHost/MainWindow.xaml.cs"));

        Assert.Contains("DwmwaCaptionColor", chrome);
        Assert.Contains("DwmwaTextColor", chrome);
        Assert.Contains("GraphiteCaption", chrome);
        Assert.Contains("NativeWindowTheme.Apply", window);
    }

    [Fact]
    public void Planning_workbench_is_the_default_planner_landing_screen()
    {
        var workbench = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor"));
        var controlTower = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/Home.razor"));
        var menu = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/DesktopMenuBar.razor"));

        Assert.Contains("@page \"/\"", workbench);
        Assert.Contains("@page \"/control-tower\"", controlTower);
        Assert.Contains("Href=\"/\" Label=\"Planning Workbench\"", menu);
        Assert.Contains("Label=\"Control overview\"", menu);
    }

    [Fact]
    public void Tailwind_rebuild_tracks_Razor_sources_and_contains_workbench_geometry()
    {
        var project = File.ReadAllText(Repo.File("src/APS.UI/APS.UI.csproj"));
        var input = File.ReadAllText(Repo.File("src/APS.UI/wwwroot/tailwind-input.css"));

        Assert.Contains("TailwindSource", project);
        Assert.Contains("@(TailwindSource)", project);
        Assert.Contains("Inputs=\"$(TailwindInputCss);@(TailwindSource)\"", project);
        Assert.Contains("Outputs=\"$(TailwindOutputCss)\"", project);
        Assert.Contains(".aps-gantt-lane", input);
        Assert.Contains("--aps-gantt-row-height", input);
    }
}
