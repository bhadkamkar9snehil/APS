using APS.Application;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace APS.DesktopHost.Updates;

public sealed class VelopackUpdateService : IUpdateService, IDisposable
{
    private readonly IUpdateBackend _backend;
    private readonly Action _requestShutdown;
    private readonly ILogger<VelopackUpdateService> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UpdateCandidate? _candidate;

    public VelopackUpdateService(IUpdateBackend backend, Action requestShutdown, ILogger<VelopackUpdateService> log)
    {
        _backend = backend;
        _requestShutdown = requestShutdown;
        _log = log;
        Status = !_backend.IsInstalled
            ? new UpdateStatus(UpdatePhase.Unsupported, _backend.CurrentVersion)
            : _backend.PendingVersion is { } pending
                ? new UpdateStatus(UpdatePhase.ReadyToRestart, _backend.CurrentVersion, pending, 100)
                : new UpdateStatus(UpdatePhase.Idle, _backend.CurrentVersion);
    }

    public UpdateStatus Status { get; private set; }
    public event Action? Changed;

    public async Task CheckNowAsync(CancellationToken cancellationToken = default)
    {
        if (!_backend.IsInstalled || Status.Phase is UpdatePhase.Downloading or UpdatePhase.ReadyToRestart) return;
        if (!await _gate.WaitAsync(0, cancellationToken)) return;

        var previous = Status;
        var attemptedAt = DateTimeOffset.Now;
        try
        {
            Publish(previous with { Phase = UpdatePhase.Checking, LastAttemptAt = attemptedAt, FailureCode = null });
            _candidate = await _backend.CheckAsync(cancellationToken);
            Publish(_candidate is null
                ? new UpdateStatus(UpdatePhase.Current, _backend.CurrentVersion, LastAttemptAt: attemptedAt, LastSuccessfulCheckAt: DateTimeOffset.Now)
                : new UpdateStatus(UpdatePhase.Available, _backend.CurrentVersion, _candidate.Version, LastAttemptAt: attemptedAt, LastSuccessfulCheckAt: DateTimeOffset.Now));
            _log.LogInformation("APS Planner update check completed. State={State} AvailableVersion={AvailableVersion}", Status.Phase, Status.AvailableVersion);
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
                : new UpdateStatus(UpdatePhase.Failed, _backend.CurrentVersion, previous.AvailableVersion, LastAttemptAt: attemptedAt, LastSuccessfulCheckAt: previous.LastSuccessfulCheckAt, FailureCode: code));
            _log.LogWarning(exception, "APS Planner update check failed. FailureCode={FailureCode}", code);
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
            await _backend.DownloadAsync(_candidate, progress => Publish(Status with { DownloadProgress = progress }), cancellationToken);
            Publish(Status with { Phase = UpdatePhase.ReadyToRestart, DownloadProgress = 100 });
            _log.LogInformation("APS Planner update {Version} downloaded and ready to restart.", Status.AvailableVersion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Publish(available);
        }
        catch (Exception exception)
        {
            Publish(available with { FailureCode = "DownloadFailed" });
            _log.LogWarning(exception, "APS Planner update download failed. Version={Version}", available.AvailableVersion);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void RestartAndApply()
    {
        if (Status.Phase != UpdatePhase.ReadyToRestart) return;
        _log.LogInformation("Restarting APS Planner to apply update {Version}.", Status.AvailableVersion);
        _backend.WaitExitThenApply();
        _requestShutdown();
    }

    private void Publish(UpdateStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }

    private static string Classify(Exception exception) => exception switch
    {
        HttpRequestException http when http.StatusCode == System.Net.HttpStatusCode.Forbidden => "RateLimited",
        HttpRequestException => "NetworkUnavailable",
        _ => "FeedUnavailable"
    };

    public void Dispose() => _gate.Dispose();
}
