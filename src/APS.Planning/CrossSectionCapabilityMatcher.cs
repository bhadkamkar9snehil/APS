using APS.Domain;

namespace APS.Planning;

internal static class CrossSectionCapabilityMatcher
{
    public static bool Matches(
        RouteResourceCapability capability,
        string inputCode,
        string outputCode,
        IReadOnlyCollection<CrossSectionSpecification>? crossSections)
    {
        var byCode = (crossSections ?? Array.Empty<CrossSectionSpecification>())
            .ToDictionary(x => x.CrossSectionCode, StringComparer.OrdinalIgnoreCase);

        byCode.TryGetValue(inputCode, out var input);
        byCode.TryGetValue(outputCode, out var output);

        return Exact(capability.InputCrossSectionCode, inputCode) &&
               Exact(capability.OutputCrossSectionCode, outputCode) &&
               Family(capability.InputSectionFamilyCode, input?.SectionFamilyCode) &&
               Family(capability.OutputSectionFamilyCode, output?.SectionFamilyCode) &&
               Family(capability.InputCasterFormatClassCode, input?.CasterFormatClassCode) &&
               Family(capability.OutputRollingFamilyCode, output?.RollingFamilyCode);
    }

    private static bool Exact(string? configured, string actual) =>
        string.IsNullOrWhiteSpace(configured) ||
        string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);

    private static bool Family(string? configured, string? actual) =>
        string.IsNullOrWhiteSpace(configured) ||
        (!string.IsNullOrWhiteSpace(actual) && string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase));
}
