using APS.Domain;

namespace APS.Application;

public sealed record PlanningMasterDataSnapshot(
    IReadOnlyCollection<Plant> Plants,
    IReadOnlyCollection<ProcessStage> ProcessStages,
    IReadOnlyCollection<Resource> Resources,
    IReadOnlyCollection<ResourceCapability> ResourceCapabilities,
    IReadOnlyCollection<ResourceCalendar> ResourceCalendars,
    IReadOnlyCollection<PlantFlowLink> FlowLinks,
    IReadOnlyCollection<TransitionRule> TransitionRules,
    IReadOnlyCollection<ManufacturingRoute> Routes,
    IReadOnlyCollection<ManufacturingRouteOperation> RouteOperations,
    IReadOnlyCollection<RouteResourceCapability> RouteResourceCapabilities)
{
    public RoutePlanningInput? RoutePlanning => RouteOperations.Count == 0
        ? null
        : new RoutePlanningInput(RouteOperations, RouteResourceCapabilities);
}

public interface IPlanningMasterDataProvider
{
    Task<PlanningMasterDataSnapshot> GetAsync(CancellationToken cancellationToken = default);
}

public sealed record PlanningCalculationRequest(
    IReadOnlyCollection<ProductionOrder> ProductionOrders,
    CampaignPlanningPolicy CampaignPolicy,
    ProductionStructurePlanningPolicy StructurePolicy,
    DateTime HorizonStartUtc,
    DateTime HorizonEndUtc,
    int MaxSolverSeconds = 20,
    string CampaignNumberPrefix = "CMP");
