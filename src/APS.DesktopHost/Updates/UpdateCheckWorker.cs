using APS.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APS.DesktopHost.Updates;

public sealed class UpdateCheckWorker(IUpdateService updates, ILogger<UpdateCheckWorker> log) : BackgroundService
{
    private static readonly TimeSpan RegularInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (updates.Status.Phase == UpdatePhase.Unsupported)
        {
            log.LogDebug("Skipping update checks because APS Planner is not running from a Velopack installation.");
            return;
        }

        var consecutiveFailures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            await updates.CheckNowAsync(stoppingToken);

            consecutiveFailures = updates.Status.FailureCode is null ? 0 : consecutiveFailures + 1;
            var delay = consecutiveFailures == 0
                ? RegularInterval + TimeSpan.FromMinutes(Random.Shared.NextDouble() * 5)
                : TimeSpan.FromHours(Math.Min(Math.Pow(2, consecutiveFailures), MaximumBackoff.TotalHours));

            log.LogDebug(
                "Next APS Planner update check scheduled in {Delay}. ConsecutiveFailures={ConsecutiveFailures}",
                delay,
                consecutiveFailures);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
