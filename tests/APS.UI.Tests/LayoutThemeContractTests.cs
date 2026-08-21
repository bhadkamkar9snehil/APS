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
}
