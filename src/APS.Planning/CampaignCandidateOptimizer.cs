using APS.Application;

namespace APS.Planning;

/// <summary>
/// One requirement entering campaign composition: how much of a Production Order still has to be
/// rolled, and the service obligation that quantity carries.
/// </summary>
internal sealed record CampaignRequirement(
    Guid ProductionOrderId,
    string ProductionOrderNumber,
    decimal QuantityMt,
    DateTime RequiredDate,
    int Priority,
    bool IsMakeToOrder);

/// <summary>One candidate campaign: which requirements it groups and how much of each.</summary>
internal sealed record CampaignCandidate(IReadOnlyList<CampaignRequirementSlice> Slices)
{
    public decimal QuantityMt => Slices.Sum(x => x.QuantityMt);

    /// <summary>
    /// The date the campaign must be finished by: the earliest obligation in it. Grouping a later
    /// order into this campaign therefore pulls it forward to here - which is precisely the cost the
    /// optimizer has to weigh rather than ignore.
    /// </summary>
    public DateTime RequiredDate => Slices.Min(x => x.Requirement.RequiredDate);
}

internal sealed record CampaignRequirementSlice(CampaignRequirement Requirement, decimal QuantityMt);

internal sealed record CampaignCompositionOption(
    IReadOnlyList<CampaignCandidate> Campaigns,
    CampaignObjectiveBreakdown Score);

/// <summary>
/// #15: chooses how the requirements in one compatible group are split into campaigns.
///
/// Compatibility answers "can these coexist?"; it does not answer "should they coexist in this
/// campaign now?". Filling each campaign to its maximum in due-date order answers the second question
/// by accident: a far-future order joins a campaign purely because tonnage was left over, is produced
/// weeks early, and the campaign's service date collapses to the earliest order in it.
///
/// So several candidate compositions are generated and scored against an explicit objective, and the
/// best is selected. Because selection is by score rather than by arrival order, permuting the input
/// produces the same answer.
/// </summary>
internal static class CampaignCandidateOptimizer
{
    /// <summary>
    /// Cohort widths, in days, used to generate candidates. Each groups orders whose required dates
    /// fall within that many days of the cohort's earliest, so narrow widths favour service and wide
    /// ones favour campaign efficiency. The widest is effectively "fill to capacity regardless of
    /// date", which is the behaviour this replaces - retained as a candidate because on a group of
    /// same-day orders it is genuinely the right answer.
    /// </summary>
    private static readonly int[] CohortWindowDays = [0, 1, 2, 3, 7, 14, 36500];

    public static CampaignCompositionOption Choose(
        IReadOnlyList<CampaignRequirement> requirements,
        CampaignPlanningPolicy policy,
        CampaignObjectiveWeights weights)
    {
        var live = requirements.Where(x => x.QuantityMt > 0m).ToArray();
        if (live.Length == 0)
        {
            return new CampaignCompositionOption(
                Array.Empty<CampaignCandidate>(),
                new CampaignObjectiveBreakdown("EMPTY", 0, 0m, 0m, 0m, 0m, 0m));
        }

        var options = CohortWindowDays
            .Select(windowDays => Evaluate(BuildCohorts(live, policy, windowDays), policy, weights, StrategyCode(windowDays)))
            .ToArray();

        var selected = options
            .OrderBy(x => x.Score.DominanceKey.Service)
            .ThenBy(x => x.Score.DominanceKey.Cost)
            // Deterministic tie-break: fewer campaigns, then the narrower service window, so an
            // unchanged input always yields an identical plan.
            .ThenBy(x => x.Score.CampaignCount)
            .ThenBy(x => x.Score.StrategyCode, StringComparer.Ordinal)
            .First();

        return selected with
        {
            Score = selected.Score
        };
    }

    public static IReadOnlyCollection<CampaignObjectiveBreakdown> Considered(
        IReadOnlyList<CampaignRequirement> requirements,
        CampaignPlanningPolicy policy,
        CampaignObjectiveWeights weights)
    {
        var live = requirements.Where(x => x.QuantityMt > 0m).ToArray();
        if (live.Length == 0) return Array.Empty<CampaignObjectiveBreakdown>();

        return CohortWindowDays
            .Select(windowDays => Evaluate(BuildCohorts(live, policy, windowDays), policy, weights, StrategyCode(windowDays)).Score)
            .ToArray();
    }

    private static string StrategyCode(int windowDays) =>
        windowDays >= 36500 ? "FILL_TO_CAPACITY" : $"SERVICE_WINDOW_{windowDays:00}D";

    /// <summary>
    /// Splits requirements into campaigns by walking them in required-date order and starting a new
    /// campaign whenever the next requirement falls outside the current cohort's service window or
    /// the campaign is full. A requirement larger than one campaign is split across consecutive ones,
    /// which is unavoidable and does not change its service obligation.
    /// </summary>
    private static IReadOnlyList<CampaignCandidate> BuildCohorts(
        IReadOnlyList<CampaignRequirement> requirements,
        CampaignPlanningPolicy policy,
        int windowDays)
    {
        // Ordering here is a construction device, not the decision: every candidate is built from the
        // same canonical order, and which candidate wins is settled by score.
        var ordered = requirements
            .OrderBy(x => x.RequiredDate)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.IsMakeToOrder ? 0 : 1)
            .ThenBy(x => x.ProductionOrderNumber, StringComparer.Ordinal)
            .ToArray();

        var maximum = Math.Max(policy.MaximumCampaignQuantityMt, 0.0001m);
        var campaigns = new List<CampaignCandidate>();
        var current = new List<CampaignRequirementSlice>();
        var currentQuantity = 0m;
        DateTime? cohortAnchor = null;

        void Flush()
        {
            if (current.Count == 0) return;
            campaigns.Add(new CampaignCandidate(current.ToArray()));
            current = [];
            currentQuantity = 0m;
            cohortAnchor = null;
        }

        foreach (var requirement in ordered)
        {
            var remaining = requirement.QuantityMt;

            if (cohortAnchor.HasValue &&
                (requirement.RequiredDate - cohortAnchor.Value).TotalDays > windowDays)
            {
                Flush();
            }

            while (remaining > 0m)
            {
                var capacity = maximum - currentQuantity;
                if (capacity <= 0m)
                {
                    Flush();
                    capacity = maximum;
                }

                cohortAnchor ??= requirement.RequiredDate;
                var slice = Math.Min(remaining, capacity);
                current.Add(new CampaignRequirementSlice(requirement, slice));
                currentQuantity += slice;
                remaining -= slice;
            }
        }

        Flush();
        return campaigns;
    }

    private static CampaignCompositionOption Evaluate(
        IReadOnlyList<CampaignCandidate> campaigns,
        CampaignPlanningPolicy policy,
        CampaignObjectiveWeights weights,
        string strategyCode)
    {
        var earlyProductionMtDays = 0m;
        var serviceRiskMtDays = 0m;
        var residualHeatMt = 0m;
        var belowMinimumShortfallMt = 0m;

        var heatSize = Math.Max(policy.NominalHeatSizeMt, 0.0001m);
        // TargetCampaignQuantityMt is the configured "a campaign this small is uneconomic" line.
        var minimumCampaign = Math.Max(0m, policy.TargetCampaignQuantityMt);

        foreach (var campaign in campaigns)
        {
            var campaignDate = campaign.RequiredDate;

            foreach (var slice in campaign.Slices)
            {
                var days = (decimal)(slice.Requirement.RequiredDate - campaignDate).TotalDays;
                if (days > 0m)
                {
                    // Produced ahead of when it was wanted, because it shares a campaign with something
                    // due earlier. Weighted by priority so pulling an urgent order early costs more.
                    earlyProductionMtDays += slice.QuantityMt * days * PriorityFactor(slice.Requirement);
                }
            }

            var heats = Math.Ceiling(campaign.QuantityMt / heatSize);
            residualHeatMt += Math.Max(0m, heats * heatSize - campaign.QuantityMt);

            if (campaign.QuantityMt < minimumCampaign)
            {
                belowMinimumShortfallMt += minimumCampaign - campaign.QuantityMt;
            }
        }

        // Campaigns run in sequence, so a requirement sitting in a later campaign is served later.
        // Anything whose obligation falls before the campaign ahead of it is at service risk - this is
        // what stops narrow service windows from being chosen when they push work past its due date.
        var sequenced = campaigns.OrderBy(x => x.RequiredDate).ToArray();
        for (var i = 1; i < sequenced.Length; i++)
        {
            foreach (var slice in sequenced[i].Slices)
            {
                var predecessorDate = sequenced[i - 1].RequiredDate;
                var days = (decimal)(predecessorDate - slice.Requirement.RequiredDate).TotalDays;
                if (days > 0m)
                {
                    serviceRiskMtDays += slice.QuantityMt * days * PriorityFactor(slice.Requirement);
                }
            }
        }

        var totalCost =
            serviceRiskMtDays * weights.ServiceRiskPerMtDay +
            earlyProductionMtDays * weights.EarlyProductionPerMtDay +
            campaigns.Count * weights.CampaignSetupCost +
            residualHeatMt * weights.ResidualHeatPerMt +
            belowMinimumShortfallMt * weights.BelowMinimumCampaignPerMt;

        return new CampaignCompositionOption(
            campaigns,
            new CampaignObjectiveBreakdown(
                strategyCode,
                campaigns.Count,
                decimal.Round(serviceRiskMtDays, 4),
                decimal.Round(earlyProductionMtDays, 4),
                decimal.Round(residualHeatMt, 4),
                decimal.Round(belowMinimumShortfallMt, 4),
                decimal.Round(totalCost, 4)));
    }

    /// <summary>
    /// Make-to-order work carries a real customer promise; make-to-stock is a replenishment target.
    /// Priority raises the multiplier further, so an urgent order dominates a routine one of the same
    /// tonnage without needing a separate objective term.
    /// </summary>
    private static decimal PriorityFactor(CampaignRequirement requirement) =>
        (requirement.IsMakeToOrder ? 1m : 0.25m) * (1m + Math.Clamp(requirement.Priority, 0, 10) / 10m);
}
