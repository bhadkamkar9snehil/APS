using Microsoft.JSInterop;

namespace APS.UI.Theme;

public sealed class ThemeService(IJSRuntime js) : IAsyncDisposable
{
    private bool disposed;

    public async Task InitializeAsync()
    {
        ThrowIfDisposed();
        await js.InvokeVoidAsync("apsTheme.initialize");
    }

    public async Task SetModeAsync(ThemeMode mode)
    {
        ThrowIfDisposed();
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        await js.InvokeVoidAsync("apsTheme.setMode", (int)mode);
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
