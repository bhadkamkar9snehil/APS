namespace APS.UI.Tests;

public sealed class LayoutThemeContractTests
{
    [Fact]
    public void Main_layout_uses_icon_and_single_APS_brand()
    {
        var razor = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/MainLayout.razor"));

        Assert.Contains("app-icon.png", razor);
        Assert.Contains(">APS<", razor);
        Assert.DoesNotContain("Steel planning system", razor);
        Assert.DoesNotContain(">A</div>", razor);
    }

    [Fact]
    public void Appearance_popover_exposes_all_modes_accents_and_reset()
    {
        var razor = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/AppearancePopover.razor"));

        foreach (var label in new[] { "System", "Light", "Dark", "Amber", "Violet", "Forest", "Brick", "Plum", "Olive", "Custom", "Reset" })
            Assert.Contains(label, razor);
    }

    [Fact]
    public void Active_navigation_uses_complete_surface_selection()
    {
        var razor = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/NavItem.razor"));

        Assert.Contains("bg-accent-soft", razor);
        Assert.Contains("outline", razor);
        Assert.DoesNotContain("border-l", razor);
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
        var layout = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/MainLayout.razor"));

        Assert.Contains("@page \"/\"", workbench);
        Assert.Contains("@page \"/control-tower\"", controlTower);
        Assert.Contains("Href=\"/\" Match=\"NavLinkMatch.All\" Label=\"Planning Workbench\"", layout);
        Assert.Contains("Href=\"/control-tower\" Label=\"Control Tower\"", layout);
        Assert.True(layout.IndexOf("Label=\"Planning Workbench\"", StringComparison.Ordinal) <
                    layout.IndexOf("Label=\"Control Tower\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Tailwind_rebuild_tracks_Razor_sources_and_contains_workbench_geometry()
    {
        var project = File.ReadAllText(Repo.File("src/APS.UI/APS.UI.csproj"));
        var css = File.ReadAllText(Repo.File("src/APS.UI/wwwroot/tailwind.css"));

        Assert.Contains("TailwindSource", project);
        Assert.Contains("@(TailwindSource)", project);
        Assert.Contains(".sticky", css);
        Assert.Contains(".min-h-0", css);
        Assert.Contains(".grid-cols-\\[176px_1fr\\]", css);
        Assert.Contains(".grid-cols-\\[18rem_minmax\\(0\\,1fr\\)_20rem\\]", css);
    }
}
