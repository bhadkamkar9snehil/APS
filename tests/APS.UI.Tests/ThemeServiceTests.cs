using APS.UI.Theme;
using Microsoft.JSInterop;

namespace APS.UI.Tests;

public sealed class ThemeServiceTests
{
    [Fact]
    public async Task Mode_change_preserves_accent_and_applies_preference()
    {
        var js = new RecordingJsRuntime();
        await using var service = new ThemeService(js);
        var accent = service.Preference.Accent;

        await service.SetModeAsync(ThemeMode.Dark);

        Assert.Equal(ThemeMode.Dark, service.Preference.Mode);
        Assert.Equal(accent, service.Preference.Accent);
        Assert.Equal("apsTheme.apply", js.Invocations.Single());
    }

    [Fact]
    public async Task Invalid_mode_is_rejected_before_javascript_invocation()
    {
        var js = new RecordingJsRuntime();
        await using var service = new ThemeService(js);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetModeAsync((ThemeMode)99));

        Assert.Empty(js.Invocations);
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

    [Fact]
    public async Task Explicit_mode_ignores_system_theme_notifications()
    {
        var js = new RecordingJsRuntime();
        await using var service = new ThemeService(js);
        await service.SetModeAsync(ThemeMode.Light);

        await service.OnSystemThemeChanged(true);

        Assert.Equal("light", service.EffectiveTheme);
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
