using APS.Domain;

namespace APS.Application;

/// <summary>
/// PO-grain material answer produced by the canonical BOM/time-phased material pass before Campaign formation.
/// Campaign is a consumer of these facts; it must not independently reserve/net the same stock again.
/// RollingRequirementMt is finished/rolling output demand. SteelFeedRequirementMt is the upstream billet/bloom/slab
/// requirement after configured yield/loss and therefore may legitimately be larger than rolling demand.
/// </summary>
public sealed record PrecomputedCampaignMaterialDemand(
    Guid ProductionOrderId,
    decimal RollingRequirementMt,
    decimal SteelFeedRequirementMt,
    decimal CoveredIntermediateMt,
    decimal FreshSteelRequirementMt,
    decimal UncoveredSteelFeedShortfallMt,
    IReadOnlyCollection<PlanningInventoryAllocation> CoverageAllocations,
    IReadOnlyCollection<Guid>? MaterialRequirementIds = null);
