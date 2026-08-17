using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class RouteCapabilityMaterializer
{
    public static RoutePlanningInput? Materialize(
        RoutePlanningInput? input,
        IReadOnlyCollection<ProductionOrder> productionOrders,
        IReadOnlyCollection<CrossSectionSpecification>? crossSections)
    {
        if (input is null) return null;

        var effective = new List<RouteResourceCapability>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var operationsByRoute = input.Operations
            .GroupBy(x => x.RouteCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.SequenceNumber).ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var capability in input.ResourceCapabilities)
        {
            var usesSectionHierarchy =
                !string.IsNullOrWhiteSpace(capability.InputSectionFamilyCode) ||
                !string.IsNullOrWhiteSpace(capability.OutputSectionFamilyCode) ||
                !string.IsNullOrWhiteSpace(capability.InputCasterFormatClassCode) ||
                !string.IsNullOrWhiteSpace(capability.OutputRollingFamilyCode);

            if (!usesSectionHierarchy)
            {
                Add(capability);
                continue;
            }

            foreach (var order in productionOrders.Where(x =>
                         string.Equals(x.RouteCode, capability.RouteCode, StringComparison.OrdinalIgnoreCase) &&
                         Matches(capability.GradeCode, x.GradeCode) &&
                         Matches(capability.GradeFamilyCode, x.GradeFamilyCode) &&
                         Matches(capability.CastingClassCode, x.SteelGrade?.CastingClassCode) &&
                         Matches(capability.ProductFamilyCode, x.ProductFamilyCode)))
            {
                if (!operationsByRoute.TryGetValue(order.RouteCode, out var route)) continue;
                var operation = route.FirstOrDefault(x => x.ProcessOperationType == capability.ProcessOperationType);
                if (operation is null) continue;

                var inputCode = operation.InputCrossSectionCode ??
                                (operation.ProcessOperationType == ProcessOperationType.HotRoll
                                    ? order.CasterSectionCode
                                    : order.FinalCrossSectionCode);
                var outputCode = operation.OutputCrossSectionCode ?? order.FinalCrossSectionCode;

                if (!CrossSectionCapabilityMatcher.Matches(capability, inputCode, outputCode, crossSections)) continue;

                Add(new RouteResourceCapability
                {
                    ResourceId = capability.ResourceId,
                    RouteCode = capability.RouteCode,
                    ProcessOperationType = capability.ProcessOperationType,
                    CapabilityClassCode = capability.CapabilityClassCode,
                    GradeCode = capability.GradeCode,
                    GradeFamilyCode = capability.GradeFamilyCode,
                    CastingClassCode = capability.CastingClassCode,
                    MaterialSpecificationCode = capability.MaterialSpecificationCode,
                    InputCrossSectionCode = inputCode,
                    OutputCrossSectionCode = outputCode,
                    ProductFamilyCode = capability.ProductFamilyCode,
                    MinimumQuantityMt = capability.MinimumQuantityMt,
                    MaximumQuantityMt = capability.MaximumQuantityMt,
                    ThroughputMtPerHour = capability.ThroughputMtPerHour,
                    FixedDurationMinutes = capability.FixedDurationMinutes,
                    AssignmentPenalty = capability.AssignmentPenalty,
                    IsPreferred = capability.IsPreferred
                });
            }
        }

        return input with { ResourceCapabilities = effective, CrossSections = crossSections ?? input.CrossSections };

        void Add(RouteResourceCapability capability)
        {
            var key = $"{capability.ResourceId:N}|{capability.RouteCode}|{capability.ProcessOperationType}|{capability.GradeCode}|{capability.GradeFamilyCode}|{capability.InputCrossSectionCode}|{capability.OutputCrossSectionCode}|{capability.ProductFamilyCode}";
            if (keys.Add(key)) effective.Add(capability);
        }
    }

    private static bool Matches(string? configured, string? actual) =>
        string.IsNullOrWhiteSpace(configured) ||
        string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);
}
