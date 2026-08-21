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

    public CampaignTechnicalEvaluation Technical { get; init; } = CampaignTechnicalEvaluation.Neutral;
}

internal sealed record CampaignRequirementSlice(CampaignRequirement Requirement, decimal QuantityMt);

internal sealed record CampaignCompositionOption(
    IReadOnlyList<CampaignCandidate> Campaigns,
    CampaignObjectiveBreakdown Score);

/// <summary>
/// #15 campaign composition optimizer.
///
/// The original implementation only compared a fixed set of service-window cohorts. Those remain as
/// inspectable reference alternatives, but the authoritative candidate is now a dynamic partition over
/// every legal boundary in canonical due-date order. Each prospective campaign is technically evaluated
/// before it can enter the partition: furnace heat envelopes, route/resource availability and effective
/// grade-transition rules can therefore reject or price a composition before campaign identity is fixed.
/// </summary>
internal static class CampaignCandidateOptimizer
{
    private static readonly int[] ReferenceCohortWindowDays = [0, 1, 2, 3, 7, 14, 36500];

    public static CampaignCompositionOption Choose(
        IReadOnlyList<CampaignRequirement> requirements,
        CampaignPlanningPolicy policy,
        CampaignObjectiveWeights weights,
        Func<CampaignCandidate, CampaignTechnicalEvaluation>? technicalEvaluator = null)
    {
        var live = requirements.Where(x => x.QuantityMt > 0m).ToArray();
        if (live.Length == 0)
        {
            return new CampaignCompositionOption(
                Array.Empty<CampaignCandidate>(),
                new CampaignObjectiveBreakdown("EMPTY", 0, 0m, 0m, 0m, 0m, 0m));
        }

        var options = BuildOptions(live, policy, weights, technicalEvaluator).ToArray();
        var feasible = options.Where(x => x.Score.IsTechnicallyFeasible).ToArray();
        if (feasible.Length == 0)
        {
            var reasons = options
                .Select(x => x.Score.TechnicalReason)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            throw new InvalidOperationException(
                "No technically feasible campaign composition exists for this compatibility group. " +
                string.Join(" | ", reasons));
        }

        return feasible
            .OrderBy(x => x.Score.DominanceKey.Service)
            .ThenBy(x => x.Score.DominanceKey.Cost)
            .ThenBy(x => x.Score.CampaignCount)
            .ThenBy(x => x.Score.StrategyCode, StringComparer.Ordinal)
            .First();
    }

    public static IReadOnlyCollection<CampaignObjectiveBreakdown> Considered(
        IReadOnlyList<CampaignRequirement> requirements,
        CampaignPlanningPolicy policy,
        CampaignObjectiveWeights weights,
        Func<CampaignCandidate, CampaignTechnicalEvaluation>? technicalEvaluator = null)
    {
        var live = requirements.Where(x => x.QuantityMt > 0m).ToArray();
        if (live.Length == 0) return Array.Empty<CampaignObjectiveBreakdown>();
        return BuildOptions(live, policy, weights, technicalEvaluator)
            .Select(x => x.Score)
            .GroupBy(x => x.StrategyCode, StringComparer.Ordinal)
            .Select(x => x.First())
            .ToArray();
    }

    private static IEnumerable<CampaignCompositionOption> BuildOptions(
        IReadOnlyList<CampaignRequirement> requirements,
        CampaignPlanningPolicy policy,
        CampaignObjectiveWeights weights,
        Func<CampaignCandidate, CampaignTechnicalEvaluation>? technicalEvaluator)
    {
        // Dynamic partition is authoritative: unlike fixed day buckets it evaluates every legal
        // boundary over the canonical service order and can react to technical feasibility/cost.
        yield return BuildDynamicPartition(requirements, policy, weights, technicalEvaluator);

        // Retain the old cohort families as explainability/reference candidates. They are useful to
        // show how much the selected partition gained over familiar service-window/fill behavior.
        foreach (var windowDays in ReferenceCohortWindowDays)
        {
            yield return Evaluate(
                BuildCohorts(requirements, policy, windowDays),
                policy,
                weights,
                StrategyCode(windowDays),
                technicalEvaluator);
        }
    }

    private static CampaignCompositionOption BuildDynamicPartition(
        IReadOnlyList<CampaignRequirement> requirements,
        CampaignPlanningPolicy policy,
        CampaignObjectiveWeights weights,
        Func<CampaignCandidate, CampaignTechnicalEvaluation>? technicalEvaluator)
    {
        var atoms = BuildAtoms(requirements, policy).ToArray();
        var states = new DynamicState?[atoms.Length + 1];
        states[0] = new DynamicState(Array.Empty<CampaignCandidate>(), 0m, 0m, 0m);
        var maximum = Math.Max(policy.MaximumCampaignQuantityMt, 0.0001m);

        for (var end = 1; end <= atoms.Length; end++)
        {
            decimal quantity = 0m;
            for (var start = end - 1; start >= 0; start--)
            {
                quantity += atoms[start].QuantityMt;
                if (quantity > maximum + 0.0001m) break;
                if (states[start] is null) continue;

                var candidate = new CampaignCandidate(atoms[start..end]);
                var one = Evaluate(
                    new[] { candidate },
                    policy,
                    weights,
                    "DYNAMIC_SEGMENT",
                    technicalEvaluator);
                if (!one.Score.IsTechnicallyFeasible) continue;

                var predecessor = states[start]!;
                var state = new DynamicState(
                    predecessor.Campaigns.Concat(one.Campaigns).ToArray(),
                    predecessor.ServiceRisk + one.Score.ServiceRiskMtDays,
                    predecessor.Cost + one.Score.TotalCost,
                    predecessor.EarlyProduction + one.Score.EarlyProductionMtDays);

                if (states[end] is null || Better(state, states[end]!)) states[end] = state;
            }
        }

        if (states[^1] is null)
        {
            // Return an inspectable rejected alternative; Choose() will report the technical reasons
            // from the reference candidates if every dynamic segment is infeasible too.
            return new CampaignCompositionOption(
                Array.Empty<CampaignCandidate>(),
                new CampaignObjectiveBreakdown("DYNAMIC_PARTITION", 0, 0m, 0m, 0m, 0m, decimal.MaxValue / 1000m)
                {
                    IsTechnicallyFeasible = false,
                    TechnicalReason = "No sequence of technically feasible campaign segments covers all requirements."
                });
        }

        return Evaluate(states[^1]!.Campaigns, policy, weights, "DYNAMIC_PARTITION", technicalEvaluator);
    }

    private static bool Better(DynamicState candidate, DynamicState current)
    {
        var byService = candidate.ServiceRisk.CompareTo(current.ServiceRisk);
        if (byService != 0) return byService < 0;
        var byCost = candidate.Cost.CompareTo(current.Cost);
        if (byCost != 0) return byCost < 0;
        var byCount = candidate.Campaigns.Count.CompareTo(current.Campaigns.Count);
        if (byCount != 0) return byCount < 0;
        return candidate.EarlyProduction < current.EarlyProduction;
    }

    private static IReadOnlyList<CampaignRequirementSlice> BuildAtoms(
        IReadOnlyList<CampaignRequirement> requirements,
        CampaignPlanningPolicy policy)
    {
        var maximum = Math.Max(policy.MaximumCampaignQuantityMt, 0.0001m);
        var result = new List<CampaignRequirementSlice>();
        foreach (var requirement in CanonicalOrder(requirements))
        {
            var remaining = requirement.QuantityMt;
            while (remaining > 0m)
            {
                var quantity = Math.Min(remaining, maximum);
                result.Add(new CampaignRequirementSlice(requirement, quantity));
                remaining -= quantity;
            }
        }
        return result;
    }

    private static string StrategyCode(int windowDays) =>
        windowDays >= 36500 ? "FILL_TO_CAPACITY" : $"SERVICE_WINDOW_{windowDays:00}D";

    private static IReadOnlyList<CampaignCandidate> BuildCohorts(
        IReadOnlyList<CampaignRequirement> requirements,
        CampaignPlanningPolicy policy,
        int windowDays)
    {
        var ordered = CanonicalOrder(requirements);
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
                Flush();

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
        string strategyCode,
        Func<CampaignCandidate, CampaignTechnicalEvaluation>? technicalEvaluator)
    {
        var earlyProductionMtDays = 0m;
        var serviceRiskMtDays = 0m;
        var residualHeatMt = 0m;
        var belowMinimumShortfallMt = 0m;
        var transitionCost = 0m;
        var heatTargetDeviation = 0m;
        var evaluated = new List<CampaignCandidate>(campaigns.Count);
        var gradeSequence = new List<string>();
        string? technicalReason = null;

        var heatSize = Math.Max(policy.NominalHeatSizeMt, 0.0001m);
        var minimumCampaign = Math.Max(0m, policy.TargetCampaignQuantityMt);

        foreach (var rawCampaign in campaigns)
        {
            var technical = technicalEvaluator?.Invoke(rawCampaign) ?? CampaignTechnicalEvaluation.Neutral;
            var campaign = rawCampaign with { Technical = technical };
            evaluated.Add(campaign);
            if (!technical.IsFeasible)
            {
                technicalReason ??= technical.Reason;
                continue;
            }

            transitionCost += technical.GradeTransitionCost;
            heatTargetDeviation += technical.HeatTargetDeviationMt;
            gradeSequence.AddRange(technical.GradeSequence);

            var campaignDate = campaign.RequiredDate;
            foreach (var slice in campaign.Slices)
            {
                var days = (decimal)(slice.Requirement.RequiredDate - campaignDate).TotalDays;
                if (days > 0m)
                    earlyProductionMtDays += slice.QuantityMt * days * PriorityFactor(slice.Requirement);
            }

            if (technicalEvaluator is null)
            {
                var heats = Math.Ceiling(campaign.QuantityMt / heatSize);
                residualHeatMt += Math.Max(0m, heats * heatSize - campaign.QuantityMt);
            }
            else
            {
                // Physical heat fitting supersedes the nominal-heat residual approximation when the
                // technical evaluator has actual furnace envelopes.
                residualHeatMt += technical.HeatTargetDeviationMt;
            }

            if (campaign.QuantityMt < minimumCampaign)
                belowMinimumShortfallMt += minimumCampaign - campaign.QuantityMt;
        }

        var technicallyFeasible = technicalReason is null;
        if (technicallyFeasible)
        {
            var sequenced = evaluated.OrderBy(x => x.RequiredDate).ToArray();
            for (var i = 1; i < sequenced.Length; i++)
            {
                foreach (var slice in sequenced[i].Slices)
                {
                    var predecessorDate = sequenced[i - 1].RequiredDate;
                    var days = (decimal)(predecessorDate - slice.Requirement.RequiredDate).TotalDays;
                    if (days > 0m)
                        serviceRiskMtDays += slice.QuantityMt * days * PriorityFactor(slice.Requirement);
                }
            }
        }

        var totalCost = technicallyFeasible
            ? serviceRiskMtDays * weights.ServiceRiskPerMtDay +
              earlyProductionMtDays * weights.EarlyProductionPerMtDay +
              evaluated.Count * weights.CampaignSetupCost +
              residualHeatMt * weights.ResidualHeatPerMt +
              belowMinimumShortfallMt * weights.BelowMinimumCampaignPerMt +
              transitionCost * weights.GradeTransitionCostWeight +
              heatTargetDeviation * weights.HeatTargetDeviationPerMt
            : decimal.MaxValue / 1000m;

        return new CampaignCompositionOption(
            evaluated,
            new CampaignObjectiveBreakdown(
                strategyCode,
                evaluated.Count,
                decimal.Round(serviceRiskMtDays, 4),
                decimal.Round(earlyProductionMtDays, 4),
                decimal.Round(residualHeatMt, 4),
                decimal.Round(belowMinimumShortfallMt, 4),
                decimal.Round(totalCost, 4))
            {
                GradeTransitionCost = decimal.Round(transitionCost, 4),
                HeatTargetDeviationMt = decimal.Round(heatTargetDeviation, 4),
                IsTechnicallyFeasible = technicallyFeasible,
                TechnicalReason = technicalReason,
                GradeSequence = gradeSequence.ToArray()
            });
    }

    private static CampaignRequirement[] CanonicalOrder(IReadOnlyList<CampaignRequirement> requirements) =>
        requirements
            .OrderBy(x => x.RequiredDate)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.IsMakeToOrder ? 0 : 1)
            .ThenBy(x => x.ProductionOrderNumber, StringComparer.Ordinal)
            .ToArray();

    private static decimal PriorityFactor(CampaignRequirement requirement) =>
        (requirement.IsMakeToOrder ? 1m : 0.25m) * (1m + Math.Clamp(requirement.Priority, 0, 10) / 10m);

    private sealed record DynamicState(
        IReadOnlyList<CampaignCandidate> Campaigns,
        decimal ServiceRisk,
        decimal Cost,
        decimal EarlyProduction);
}
