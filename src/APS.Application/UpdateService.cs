namespace APS.Application;

public enum UpdatePhase
{
    Unsupported,
    Idle,
    Checking,
    Current,
    Available,
    Downloading,
    ReadyToRestart,
    Failed
}

public sealed record UpdateStatus(
    UpdatePhase Phase,
    string CurrentVersion,
    string? AvailableVersion = null,
    int? DownloadProgress = null,
    DateTimeOffset? LastAttemptAt = null,
    DateTimeOffset? LastSuccessfulCheckAt = null,
    string? FailureCode = null);

public interface IUpdateService
{
    UpdateStatus Status { get; }
    event Action? Changed;

    Task CheckNowAsync(CancellationToken cancellationToken = default);
    Task DownloadAsync(CancellationToken cancellationToken = default);
    void RestartAndApply();
}
