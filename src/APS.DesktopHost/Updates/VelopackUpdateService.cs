using APS.Application;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Velopack;

namespace APS.DesktopHost.Updates;

public sealed class VelopackUpdateService(
    UpdateManager manager,
    Action requestShutdown,
    ILogger<VelopackUpdateService> log) : IUpdateService, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UpdateInfo? _candidate;

    public UpdateStatus Status { get; private set; } = !manager.IsInstalled
        ? new UpdateStatus(UpdatePhase.Unsupported, CurrentVersion(manager))
        : manager.UpdatePendingRestart is { } pending
            ? new UpdateStatus(UpdatePhase.ReadyToRestart, CurrentVersion(manager), pending.Version.ToString(), 100)
            : new UpdateStatus(UpdatePhase.Idle, CurrentVersion(manager));
    public event Action? Changed;

    public async Task CheckNowAsync(CancellationToken cancellationToken = default)
    {
        if (!manager.IsInstalled || Status.Phase is UpdatePhase.Downloading or UpdatePhase.ReadyToRestart) return;
        if (!await _gate.WaitAsync(0, cancellationToken)) return;

        var previous = Status;
        var attemptedAt = DateTimeOffset.Now;
        try
        {
            Publish(previous with { Phase = UpdatePhase.Checking, LastAttemptAt = attemptedAt, FailureCode = null });
            cancellationToken.ThrowIfCancellationRequested();
            _candidate = await manager.CheckForUpdatesAsync();
            cancellationToken.ThrowIfCancellationRequested();
            Publish(_candidate is null
                ? new UpdateStatus(UpdatePhase.Current, CurrentVersion(manager), LastAttemptAt: attemptedAt, LastSuccessfulCheckAt: DateTimeOffset.Now)
                : new UpdateStatus(UpdatePhase.Available, CurrentVersion(manager), _candidate.TargetFullRelease.Version.ToString(), LastAttemptAt: attemptedAt, LastSuccessfulCheckAt: DateTimeOffset.Now));
            log.LogInformation("APS Planner update check completed. State={State} AvailableVersion={AvailableVersion}", Status.Phase, Status.AvailableVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Publish(previous);
        }
        catch (Exception exception)
        {
            var code = Classify(exception);
            Publish(previous.Phase == UpdatePhase.Available
                ? previous with { LastAttemptAt = attemptedAt, FailureCode = code }
                : new UpdateStatus(UpdatePhase.Failed, CurrentVersion(manager), previous.AvailableVersion, LastAttemptAt: attemptedAt, LastSuccessfulCheckAt: previous.LastSuccessfulCheckAt, FailureCode: code));
            log.LogWarning(exception, "APS Planner update check failed. FailureCode={FailureCode}", code);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DownloadAsync(CancellationToken cancellationToken = default)
    {
        if (Status.Phase != UpdatePhase.Available || _candidate is null) return;
        if (!await _gate.WaitAsync(0, cancellationToken)) return;

        var available = Status;
        try
        {
            Publish(available with { Phase = UpdatePhase.Downloading, DownloadProgress = 0, FailureCode = null });
            await manager.DownloadUpdatesAsync(_candidate, progress => Publish(Status with { DownloadProgress = progress }), cancellationToken);
            Publish(Status with { Phase = UpdatePhase.ReadyToRestart, DownloadProgress = 100 });
            log.LogInformation("APS Planner update {Version} downloaded and ready to restart.", Status.AvailableVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Publish(available);
        }
        catch (Exception exception)
        {
            Publish(available with { FailureCode = "DownloadFailed" });
            log.LogWarning(exception, "APS Planner update download failed. Version={Version}", available.AvailableVersion);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void RestartAndApply()
    {
        if (Status.Phase != UpdatePhase.ReadyToRestart) return;
        log.LogInformation("Restarting APS Planner to apply update {Version}.", Status.AvailableVersion);
        manager.WaitExitThenApplyUpdates(manager.UpdatePendingRestart, silent: true, restart: true);
        requestShutdown();
    }

    private void Publish(UpdateStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }

    private static string CurrentVersion(UpdateManager manager) => manager.CurrentVersion?.ToString() ??
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";

    private static string Classify(Exception exception) => exception switch
    {
        HttpRequestException http when http.StatusCode == System.Net.HttpStatusCode.Forbidden => "RateLimited",
        HttpRequestException => "NetworkUnavailable",
        _ => "FeedUnavailable"
    };

    public void Dispose() => _gate.Dispose();
}