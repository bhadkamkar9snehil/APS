using APS.UI.Theme;

namespace APS.UI.Tests;

public sealed class ThemeColorTests
{
    [Theory]
    [InlineData("#7c3aed")]
    [InlineData("#7C3AED")]
    public void Strict_hex_parser_accepts_six_digit_rgb(string input)
    {
        Assert.True(ThemeColor.TryParseHex(input, out var color));
        Assert.Equal("#7C3AED", color.ToHex());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7c3aed")]
    [InlineData("#fff")]
    [InlineData("#zzzzzz")]
    [InlineData("#7C3AED00")]
    public void Strict_hex_parser_rejects_invalid_values(string? input)
    {
        Assert.False(ThemeColor.TryParseHex(input, out _));
    }

    [Fact]
    public void Foreground_selection_meets_aa_for_representative_custom_accent()
    {
        ThemeColor.TryParseHex("#7C3AED", out var color);

        Assert.True(color.ContrastRatio(color.BestForeground()) >= 4.5);
    }

    [Theory]
    [InlineData("#F5A623", "#171411")]
    [InlineData("#4B286D", "#FFFFFF")]
    public void Foreground_selection_chooses_the_stronger_neutral(string accent, string expected)
    {
        ThemeColor.TryParseHex(accent, out var color);

        Assert.Equal(expected, color.BestForeground().ToHex());
    }
}
