using APS.Domain;

namespace APS.Application;

/// <summary>
/// PO-grain material answer produced by the canonical BOM/time-phased material pass before Campaign formation.
/// Campaign is a consumer of these facts; it must not independently reserve/net the same stock again.
/// </summary>
public sealed record PrecomputedCampaignMaterialDemand(
    Guid ProductionOrderId,
    decimal RollingRequirementMt,
    decimal CoveredIntermediateMt,
    decimal FreshSteelRequirementMt,
    IReadOnlyCollection<PlanningInventoryAllocation> CoverageAllocations,
    IReadOnlyCollection<Guid>? MaterialRequirementIds = null);
