using APS.Application;

namespace APS.Infrastructure;

public sealed class UnavailablePlannerWorkspaceQueryService : IPlannerWorkspaceQueryService
{
    public Task<PlanContextView?> GetCurrentPlanAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<PlanContextView?>(null);

    public Task<PlanContextView?> GetPlanContextAsync(
        Guid planVersionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<PlanContextView?>(null);

    public Task<IReadOnlyCollection<PlanVersionListItemView>> GetRecentPlanVersionsAsync(
        int take = 20,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<PlanVersionListItemView>>(Array.Empty<PlanVersionListItemView>());

    public Task<ControlTowerView?> GetControlTowerAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ControlTowerView?>(null);

    public Task<DemandSupplyView?> GetDemandSupplyAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<DemandSupplyView?>(null);

    public Task<CampaignStudioView?> GetCampaignStudioAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<CampaignStudioView?>(null);
}
