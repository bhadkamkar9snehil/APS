using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class SteelmakingMakeFeasibilityEvaluator
{
    private static readonly ProcessOperationType[] DefaultOrder =
    {
        ProcessOperationType.Eaf,
        ProcessOperationType.Lrf,
        ProcessOperationType.Vd,
        ProcessOperationType.Ccm
    };

    public static MakePathFeasibility Evaluate(ProductionOrder po, CampaignPlanningRequest request)
    {
        if (request.Resources is null || request.Resources.Count == 0)
            return new MakePathFeasibility(true, "Plant topology was not supplied; legacy campaign planning cannot pre-validate the internal route.", new Dictionary<ProcessOperationType, IReadOnlyCollection<Guid>>());

        var operations = RequiredOperations(po, request.RoutePlanning);
        var eligible = new Dictionary<ProcessOperationType, IReadOnlyCollection<Guid>>();
        foreach (var operation in operations)
        {
            var ids = EligibleResources(operation, po, request).Select(x => x.Id).Distinct().ToArray();
            eligible[operation] = ids;
            if (ids.Length == 0)
            {
                return new MakePathFeasibility(
                    false,
                    $"No active eligible {operation} resource satisfies route, grade/order capability and required-resource constraints.",
                    eligible);
            }
        }

        var links = request.FlowLinks ?? Array.Empty<PlantFlowLink>();
        if (links.Count > 0)
        {
            for (var i = 0; i < operations.Count - 1; i++)
            {
                var fromOperation = operations[i];
                var toOperation = operations[i + 1];
                var fromIds = eligible[fromOperation];
                var toIds = eligible[toOperation];
                var connected = links.Any(link =>
                    link.IsEnabled &&
                    fromIds.Contains(link.FromResourceId) &&
                    toIds.Contains(link.ToResourceId) &&
                    (!link.FromProcessOperationType.HasValue || link.FromProcessOperationType == fromOperation) &&
                    (!link.ToProcessOperationType.HasValue || link.ToProcessOperationType == toOperation));

                if (!connected)
                {
                    return new MakePathFeasibility(
                        false,
                        $"Eligible {fromOperation} and {toOperation} resources exist, but no enabled physical material-flow link connects them.",
                        eligible);
                }
            }
        }

        return new MakePathFeasibility(true, "At least one complete active internal steelmaking resource path exists.", eligible);
    }

    private static IReadOnlyList<ProcessOperationType> RequiredOperations(
        ProductionOrder po,
        RoutePlanningInput? routePlanning)
    {
        var required = new HashSet<ProcessOperationType>
        {
            ProcessOperationType.Eaf,
            ProcessOperationType.Ccm
        };

        var routeOperations = (routePlanning?.Operations ?? Array.Empty<ManufacturingRouteOperation>())
            .Where(x => Same(x.RouteCode, po.RouteCode) && IsSteelmaking(x.ProcessOperationType))
            .OrderBy(x => x.SequenceNumber)
            .ToArray();

        foreach (var routeOperation in routeOperations)
        {
            if (ResolveRequirement(routeOperation.ProcessOperationType, po, routeOperation.Requirement) == RequirementDisposition.Required)
                required.Add(routeOperation.ProcessOperationType);
            else if (ResolveRequirement(routeOperation.ProcessOperationType, po, routeOperation.Requirement) == RequirementDisposition.Forbidden)
                required.Remove(routeOperation.ProcessOperationType);
        }

        foreach (var gradeRequirement in po.SteelGrade?.ProcessRequirements ?? Array.Empty<GradeProcessRequirement>())
        {
            if (!IsSteelmaking(gradeRequirement.ProcessOperationType)) continue;
            if (gradeRequirement.Requirement == RequirementDisposition.Required) required.Add(gradeRequirement.ProcessOperationType);
            if (gradeRequirement.Requirement == RequirementDisposition.Forbidden) required.Remove(gradeRequirement.ProcessOperationType);
        }

        foreach (var orderRequirement in po.Requirement?.ProcessOverrides ?? Array.Empty<OrderProcessRequirement>())
        {
            if (!IsSteelmaking(orderRequirement.ProcessOperationType)) continue;
            if (orderRequirement.Requirement == RequirementDisposition.Required) required.Add(orderRequirement.ProcessOperationType);
            if (orderRequirement.Requirement == RequirementDisposition.Forbidden) required.Remove(orderRequirement.ProcessOperationType);
        }

        if (po.Requirement?.RequireVd == true) required.Add(ProcessOperationType.Vd);
        if (po.Requirement?.ForbidVd == true) required.Remove(ProcessOperationType.Vd);

        var routeOrder = routeOperations.Select(x => x.ProcessOperationType).Distinct().ToArray();
        return required
            .OrderBy(operation =>
            {
                var routeIndex = Array.IndexOf(routeOrder, operation);
                if (routeIndex >= 0) return routeIndex;
                var defaultIndex = Array.IndexOf(DefaultOrder, operation);
                return 100 + (defaultIndex >= 0 ? defaultIndex : (int)operation);
            })
            .ToArray();
    }

    private static IEnumerable<Resource> EligibleResources(
        ProcessOperationType operation,
        ProductionOrder po,
        CampaignPlanningRequest request)
    {
        var requiredResourceIds = RequiredResourceIds(po, operation);
        var requiredCapabilityClass = RequiredCapabilityClass(po, operation);
        var routeCaps = request.RoutePlanning?.ResourceCapabilities
            .Where(x => Same(x.RouteCode, po.RouteCode) && x.ProcessOperationType == operation)
            .ToArray() ?? Array.Empty<RouteResourceCapability>();
        var capabilities = request.ResourceCapabilities ?? Array.Empty<ResourceCapability>();

        foreach (var resource in request.Resources ?? Array.Empty<Resource>())
        {
            if (!resource.IsActive ||
                resource.OperatingState is ResourceOperatingState.Breakdown or ResourceOperatingState.Disabled or ResourceOperatingState.PlannedMaintenance ||
                !MatchesOperation(resource, operation))
                continue;
            if (requiredResourceIds.Count > 0 && !requiredResourceIds.Contains(resource.Id)) continue;

            if (routeCaps.Length > 0)
            {
                var routeMatch = routeCaps.Any(x =>
                    x.ResourceId == resource.Id &&
                    Matches(x.GradeCode, po.GradeCode) &&
                    Matches(x.GradeFamilyCode, po.GradeFamilyCode) &&
                    Matches(x.ProductFamilyCode, po.ProductFamilyCode));
                if (!routeMatch) continue;
            }

            var resourceCaps = capabilities.Where(x => x.ResourceId == resource.Id).ToArray();
            if (resourceCaps.Length > 0)
            {
                var matching = resourceCaps.Where(x =>
                        (!x.ProcessOperationType.HasValue || x.ProcessOperationType == operation) &&
                        Matches(x.RouteCode, po.RouteCode) &&
                        Matches(x.GradeCode, po.GradeCode) &&
                        Matches(x.GradeFamilyCode, po.GradeFamilyCode) &&
                        Matches(x.ProductFamilyCode, po.ProductFamilyCode))
                    .ToArray();
                if (matching.Length == 0) continue;
                if (!string.IsNullOrWhiteSpace(requiredCapabilityClass) &&
                    !matching.Any(x => Same(x.CapabilityClassCode, requiredCapabilityClass)))
                    continue;
            }
            else if (!string.IsNullOrWhiteSpace(requiredCapabilityClass))
            {
                continue;
            }

            yield return resource;
        }
    }

    private static HashSet<Guid> RequiredResourceIds(ProductionOrder po, ProcessOperationType operation)
    {
        var result = new HashSet<Guid>();
        if (po.Requirement?.RequiredResourceId is { } general) result.Add(general);
        foreach (var specific in po.Requirement?.ProcessOverrides
                     .Where(x => x.ProcessOperationType == operation && x.RequiredResourceId.HasValue)
                     .Select(x => x.RequiredResourceId!.Value)
                 ?? Enumerable.Empty<Guid>())
        {
            result.Add(specific);
        }
        return result;
    }

    private static string? RequiredCapabilityClass(ProductionOrder po, ProcessOperationType operation)
    {
        var order = po.Requirement?.ProcessOverrides
            .Where(x => x.ProcessOperationType == operation && !string.IsNullOrWhiteSpace(x.CapabilityClassCode))
            .Select(x => x.CapabilityClassCode)
            .FirstOrDefault();
        return order ?? po.SteelGrade?.ProcessRequirements
            .Where(x => x.ProcessOperationType == operation && !string.IsNullOrWhiteSpace(x.CapabilityClassCode))
            .Select(x => x.CapabilityClassCode)
            .FirstOrDefault();
    }

    private static RequirementDisposition ResolveRequirement(
        ProcessOperationType operation,
        ProductionOrder po,
        RequirementDisposition routeDefault)
    {
        var value = routeDefault;
        var grade = po.SteelGrade?.ProcessRequirements.FirstOrDefault(x => x.ProcessOperationType == operation);
        if (grade is not null) value = grade.Requirement;
        var order = po.Requirement?.ProcessOverrides.FirstOrDefault(x => x.ProcessOperationType == operation);
        if (order is not null) value = order.Requirement;
        if (operation == ProcessOperationType.Vd)
        {
            if (po.Requirement?.RequireVd == true) value = RequirementDisposition.Required;
            if (po.Requirement?.ForbidVd == true) value = RequirementDisposition.Forbidden;
        }
        return value;
    }

    private static bool MatchesOperation(Resource resource, ProcessOperationType operation)
    {
        if (resource.ProcessUnitType != ProcessUnitType.Unknown)
        {
            return operation switch
            {
                ProcessOperationType.Eaf => resource.ProcessUnitType == ProcessUnitType.Eaf,
                ProcessOperationType.Lrf => resource.ProcessUnitType == ProcessUnitType.Lrf,
                ProcessOperationType.Vd => resource.ProcessUnitType == ProcessUnitType.Vd,
                ProcessOperationType.Ccm => resource.ProcessUnitType == ProcessUnitType.Ccm,
                _ => false
            };
        }

        return operation switch
        {
            ProcessOperationType.Eaf => resource.ResourceType == ResourceType.Furnace,
            ProcessOperationType.Lrf or ProcessOperationType.Vd => resource.ResourceType == ResourceType.Refining,
            ProcessOperationType.Ccm => resource.ResourceType == ResourceType.Caster,
            _ => false
        };
    }

    private static bool IsSteelmaking(ProcessOperationType operation) =>
        operation is ProcessOperationType.Eaf or ProcessOperationType.Lrf or ProcessOperationType.Vd or ProcessOperationType.Ccm;

    private static bool Matches(string? configured, string? actual) =>
        string.IsNullOrWhiteSpace(configured) || string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

internal sealed record MakePathFeasibility(
    bool IsFeasible,
    string Explanation,
    IReadOnlyDictionary<ProcessOperationType, IReadOnlyCollection<Guid>> EligibleResourcesByOperation);
