namespace APS.UI.Theme;

public sealed record ThemePreference(int Version, ThemeMode Mode, ThemeAccent Accent)
{
    public const int CurrentVersion = 1;

    public static ThemePreference Default { get; } =
        new(CurrentVersion, ThemeMode.System, ThemeAccent.Amber);
}
