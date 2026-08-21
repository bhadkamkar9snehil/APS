namespace APS.UI.Theme;

public enum ThemeAccentKind
{
    Amber,
    Violet,
    Forest,
    Brick,
    Plum,
    Olive,
    Custom
}

public sealed record ThemeAccent(ThemeAccentKind Kind, string? CustomHex = null)
{
    public static ThemeAccent Amber { get; } = new(ThemeAccentKind.Amber);
}
