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
    IReadOnlyCollection<RouteResourceCapability> RouteResourceCapabilities,
    IReadOnlyCollection<PlantArea>? PlantAreas = null,
    IReadOnlyCollection<SteelGrade>? SteelGrades = null,
    IReadOnlyCollection<CrossSectionSpecification>? CrossSections = null,
    IReadOnlyCollection<MaterialSpecification>? MaterialSpecifications = null,
    IReadOnlyCollection<PackagingSpecification>? PackagingSpecifications = null,
    IReadOnlyCollection<ExternalMaterialSupply>? ExternalMaterialSupplies = null)
{
    public IReadOnlyCollection<PlantArea> EffectivePlantAreas => PlantAreas ?? Array.Empty<PlantArea>();
    public IReadOnlyCollection<SteelGrade> EffectiveSteelGrades => SteelGrades ?? Array.Empty<SteelGrade>();
    public IReadOnlyCollection<CrossSectionSpecification> EffectiveCrossSections => CrossSections ?? Array.Empty<CrossSectionSpecification>();
    public IReadOnlyCollection<MaterialSpecification> EffectiveMaterialSpecifications => MaterialSpecifications ?? Array.Empty<MaterialSpecification>();
    public IReadOnlyCollection<PackagingSpecification> EffectivePackagingSpecifications => PackagingSpecifications ?? Array.Empty<PackagingSpecification>();
    public IReadOnlyCollection<ExternalMaterialSupply> EffectiveExternalMaterialSupplies => ExternalMaterialSupplies ?? Array.Empty<ExternalMaterialSupply>();

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
