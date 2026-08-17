using APS.Domain;

namespace APS.Application;

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
    string CampaignNumberPrefix = "CMP");

public sealed record PlanningRunResult(
    Guid PlanVersionId,
    DateTime CreatedOnUtc,
    CampaignPlanningResult CampaignPlan,
    ProductionStructurePlanningResult ProductionStructure,
    FiniteScheduleResult Schedule,
    bool IsFeasible);

public interface IPlanningEngine
{
    PlanningRunResult Run(PlanningRunRequest request);
}
