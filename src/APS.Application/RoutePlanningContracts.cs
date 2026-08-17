using APS.Domain;

namespace APS.Application;

public sealed record RoutePlanningInput(
    IReadOnlyCollection<ManufacturingRouteOperation> Operations,
    IReadOnlyCollection<RouteResourceCapability> ResourceCapabilities);
