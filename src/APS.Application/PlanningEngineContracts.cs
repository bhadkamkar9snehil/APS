using APS.Domain;

namespace APS.Application;

public sealed record PlanningTimeFencePolicy(
    int FrozenMinutes = 120,
    int SlushyMinutes = 720,
    int SlushyMovementPenaltyPerMinute = 50,
    int SlushyResourceChangePenalty = 5000);

public sealed record BaselinePlanOperation(
    string PlanningKey,
    Guid ResourceId,
    DateTime StartUtc,
    DateTime EndUtc,
    FiniteScheduleTaskType TaskType);

public sealed record PlanningReplanContext(
    Guid BaselinePlanVersionId,
    DateTime ReferenceTimeUtc,
    PlanningTimeFencePolicy TimeFencePolicy,
    IReadOnlyCollection<BaselinePlanOperation> BaselineOperations);

public sealed record PlanningTaskIdentity(
    Guid TaskId,
    Guid SourceEntityId,
    string PlanningKey,
    FiniteScheduleTaskType TaskType);

public sealed record PlanningRunRequest(
    IReadOnlyCollection<ProductionOrder> ProductionOrders,
    IReadOnlyCollection<InventoryPosition> Inventory,
    IReadOnlyCollection<Resource> Resources,
    IReadOnlyCollection<ResourceCapability> Capabilities,
    IReadOnlyCollection<ResourceCalendar> ResourceCalendars,
    IReadOnlyCollection<TransitionRule> TransitionRules,
    IReadOnlyCollection<PlantFlowLink> FlowLinks,
    CampaignPlanningPolicy CampaignPolicy,
    ProductionStructurePlanningPolicy StructurePolicy,
    DateTime HorizonStartUtc,
    DateTime HorizonEndUtc,
    int MaxSolverSeconds = 20,
    string CampaignNumberPrefix = "CMP",
    PlanningReplanContext? ReplanContext = null,
    RoutePlanningInput? RoutePlanning = null);

public sealed record PlanningRunResult(
    Guid PlanVersionId,
    DateTime CreatedOnUtc,
    CampaignPlanningResult CampaignPlan,
    ProductionStructurePlanningResult ProductionStructure,
    FiniteScheduleResult Schedule,
    bool IsFeasible,
    IReadOnlyCollection<PlanningTaskIdentity>? TaskIdentities = null,
    Guid? BaselinePlanVersionId = null);

public interface IPlanningEngine
{
    PlanningRunResult Run(PlanningRunRequest request);
}
