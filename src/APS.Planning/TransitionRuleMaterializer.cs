using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class TransitionRuleMaterializer
{
    public static IReadOnlyCollection<TransitionRule> Materialize(
        IReadOnlyCollection<TransitionRule> rules,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyCollection<ProductionOrder> productionOrders,
        IReadOnlyCollection<SteelGrade>? grades,
        IReadOnlyCollection<CrossSectionSpecification>? crossSections,
        RoutePlanningInput? routePlanning)
    {
        if (rules.Count == 0) return rules;

        var gradeCodes = productionOrders
            .Select(x => x.GradeCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sectionCodes = productionOrders
            .SelectMany(x => new[] { x.CasterSectionCode, x.FinalCrossSectionCode })
            .Concat(routePlanning?.Operations.SelectMany(x => new[] { x.InputCrossSectionCode, x.OutputCrossSectionCode }) ?? Array.Empty<string?>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var operations = routePlanning?.Operations
            .Select(x => x.ProcessOperationType)
            .Where(x => x != ProcessOperationType.Unknown)
            .Distinct()
            .ToArray()
            ?? Enum.GetValues<ProcessOperationType>().Where(x => x != ProcessOperationType.Unknown).ToArray();

        var effective = new List<TransitionRule>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in resources.Where(x => x.IsActive))
        {
            foreach (var operation in operations)
            {
                MaterializeDimension(
                    rules,
                    resource,
                    operation,
                    TransitionDimension.Grade,
                    gradeCodes,
                    grades,
                    crossSections,
                    effective,
                    keys);

                MaterializeDimension(
                    rules,
                    resource,
                    operation,
                    TransitionDimension.CrossSection,
                    sectionCodes,
                    grades,
                    crossSections,
                    effective,
                    keys);
            }
        }

        // Exact rules that reference codes outside the current demand set are intentionally not copied;
        // the effective rule set is a plan-run snapshot, not a duplicate master table.
        return effective;
    }

    private static void MaterializeDimension(
        IReadOnlyCollection<TransitionRule> rules,
        Resource resource,
        ProcessOperationType operation,
        TransitionDimension dimension,
        IReadOnlyCollection<string> codes,
        IReadOnlyCollection<SteelGrade>? grades,
        IReadOnlyCollection<CrossSectionSpecification>? crossSections,
        ICollection<TransitionRule> output,
        ISet<string> keys)
    {
        foreach (var from in codes)
        {
            foreach (var to in codes)
            {
                if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) continue;
                var resolved = TransitionRuleResolver.Resolve(
                    rules,
                    resource,
                    operation,
                    dimension,
                    from,
                    to,
                    grades,
                    crossSections);
                if (resolved is null) continue;

                var key = $"{resource.Id:N}|{operation}|{dimension}|{from}|{to}";
                if (!keys.Add(key)) continue;

                output.Add(new TransitionRule
                {
                    ResourceId = resource.Id,
                    ResourceType = resource.ResourceType,
                    ProcessUnitType = resource.ProcessUnitType,
                    ProcessOperationType = operation,
                    Scope = TransitionRuleScope.ExactCode,
                    Dimension = dimension,
                    FromCode = from,
                    ToCode = to,
                    IsAllowed = resolved.IsAllowed,
                    RequiresSequenceBreak = resolved.RequiresSequenceBreak,
                    Penalty = resolved.Penalty,
                    TransitionTime = resolved.TransitionTime,
                    ReasonCode = resolved.ReasonCode
                });
            }
        }
    }
}
