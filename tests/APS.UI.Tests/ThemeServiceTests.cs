using APS.UI.Theme;
using Microsoft.JSInterop;

namespace APS.UI.Tests;

public sealed class ThemeServiceTests
{
    [Fact]
    public async Task Invalid_custom_accent_preserves_previous_preference()
    {
        var js = new RecordingJsRuntime();
        await using var service = new ThemeService(js);
        await service.SetPresetAsync(ThemeAccentKind.Forest);

        var changed = await service.SetCustomAccentAsync("not-a-color");

        Assert.False(changed);
        Assert.Equal(ThemeAccentKind.Forest, service.Preference.Accent.Kind);
        Assert.Single(js.Invocations);
    }

    [Fact]
    public async Task Valid_custom_accent_is_normalized_and_applied()
    {
        var js = new RecordingJsRuntime();
        await using var service = new ThemeService(js);

        var changed = await service.SetCustomAccentAsync("#7c3aed");

        Assert.True(changed);
        Assert.Equal(ThemeAccentKind.Custom, service.Preference.Accent.Kind);
        Assert.Equal("#7C3AED", service.Preference.Accent.CustomHex);
        Assert.Equal("apsTheme.apply", js.Invocations.Single());
    }

    [Fact]
    public async Task Reset_restores_system_and_amber()
    {
        var js = new RecordingJsRuntime();
        await using var service = new ThemeService(js);
        await service.SetModeAsync(ThemeMode.Dark);

        await service.ResetAsync();

        Assert.Equal(ThemePreference.Default, service.Preference);
        Assert.Equal("apsTheme.reset", js.Invocations.Last());
    }

    [Fact]
    public async Task System_notification_updates_effective_theme_without_changing_preference()
    {
        var js = new RecordingJsRuntime();
        await using var service = new ThemeService(js);
        await service.OnSystemThemeChanged(true);

        Assert.Equal("dark", service.EffectiveTheme);
        Assert.Equal(ThemeMode.System, service.Preference.Mode);
    }

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<string> Invocations { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Invocations.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
