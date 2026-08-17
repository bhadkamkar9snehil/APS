using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class LogicalCastSequenceProjector
{
    public static ProductionStructurePlanningResult Apply(
        ProductionStructurePlanningResult structure,
        ProductionStructurePlanningRequest request)
    {
        var issues = structure.Issues
            .Where(x => x.Code != "CASTER_NOT_ELIGIBLE")
            .ToList();
        var sequences = new List<CastSequence>();
        var supplies = new List<PlannedBilletSupply>();
        var resources = request.Resources
            .Where(IsSchedulableCcm)
            .ToDictionary(x => x.Id);
        var capabilities = request.Capabilities
            .GroupBy(x => x.ResourceId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var priorSupplyByHeat = structure.PlannedBilletSupplies
            .GroupBy(x => x.CampaignHeatId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.QuantityMt));

        var sequenceNumber = 0;
        foreach (var campaign in request.Campaigns.OrderBy(x => x.RequiredDate).ThenBy(x => x.CampaignNumber))
        {
            LogicalSequenceState? current = null;
            foreach (var heat in campaign.Heats.OrderBy(x => x.SequenceNumber))
            {
                var heatCandidates = EligibleCasters(campaign, heat, resources, capabilities).ToHashSet();
                if (heatCandidates.Count == 0)
                {
                    issues.Add(new PlanningIssue(
                        PlanningIssueSeverity.Error,
                        "CASTER_NOT_ELIGIBLE",
                        $"No active CCM can cast heat {campaign.CampaignNumber}/{heat.SequenceNumber:00} ({heat.GradeCode}/{campaign.CasterSectionCode}).",
                        heat.Id));
                    current = null;
                    continue;
                }

                if (current is not null)
                {
                    var nextCount = current.Sequence.Heats.Count + 1;
                    var compatible = current.EligibleCasterIds
                        .Intersect(heatCandidates)
                        .Where(id => CanCarryAdditionalHeat(resources[id], nextCount, request.Policy))
                        .Where(id => TransitionAllowsContinuation(
                            request.TransitionRules,
                            resources[id],
                            current.LastGradeCode,
                            heat.GradeCode))
                        .ToHashSet();

                    if (compatible.Count > 0)
                    {
                        AddHeat(current.Sequence, heat);
                        current = current with
                        {
                            EligibleCasterIds = compatible,
                            LastGradeCode = heat.GradeCode
                        };
                        AddSupply(campaign, heat, current.Sequence, priorSupplyByHeat, supplies);
                        continue;
                    }
                }

                var newCandidates = heatCandidates
                    .Where(id => CanCarryAdditionalHeat(resources[id], 1, request.Policy))
                    .ToHashSet();
                if (newCandidates.Count == 0)
                {
                    issues.Add(new PlanningIssue(
                        PlanningIssueSeverity.Error,
                        "CAST_SEQUENCE_LIMIT_INFEASIBLE",
                        $"No CCM can start a valid cast sequence for heat {campaign.CampaignNumber}/{heat.SequenceNumber:00}.",
                        heat.Id));
                    current = null;
                    continue;
                }

                var sequence = new CastSequence
                {
                    CampaignId = campaign.Id,
                    CasterResourceId = Guid.Empty, // Physical assignment belongs to CP-SAT.
                    SequenceNumber = ++sequenceNumber,
                    CasterSectionCode = campaign.CasterSectionCode,
                    RouteCode = campaign.RouteCode,
                    TundishNumber = 1
                };
                AddHeat(sequence, heat);
                sequences.Add(sequence);
                current = new LogicalSequenceState(sequence, newCandidates, heat.GradeCode);
                AddSupply(campaign, heat, sequence, priorSupplyByHeat, supplies);
            }
        }

        return structure with
        {
            CastSequences = sequences,
            PlannedBilletSupplies = supplies,
            Issues = issues
        };
    }

    private static IEnumerable<Guid> EligibleCasters(
        Campaign campaign,
        CampaignHeat heat,
        IReadOnlyDictionary<Guid, Resource> resources,
        IReadOnlyDictionary<Guid, ResourceCapability[]> capabilities)
    {
        var gradeFamily = campaign.Allocations
            .Select(x => x.ProductionOrder)
            .FirstOrDefault(x => x is not null && Same(x.GradeCode, heat.GradeCode))
            ?.GradeFamilyCode;

        foreach (var resource in resources.Values)
        {
            if (!capabilities.TryGetValue(resource.Id, out var values)) continue;
            var eligible = values.Any(x =>
                (!x.ProcessOperationType.HasValue || x.ProcessOperationType == ProcessOperationType.Ccm) &&
                Matches(x.RouteCode, campaign.RouteCode) &&
                Matches(x.GradeCode, heat.GradeCode) &&
                Matches(x.GradeFamilyCode, gradeFamily) &&
                Matches(x.OutputCrossSectionCode, campaign.CasterSectionCode) &&
                (!x.MinimumQuantityMt.HasValue || heat.PlannedQuantityMt >= x.MinimumQuantityMt.Value) &&
                (!x.MaximumQuantityMt.HasValue || heat.PlannedQuantityMt <= x.MaximumQuantityMt.Value));
            if (eligible) yield return resource.Id;
        }
    }

    private static bool CanCarryAdditionalHeat(Resource caster, int heatCount, ProductionStructurePlanningPolicy policy)
    {
        var limit = new[]
        {
            policy.MaximumHeatsPerCastSequence,
            caster.MaximumHeatsPerSequence ?? int.MaxValue,
            caster.MaximumHeatsPerTundish ?? int.MaxValue
        }.Min();
        return heatCount <= limit;
    }

    private static bool TransitionAllowsContinuation(
        IReadOnlyCollection<TransitionRule> rules,
        Resource caster,
        string fromGrade,
        string toGrade)
    {
        if (Same(fromGrade, toGrade)) return true;
        var rule = rules
            .Where(x => x.Dimension == TransitionDimension.Grade &&
                        Same(x.FromCode, fromGrade) &&
                        Same(x.ToCode, toGrade) &&
                        (!x.ResourceId.HasValue || x.ResourceId == caster.Id) &&
                        (!x.ProcessUnitType.HasValue || x.ProcessUnitType == ProcessUnitType.Ccm) &&
                        (!x.ProcessOperationType.HasValue || x.ProcessOperationType == ProcessOperationType.Ccm))
            .OrderByDescending(x => x.ResourceId == caster.Id)
            .FirstOrDefault();
        return rule is null || (rule.IsAllowed && !rule.RequiresSequenceBreak);
    }

    private static void AddHeat(CastSequence sequence, CampaignHeat heat)
    {
        sequence.Heats.Add(new CastSequenceHeat
        {
            CastSequenceId = sequence.Id,
            CastSequence = sequence,
            CampaignHeatId = heat.Id,
            CampaignHeat = heat,
            Position = sequence.Heats.Count + 1
        });
        heat.PreferredCasterResourceId = null;
    }

    private static void AddSupply(
        Campaign campaign,
        CampaignHeat heat,
        CastSequence sequence,
        IReadOnlyDictionary<Guid, decimal> priorSupplyByHeat,
        ICollection<PlannedBilletSupply> supplies)
    {
        var quantity = priorSupplyByHeat.TryGetValue(heat.Id, out var existing)
            ? existing
            : ExpectedOutput(campaign, heat);
        supplies.Add(new PlannedBilletSupply(
            campaign.Id,
            heat.Id,
            sequence.Id,
            Guid.Empty,
            heat.GradeCode,
            campaign.CasterSectionCode,
            quantity));
    }

    private static decimal ExpectedOutput(Campaign campaign, CampaignHeat heat)
    {
        var gradeHeats = campaign.Heats.Where(x => Same(x.GradeCode, heat.GradeCode)).OrderBy(x => x.SequenceNumber).ToArray();
        var required = campaign.Allocations
            .Where(x => x.ProductionOrder is not null && x.FreshSteelQuantityMt > 0m && Same(x.ProductionOrder.GradeCode, heat.GradeCode))
            .Sum(x => x.FreshSteelQuantityMt);
        var input = gradeHeats.Sum(x => x.PlannedQuantityMt);
        if (required <= 0m || input <= 0m) return 0m;
        var index = Array.FindIndex(gradeHeats, x => x.Id == heat.Id);
        if (index < 0) return 0m;
        if (index == gradeHeats.Length - 1)
        {
            var prior = gradeHeats.Take(index)
                .Sum(x => decimal.Round(x.PlannedQuantityMt / input * required, 4, MidpointRounding.AwayFromZero));
            return required - prior;
        }
        return decimal.Round(heat.PlannedQuantityMt / input * required, 4, MidpointRounding.AwayFromZero);
    }

    private static bool IsSchedulableCcm(Resource resource) =>
        resource.IsActive &&
        resource.ProcessUnitType == ProcessUnitType.Ccm &&
        resource.OperatingState is ResourceOperatingState.Available or ResourceOperatingState.CapacityDerated or ResourceOperatingState.QualityRestricted;
    private static bool Matches(string? configured, string? actual) => string.IsNullOrWhiteSpace(configured) || Same(configured, actual);
    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private sealed record LogicalSequenceState(
        CastSequence Sequence,
        HashSet<Guid> EligibleCasterIds,
        string LastGradeCode);
}
