using APS.UI.Theme;

namespace APS.UI.Tests;

public sealed class ThemePreferenceTests
{
    [Fact]
    public void Default_preference_follows_system_with_amber_accent()
    {
        Assert.Equal(ThemeMode.System, ThemePreference.Default.Mode);
        Assert.Equal(ThemeAccentKind.Amber, ThemePreference.Default.Accent.Kind);
        Assert.Null(ThemePreference.Default.Accent.CustomHex);
    }

    [Fact]
    public void Current_preference_schema_is_versioned()
    {
        Assert.Equal(1, ThemePreference.CurrentVersion);
        Assert.Equal(ThemePreference.CurrentVersion, ThemePreference.Default.Version);
    }
}
