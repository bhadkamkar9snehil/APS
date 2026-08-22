using APS.UI.Theme;
using Microsoft.JSInterop;

namespace APS.UI.Tests;

public sealed class ThemeServiceTests
{
    [Fact]
    public async Task Valid_mode_is_sent_to_the_browser_theme_contract()
    {
        var js = new RecordingJsRuntime();
        await using var service = new ThemeService(js);

        await service.SetModeAsync(ThemeMode.Dark);

        Assert.Equal("apsTheme.setMode", js.Invocations.Single());
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
    public async Task Disposal_releases_the_browser_media_listener()
    {
        var js = new RecordingJsRuntime();
        var service = new ThemeService(js);

        await service.DisposeAsync();

        Assert.Equal("apsTheme.dispose", js.Invocations.Single());
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
