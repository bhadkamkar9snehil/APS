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
///
/// Replanning also contributes a baseline-preserving candidate and an explicit quantity movement cost.
/// That makes campaign stability a real objective rather than assuming downstream time-fence stability
/// somehow preserves upstream PO grouping. Hard feasibility and service remain dominant.
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
        yield return BuildDynamicPartition(requirements, policy, weights, technicalEvaluator);

        if (policy.BaselineCampaignAllocations is { Count: > 0 })
        {
            var baseline = BuildBaselineComposition(requirements, policy);
            if (baseline.Count > 0)
            {
                yield return Evaluate(
                    baseline,
                    policy,
                    weights,
                    "BASELINE_STABILITY",
                    technicalEvaluator);
            }
        }

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
                // Baseline matching is a composition-level one-to-one comparison; applying it to an
                // isolated DP segment would let several segments all claim the same old campaign.
                var one = Evaluate(
                    new[] { candidate },
                    policy,
                    weights,
                    "DYNAMIC_SEGMENT",
                    technicalEvaluator,
                    includeStability: false);
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

    private static IReadOnlyList<CampaignCandidate> BuildBaselineComposition(
        IReadOnlyList<CampaignRequirement> requirements,
        CampaignPlanningPolicy policy)
    {
        var baseline = policy.BaselineCampaignAllocations ?? Array.Empty<BaselineCampaignAllocation>();
        var requirementById = requirements.ToDictionary(x => x.ProductionOrderId);
        var remaining = requirements.ToDictionary(x => x.ProductionOrderId, x => x.QuantityMt);
        var maximum = Math.Max(policy.MaximumCampaignQuantityMt, 0.0001m);
        var result = new List<CampaignCandidate>();

        var baselineGroups = baseline
            .Where(x => x.PlannedQuantityMt > 0m && requirementById.ContainsKey(x.ProductionOrderId))
            .GroupBy(x => x.CampaignId)
            .Select(group => new
            {
                CampaignId = group.Key,
                Allocations = group
                    .GroupBy(x => x.ProductionOrderId)
                    .Select(x => new BaselineCampaignAllocation(group.Key, x.Key, x.Sum(y => y.PlannedQuantityMt)))
                    .ToArray()
            })
            .OrderBy(x => x.Allocations
                .Select(a => requirementById[a.ProductionOrderId].RequiredDate)
                .DefaultIfEmpty(DateTime.MaxValue)
                .Min())
            .ThenBy(x => x.CampaignId)
            .ToArray();

        foreach (var group in baselineGroups)
        {
            var slices = new List<CampaignRequirementSlice>();
            foreach (var allocation in group.Allocations
                         .OrderBy(x => requirementById[x.ProductionOrderId].RequiredDate)
                         .ThenByDescending(x => requirementById[x.ProductionOrderId].Priority)
                         .ThenBy(x => requirementById[x.ProductionOrderId].ProductionOrderNumber, StringComparer.Ordinal))
            {
                var requirement = requirementById[allocation.ProductionOrderId];
                var quantity = Math.Min(remaining[requirement.ProductionOrderId], allocation.PlannedQuantityMt);
                if (quantity <= 0m) continue;
                slices.Add(new CampaignRequirementSlice(requirement, quantity));
                remaining[requirement.ProductionOrderId] -= quantity;
            }
            PackSlices(slices, maximum, result);
        }

        var residual = new List<CampaignRequirementSlice>();
        foreach (var requirement in CanonicalOrder(requirements))
        {
            var quantity = remaining[requirement.ProductionOrderId];
            if (quantity > 0m) residual.Add(new CampaignRequirementSlice(requirement, quantity));
        }
        PackSlices(residual, maximum, result);
        return result;
    }

    private static void PackSlices(
        IReadOnlyCollection<CampaignRequirementSlice> slices,
        decimal maximum,
        ICollection<CampaignCandidate> result)
    {
        var current = new List<CampaignRequirementSlice>();
        var currentQuantity = 0m;

        void Flush()
        {
            if (current.Count == 0) return;
            result.Add(new CampaignCandidate(current.ToArray()));
            current = [];
            currentQuantity = 0m;
        }

        foreach (var source in slices)
        {
            var remaining = source.QuantityMt;
            while (remaining > 0m)
            {
                var capacity = maximum - currentQuantity;
                if (capacity <= 0m)
                {
                    Flush();
                    capacity = maximum;
                }

                var quantity = Math.Min(remaining, capacity);
                current.Add(new CampaignRequirementSlice(source.Requirement, quantity));
                currentQuantity += quantity;
                remaining -= quantity;
            }
        }
        Flush();
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
        Func<CampaignCandidate, CampaignTechnicalEvaluation>? technicalEvaluator,
        bool includeStability = true)
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
            if (technical.HasFurnaceEvaluation)
                heatTargetDeviation += technical.HeatTargetDeviationMt;
            gradeSequence.AddRange(technical.GradeSequence);

            var campaignDate = campaign.RequiredDate;
            foreach (var slice in campaign.Slices)
            {
                var days = (decimal)(slice.Requirement.RequiredDate - campaignDate).TotalDays;
                if (days > 0m)
                    earlyProductionMtDays += slice.QuantityMt * days * PriorityFactor(slice.Requirement);
            }

            if (!technical.HasFurnaceEvaluation)
            {
                var heats = Math.Ceiling(campaign.QuantityMt / heatSize);
                residualHeatMt += Math.Max(0m, heats * heatSize - campaign.QuantityMt);
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

        var stabilityChangedMt = includeStability && technicallyFeasible
            ? CampaignStabilityChangedQuantity(evaluated, policy.BaselineCampaignAllocations)
            : 0m;

        var totalCost = technicallyFeasible
            ? serviceRiskMtDays * weights.ServiceRiskPerMtDay +
              earlyProductionMtDays * weights.EarlyProductionPerMtDay +
              evaluated.Count * weights.CampaignSetupCost +
              residualHeatMt * weights.ResidualHeatPerMt +
              belowMinimumShortfallMt * weights.BelowMinimumCampaignPerMt +
              transitionCost * weights.GradeTransitionCostWeight +
              heatTargetDeviation * weights.HeatTargetDeviationPerMt +
              stabilityChangedMt * weights.CampaignStabilityChangePerMt
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
                CampaignStabilityChangedMt = decimal.Round(stabilityChangedMt, 4),
                IsTechnicallyFeasible = technicallyFeasible,
                TechnicalReason = technicalReason,
                GradeSequence = gradeSequence.ToArray()
            });
    }

    private static decimal CampaignStabilityChangedQuantity(
        IReadOnlyList<CampaignCandidate> campaigns,
        IReadOnlyCollection<BaselineCampaignAllocation>? baselineAllocations)
    {
        if (baselineAllocations is not { Count: > 0 } || campaigns.Count == 0) return 0m;

        var currentOrderIds = campaigns
            .SelectMany(x => x.Slices)
            .Select(x => x.Requirement.ProductionOrderId)
            .ToHashSet();
        var baselineGroups = baselineAllocations
            .Where(x => x.PlannedQuantityMt > 0m && currentOrderIds.Contains(x.ProductionOrderId))
            .GroupBy(x => x.CampaignId)
            .Select(group => group
                .GroupBy(x => x.ProductionOrderId)
                .ToDictionary(x => x.Key, x => x.Sum(y => y.PlannedQuantityMt)))
            .Where(x => x.Count > 0)
            .ToArray();
        if (baselineGroups.Length == 0) return 0m;

        var currentGroups = campaigns
            .Select(campaign => campaign.Slices
                .GroupBy(x => x.Requirement.ProductionOrderId)
                .ToDictionary(x => x.Key, x => x.Sum(y => y.QuantityMt)))
            .ToArray();

        var currentTotals = currentGroups
            .SelectMany(x => x)
            .GroupBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Value));
        var baselineTotals = baselineGroups
            .SelectMany(x => x)
            .GroupBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Value));
        var comparable = currentTotals.Keys
            .Where(baselineTotals.ContainsKey)
            .Sum(id => Math.Min(currentTotals[id], baselineTotals[id]));
        if (comparable <= 0m) return 0m;

        var overlaps = new decimal[currentGroups.Length, baselineGroups.Length];
        for (var current = 0; current < currentGroups.Length; current++)
        for (var baseline = 0; baseline < baselineGroups.Length; baseline++)
        {
            overlaps[current, baseline] = currentGroups[current]
                .Where(x => baselineGroups[baseline].TryGetValue(x.Key, out _))
                .Sum(x => Math.Min(x.Value, baselineGroups[baseline][x.Key]));
        }

        var preserved = MaximumOneToOneOverlap(overlaps);
        return decimal.Round(Math.Max(0m, comparable - preserved), 4, MidpointRounding.AwayFromZero);
    }

    private static decimal MaximumOneToOneOverlap(decimal[,] source)
    {
        var rows = source.GetLength(0);
        var columns = source.GetLength(1);
        if (rows == 0 || columns == 0) return 0m;

        decimal[,] matrix;
        if (columns <= rows)
        {
            matrix = source;
        }
        else
        {
            matrix = new decimal[columns, rows];
            for (var r = 0; r < rows; r++)
            for (var c = 0; c < columns; c++)
                matrix[c, r] = source[r, c];
            (rows, columns) = (columns, rows);
        }

        // Exact matching is cheap for normal campaign counts. At very large cardinality use a
        // deterministic maximum-edge greedy fallback rather than letting a stability term dominate
        // campaign-planning runtime.
        if (columns <= 12)
        {
            var memo = new Dictionary<(int Row, int UsedMask), decimal>();
            return Solve(0, 0);

            decimal Solve(int row, int usedMask)
            {
                if (row >= rows) return 0m;
                if (memo.TryGetValue((row, usedMask), out var cached)) return cached;

                var best = Solve(row + 1, usedMask);
                for (var column = 0; column < columns; column++)
                {
                    var bit = 1 << column;
                    if ((usedMask & bit) != 0) continue;
                    best = Math.Max(best, matrix[row, column] + Solve(row + 1, usedMask | bit));
                }
                memo[(row, usedMask)] = best;
                return best;
            }
        }

        var edges = new List<(decimal Quantity, int Row, int Column)>();
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
            if (matrix[row, column] > 0m)
                edges.Add((matrix[row, column], row, column));

        var usedRows = new HashSet<int>();
        var usedColumns = new HashSet<int>();
        var total = 0m;
        foreach (var edge in edges
                     .OrderByDescending(x => x.Quantity)
                     .ThenBy(x => x.Row)
                     .ThenBy(x => x.Column))
        {
            if (usedRows.Contains(edge.Row) || usedColumns.Contains(edge.Column)) continue;
            usedRows.Add(edge.Row);
            usedColumns.Add(edge.Column);
            total += edge.Quantity;
        }
        return total;
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
