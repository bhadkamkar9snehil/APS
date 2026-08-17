using APS.Domain;

namespace APS.Application;

public sealed record PlanReleaseBuildRequest(
    Guid PlanVersionId,
    IReadOnlyCollection<Campaign> Campaigns,
    ProductionStructurePlanningResult ProductionStructure,
    FiniteScheduleResult Schedule);

public interface IPlanReleaseBuilder
{
    PlanRelease Build(PlanReleaseBuildRequest request);
}
