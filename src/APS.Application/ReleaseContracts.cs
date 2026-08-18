using APS.Domain;

namespace APS.Application;

/// <summary>
/// In-memory/demo release input. Production release must use IPersistedPlanReleaseService so the
/// caller cannot replace persisted Plan Version structure or schedule with a different payload.
/// </summary>
public sealed record PlanReleaseBuildRequest(
    Guid PlanVersionId,
    IReadOnlyCollection<Campaign> Campaigns,
    ProductionStructurePlanningResult ProductionStructure,
    FiniteScheduleResult Schedule);

public interface IPlanReleaseBuilder
{
    PlanRelease Build(PlanReleaseBuildRequest request);
}

public interface IPersistedPlanReleaseService
{
    Task<PlanRelease> ReleaseAsync(
        Guid planVersionId,
        CancellationToken cancellationToken = default);
}

public interface IPlanReleaseRepository
{
    Task<PlanRelease> PersistAsync(
        PlanRelease release,
        CancellationToken cancellationToken = default);
}
