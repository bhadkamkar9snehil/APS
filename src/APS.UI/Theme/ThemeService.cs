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
        preference = await js.InvokeAsync<ThemePreference>("apsTheme.initialize");
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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
