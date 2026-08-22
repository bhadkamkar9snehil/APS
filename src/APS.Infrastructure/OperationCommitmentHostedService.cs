using APS.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace APS.Infrastructure;

/// <summary>
/// Keeps time-based resource commitment current even when no MES/manual execution event arrives.
/// The active Plan Version remains the sole target; running/completed operations are monotonic and
/// predecessor-driven commitment is evaluated by OperationExecutionService from immutable snapshots.
/// </summary>
public sealed class OperationCommitmentHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<OperationCommitmentHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApsDbContext>();
                var execution = scope.ServiceProvider.GetRequiredService<IOperationExecutionService>();
                var activePlanIds = await db.PlanVersionStates
                    .AsNoTracking()
                    .Where(x => x.IsActive)
                    .Select(x => x.PlanVersionId)
                    .ToArrayAsync(stoppingToken);

                var now = DateTime.UtcNow;
                foreach (var planVersionId in activePlanIds)
                    await execution.RefreshCommitmentsAsync(planVersionId, now, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to refresh APS operation assignment commitments.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
