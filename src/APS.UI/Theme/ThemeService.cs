using Microsoft.JSInterop;

namespace APS.UI.Theme;

public sealed class ThemeService(IJSRuntime js) : IAsyncDisposable
{
    private const string ApplyIdentifier = "apsTheme.apply";
    private ThemePreference preference = ThemePreference.Default;
    private bool disposed;

    public async Task InitializeAsync()
    {
        ThrowIfDisposed();
        var loaded = await js.InvokeAsync<ThemePreference?>("apsTheme.initialize");
        if (loaded is not null && IsValid(loaded))
            preference = loaded;
    }

    public async Task SetModeAsync(ThemeMode mode)
    {
        ThrowIfDisposed();
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        preference = preference with { Mode = mode };
        await js.InvokeVoidAsync(ApplyIdentifier, preference);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;

        disposed = true;
        try
        {
            await js.InvokeVoidAsync("apsTheme.dispose");
        }
        catch (JSDisconnectedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static bool IsValid(ThemePreference value) =>
        value.Version == ThemePreference.CurrentVersion &&
        Enum.IsDefined(value.Mode) &&
        Enum.IsDefined(value.Accent.Kind) &&
        (value.Accent.Kind != ThemeAccentKind.Custom || ThemeColor.TryParseHex(value.Accent.CustomHex, out _));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
