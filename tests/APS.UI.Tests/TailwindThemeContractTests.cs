namespace APS.UI.Tests;

public sealed class TailwindThemeContractTests
{
    [Fact]
    public void Tailwind_source_declares_light_dark_and_semantic_roles()
    {
        var css = File.ReadAllText(Repo.File("src/APS.UI/wwwroot/tailwind-input.css"));

        Assert.Contains("[data-theme=\"dark\"]", css);
        Assert.Contains("--color-canvas", css);
        Assert.Contains("--color-surface", css);
        Assert.Contains("--color-accent-soft", css);
        Assert.Contains("--color-text-primary", css);
        Assert.Contains("--color-border", css);
        Assert.Contains("prefers-reduced-motion", css);
    }

    [Fact]
    public void Tailwind_source_defines_all_six_curated_accents_and_custom()
    {
        var css = File.ReadAllText(Repo.File("src/APS.UI/wwwroot/tailwind-input.css"));

        foreach (var accent in new[] { "amber", "violet", "forest", "brick", "plum", "olive", "custom" })
            Assert.Contains($"[data-accent=\"{accent}\"]", css);
    }

    [Fact]
    public void Theme_source_sets_native_color_scheme_and_focus_behavior()
    {
        var css = File.ReadAllText(Repo.File("src/APS.UI/wwwroot/tailwind-input.css"));

        Assert.Contains("color-scheme", css);
        Assert.Contains(":focus-visible", css);
        Assert.Contains("::selection", css);
    }
}
