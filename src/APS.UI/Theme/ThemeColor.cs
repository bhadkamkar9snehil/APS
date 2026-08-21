using System.Globalization;

namespace APS.UI.Theme;

public readonly record struct ThemeColor(byte Red, byte Green, byte Blue)
{
    private static readonly ThemeColor DarkForeground = new(0x17, 0x14, 0x11);
    private static readonly ThemeColor LightForeground = new(0xFF, 0xFF, 0xFF);

    public static bool TryParseHex(string? value, out ThemeColor color)
    {
        color = default;
        if (value is null || value.Length != 7 || value[0] != '#')
            return false;

        if (!byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
            return false;

        color = new ThemeColor(red, green, blue);
        return true;
    }

    public string ToHex() => $"#{Red:X2}{Green:X2}{Blue:X2}";

    public double RelativeLuminance =>
        0.2126 * Linearize(Red) + 0.7152 * Linearize(Green) + 0.0722 * Linearize(Blue);

    public double ContrastRatio(ThemeColor other)
    {
        var lighter = Math.Max(RelativeLuminance, other.RelativeLuminance);
        var darker = Math.Min(RelativeLuminance, other.RelativeLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    public ThemeColor BestForeground() =>
        ContrastRatio(DarkForeground) >= ContrastRatio(LightForeground)
            ? DarkForeground
            : LightForeground;

    private static double Linearize(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
