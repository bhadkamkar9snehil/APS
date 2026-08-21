using System.Reflection;
using APS.Application;

namespace APS.Service;

/// <summary>
/// The planner shell shows a self-update banner, which only makes sense for the installed desktop
/// app - APS.DesktopHost backs it with Velopack. A browser-served UI is updated by deploying the
/// server, so there is nothing for a user to download or restart.
///
/// Reports <see cref="UpdatePhase.Unsupported"/> rather than pretending to be up to date: the phase
/// exists for this case, and MainLayout renders no banner for it. Claiming Current would be a lie
/// the UI would happily display.
/// </summary>
public sealed class UnsupportedUpdateService : IUpdateService
{
    public UpdateStatus Status { get; } = new(
        UpdatePhase.Unsupported,
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0");

    /// <summary>Never raised: the status is immutable for a server-hosted UI.</summary>
    public event Action? Changed
    {
        add { }
        remove { }
    }

    public Task CheckNowAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DownloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void RestartAndApply()
    {
        // Deliberately does nothing. Restarting the process would take the UI away from every other
        // connected user, which is not what the desktop gesture means.
    }
}
