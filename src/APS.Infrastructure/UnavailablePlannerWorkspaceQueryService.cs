using APS.Application;

namespace APS.Infrastructure;

/// <summary>
/// Allows the explicitly enabled no-database demo sandbox to render its shared layout. In normal
/// production mode a missing APS database is a configuration failure, not an empty planning state.
/// </summary>
public sealed class UnavailablePlannerWorkspaceQueryService : IPlannerWorkspaceQueryService
{
    private readonly bool _allowEmptyDemoWorkspace;

    public UnavailablePlannerWorkspaceQueryService(bool allowEmptyDemoWorkspace = false)
    {
        _allowEmptyDemoWorkspace = allowEmptyDemoWorkspace;
    }

    public Task<PlanContextView?> GetCurrentPlanAsync(CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        return Task.FromResult<PlanContextView?>(null);
    }

    public Task<PlanContextView?> GetPlanContextAsync(
        Guid planVersionId,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        return Task.FromResult<PlanContextView?>(null);
    }

    public Task<IReadOnlyCollection<PlanVersionListItemView>> GetRecentPlanVersionsAsync(
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        return Task.FromResult<IReadOnlyCollection<PlanVersionListItemView>>(Array.Empty<PlanVersionListItemView>());
    }

    public Task<ControlTowerView?> GetControlTowerAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        return Task.FromResult<ControlTowerView?>(null);
    }

    public Task<DemandSupplyView?> GetDemandSupplyAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        return Task.FromResult<DemandSupplyView?>(null);
    }

    public Task<CampaignStudioView?> GetCampaignStudioAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        return Task.FromResult<CampaignStudioView?>(null);
    }

    public Task<SteelmakingCastingWorkspaceView?> GetSteelmakingCastingAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        return Task.FromResult<SteelmakingCastingWorkspaceView?>(null);
    }

    public Task<FiniteScheduleWorkspaceView?> GetFiniteScheduleAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        return Task.FromResult<FiniteScheduleWorkspaceView?>(null);
    }

    public Task<RollingFinishingWorkspaceView?> GetRollingFinishingAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        return Task.FromResult<RollingFinishingWorkspaceView?>(null);
    }

    public Task<WorkOrdersWorkspaceView?> GetWorkOrdersAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        return Task.FromResult<WorkOrdersWorkspaceView?>(null);
    }

    public Task<PlanComparisonWorkspaceView?> GetPlanComparisonAsync(
        Guid baselinePlanVersionId,
        Guid newPlanVersionId,
        CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        return Task.FromResult<PlanComparisonWorkspaceView?>(null);
    }

    private void EnsureAvailable()
    {
        if (_allowEmptyDemoWorkspace) return;

        throw new InvalidOperationException(
            "The APS production planner workspace is unavailable because the APS SQL database is not configured. " +
            "Configure the production database or explicitly enable APS:DemoModeEnabled for the isolated demo sandbox.");
    }
}
