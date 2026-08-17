using APS.Domain;

namespace APS.Planning;

internal static class TransitionRuleResolver
{
    public static TransitionRule? Resolve(
        IReadOnlyCollection<TransitionRule> rules,
        Resource resource,
        ProcessOperationType processOperationType,
        TransitionDimension dimension,
        string? fromCode,
        string? toCode,
        IReadOnlyCollection<SteelGrade>? grades = null,
        IReadOnlyCollection<CrossSectionSpecification>? crossSections = null)
    {
        if (string.IsNullOrWhiteSpace(fromCode) || string.IsNullOrWhiteSpace(toCode)) return null;
        if (string.Equals(fromCode, toCode, StringComparison.OrdinalIgnoreCase)) return null;

        var gradeByCode = dimension == TransitionDimension.Grade
            ? (grades ?? Array.Empty<SteelGrade>()).ToDictionary(x => x.GradeCode, StringComparer.OrdinalIgnoreCase)
            : null;
        var sectionByCode = dimension == TransitionDimension.CrossSection
            ? (crossSections ?? Array.Empty<CrossSectionSpecification>()).ToDictionary(x => x.CrossSectionCode, StringComparer.OrdinalIgnoreCase)
            : null;

        var candidates = new List<RuleCandidate>();
        foreach (var rule in rules.Where(x => x.Dimension == dimension))
        {
            if (!ResourceMatches(rule, resource, processOperationType)) continue;
            if (!TryResolveScopedCodes(
                    rule.Scope,
                    dimension,
                    processOperationType,
                    fromCode,
                    toCode,
                    gradeByCode,
                    sectionByCode,
                    out var scopedFrom,
                    out var scopedTo))
            {
                continue;
            }

            if (!CodeMatches(rule.FromCode, scopedFrom) || !CodeMatches(rule.ToCode, scopedTo)) continue;
            candidates.Add(new RuleCandidate(rule, ScopeRank(rule.Scope), ResourceSpecificity(rule, resource, processOperationType)));
        }

        return candidates
            .OrderByDescending(x => x.ScopeRank)
            .ThenByDescending(x => x.ResourceSpecificity)
            .ThenByDescending(x => x.Rule.Id)
            .Select(x => x.Rule)
            .FirstOrDefault();
    }

    private static bool TryResolveScopedCodes(
        TransitionRuleScope scope,
        TransitionDimension dimension,
        ProcessOperationType operation,
        string fromCode,
        string toCode,
        IReadOnlyDictionary<string, SteelGrade>? grades,
        IReadOnlyDictionary<string, CrossSectionSpecification>? sections,
        out string? scopedFrom,
        out string? scopedTo)
    {
        scopedFrom = fromCode;
        scopedTo = toCode;

        if (scope == TransitionRuleScope.Default)
        {
            scopedFrom = "*";
            scopedTo = "*";
            return true;
        }

        if (scope == TransitionRuleScope.ExactCode) return true;

        if (dimension == TransitionDimension.Grade)
        {
            if (grades is null || !grades.TryGetValue(fromCode, out var fromGrade) || !grades.TryGetValue(toCode, out var toGrade)) return false;
            if (scope is TransitionRuleScope.Class or TransitionRuleScope.SequenceClass)
            {
                scopedFrom = fromGrade.SequenceClassCode;
                scopedTo = toGrade.SequenceClassCode;
            }
            else
            {
                scopedFrom = fromGrade.GradeFamilyCode;
                scopedTo = toGrade.GradeFamilyCode;
            }
            return !string.IsNullOrWhiteSpace(scopedFrom) && !string.IsNullOrWhiteSpace(scopedTo);
        }

        if (dimension == TransitionDimension.CrossSection)
        {
            if (sections is null || !sections.TryGetValue(fromCode, out var fromSection) || !sections.TryGetValue(toCode, out var toSection)) return false;
            if (scope is TransitionRuleScope.Class or TransitionRuleScope.SequenceClass)
            {
                scopedFrom = SectionClass(fromSection, operation);
                scopedTo = SectionClass(toSection, operation);
            }
            else
            {
                scopedFrom = fromSection.SectionFamilyCode;
                scopedTo = toSection.SectionFamilyCode;
            }
            return !string.IsNullOrWhiteSpace(scopedFrom) && !string.IsNullOrWhiteSpace(scopedTo);
        }

        // Product family currently supports exact/default rules. Class/family scopes are intentionally not inferred.
        return false;
    }

    private static string? SectionClass(CrossSectionSpecification section, ProcessOperationType operation) => operation switch
    {
        ProcessOperationType.Ccm => section.CasterFormatClassCode,
        ProcessOperationType.HotRoll or ProcessOperationType.ColdRoll or ProcessOperationType.Tmt => section.RollingFamilyCode,
        _ => section.SectionFamilyCode
    };

    private static bool ResourceMatches(TransitionRule rule, Resource resource, ProcessOperationType processOperationType) =>
        (!rule.ResourceId.HasValue || rule.ResourceId == resource.Id) &&
        (!rule.ResourceType.HasValue || rule.ResourceType == resource.ResourceType) &&
        (!rule.ProcessUnitType.HasValue || rule.ProcessUnitType == resource.ProcessUnitType) &&
        (!rule.ProcessOperationType.HasValue || rule.ProcessOperationType == processOperationType);

    private static int ResourceSpecificity(TransitionRule rule, Resource resource, ProcessOperationType processOperationType)
    {
        var score = 0;
        if (rule.ResourceId == resource.Id) score += 1000;
        if (rule.ProcessUnitType == resource.ProcessUnitType) score += 100;
        if (rule.ProcessOperationType == processOperationType) score += 50;
        if (rule.ResourceType == resource.ResourceType) score += 10;
        return score;
    }

    private static int ScopeRank(TransitionRuleScope scope) => scope switch
    {
        TransitionRuleScope.ExactCode => 400,
        TransitionRuleScope.Class or TransitionRuleScope.SequenceClass => 300,
        TransitionRuleScope.Family or TransitionRuleScope.GradeFamily => 200,
        _ => 100
    };

    private static bool CodeMatches(string configured, string? actual) =>
        string.IsNullOrWhiteSpace(configured) || configured == "*" || string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);

    private sealed record RuleCandidate(TransitionRule Rule, int ScopeRank, int ResourceSpecificity);
}
