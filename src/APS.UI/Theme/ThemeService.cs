using Microsoft.JSInterop;

namespace APS.UI.Theme;

public sealed class ThemeService(IJSRuntime js) : IAsyncDisposable
{
    private const string ApplyIdentifier = "apsTheme.apply";
    private DotNetObjectReference<ThemeService>? selfReference;
    private bool disposed;

    public ThemePreference Preference { get; private set; } = ThemePreference.Default;
    public string EffectiveTheme { get; private set; } = "light";
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        ThrowIfDisposed();
        selfReference ??= DotNetObjectReference.Create(this);
        var result = await js.InvokeAsync<ThemeInitializationResult?>("apsTheme.initialize", selfReference);
        if (result is not null && IsValid(result.Preference))
        {
            Preference = result.Preference;
            EffectiveTheme = result.EffectiveTheme == "dark" ? "dark" : "light";
        }
        Changed?.Invoke();
    }

    public async Task SetModeAsync(ThemeMode mode)
    {
        ThrowIfDisposed();
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        Preference = Preference with { Mode = mode };
        await js.InvokeVoidAsync(ApplyIdentifier, Preference);
        Changed?.Invoke();
    }

    [JSInvokable]
    public Task OnSystemThemeChanged(bool dark)
    {
        if (disposed || Preference.Mode != ThemeMode.System)
            return Task.CompletedTask;

        EffectiveTheme = dark ? "dark" : "light";
        Changed?.Invoke();
        return Task.CompletedTask;
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
        selfReference?.Dispose();
    }

    private static bool IsValid(ThemePreference preference) =>
        preference.Version == ThemePreference.CurrentVersion &&
        Enum.IsDefined(preference.Mode) &&
        Enum.IsDefined(preference.Accent.Kind) &&
        (preference.Accent.Kind != ThemeAccentKind.Custom ||
         ThemeColor.TryParseHex(preference.Accent.CustomHex, out _));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    public sealed record ThemeInitializationResult(ThemePreference Preference, string EffectiveTheme);
}
