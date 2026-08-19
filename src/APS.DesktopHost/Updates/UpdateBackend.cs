using Velopack;
using Velopack.Sources;

namespace APS.DesktopHost.Updates;

public sealed record UpdateCandidate(string Version, object NativeValue);

public interface IUpdateBackend
{
    bool IsInstalled { get; }
    string CurrentVersion { get; }
    string? PendingVersion { get; }
    Task<UpdateCandidate?> CheckAsync(CancellationToken cancellationToken);
    Task DownloadAsync(UpdateCandidate candidate, Action<int> progress, CancellationToken cancellationToken);
    void WaitExitThenApply();
}

public sealed class VelopackUpdateBackend : IUpdateBackend
{
    private readonly UpdateManager _manager;

    public VelopackUpdateBackend(string repositoryUrl)
    {
        _manager = new UpdateManager(new GithubSource(repositoryUrl, accessToken: null, prerelease: false));
    }

    public bool IsInstalled => _manager.IsInstalled;
    public string CurrentVersion => _manager.CurrentVersion?.ToString() ??
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";
    public string? PendingVersion => _manager.UpdatePendingRestart?.Version?.ToString();

    public async Task<UpdateCandidate?> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var update = await _manager.CheckForUpdatesAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return update is null ? null : new UpdateCandidate(update.TargetFullRelease.Version.ToString(), update);
    }

    public Task DownloadAsync(UpdateCandidate candidate, Action<int> progress, CancellationToken cancellationToken) =>
        _manager.DownloadUpdatesAsync((UpdateInfo)candidate.NativeValue, progress, cancellationToken);

    public void WaitExitThenApply() =>
        _manager.WaitExitThenApplyUpdates(_manager.UpdatePendingRestart, silent: true, restart: true);
}
