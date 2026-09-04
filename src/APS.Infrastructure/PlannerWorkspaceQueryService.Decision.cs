using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed partial class PlannerWorkspaceQueryService
{
    public async Task<PlanComparisonWorkspaceView?> GetPlanComparisonAsync(
        Guid baselinePlanVersionId,
        Guid newPlanVersionId,
        CancellationToken cancellationToken = default)
    {
        var baseline = await BuildPlanContextAsync(baselinePlanVersionId, cancellationToken);
        var next = await BuildPlanContextAsync(newPlanVersionId, cancellationToken);
        if (baseline is null || next is null) return null;

        var difference = await new PlanComparisonService(db)
            .CompareAsync(baselinePlanVersionId, newPlanVersionId, cancellationToken);

        var resourceIds = difference.Operations
            .SelectMany(x => new[] { x.BaselineResourceId, x.NewResourceId })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        var resources = resourceIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.Resources.AsNoTracking()
                .Where(x => resourceIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);

        var changes = difference.Operations
            .OrderByDescending(x => x.ChangeType != PlanOperationChangeType.Unchanged)
            .ThenByDescending(x => Math.Abs(x.StartMovementMinutes))
            .ThenBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => new PlanOperationChangeView(
                x.PlanningKey,
                x.TaskType,
                x.ChangeType,
                ResourceCode(x.BaselineResourceId, resources),
                ResourceCode(x.NewResourceId, resources),
                x.BaselineStartUtc,
                x.NewStartUtc,
                x.BaselineEndUtc,
                x.NewEndUtc,
                x.StartMovementMinutes))
            .ToArray();

        var baselineOperations = difference.Operations
            .Where(x => x.BaselineResourceId.HasValue && x.BaselineStartUtc.HasValue && x.BaselineEndUtc.HasValue)
            .Select(x => new PlanScenarioOperationView(
                x.PlanningKey,
                x.TaskType,
                ResourceCode(x.BaselineResourceId, resources) ?? "Unassigned",
                x.BaselineStartUtc!.Value,
                x.BaselineEndUtc!.Value,
                x.ChangeType))
            .OrderBy(x => x.StartUtc)
            .ThenBy(x => x.ResourceCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var newOperations = difference.Operations
            .Where(x => x.NewResourceId.HasValue && x.NewStartUtc.HasValue && x.NewEndUtc.HasValue)
            .Select(x => new PlanScenarioOperationView(
                x.PlanningKey,
                x.TaskType,
                ResourceCode(x.NewResourceId, resources) ?? "Unassigned",
                x.NewStartUtc!.Value,
                x.NewEndUtc!.Value,
                x.ChangeType))
            .OrderBy(x => x.StartUtc)
            .ThenBy(x => x.ResourceCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var baselineAssumptions = await GetPlanningAssumptionsAsync(baselinePlanVersionId, cancellationToken);
        var newAssumptions = await GetPlanningAssumptionsAsync(newPlanVersionId, cancellationToken);
        var assumptionChanges = BuildAssumptionChanges(baselineAssumptions, newAssumptions);

        return new PlanComparisonWorkspaceView(
            baseline,
            next,
            difference.AddedCount,
            difference.RemovedCount,
            difference.MovedCount,
            difference.ResourceChangedCount,
            difference.UnchangedCount,
            difference.MaximumStartMovementMinutes,
            changes,
            assumptionChanges,
            Summary(baselineOperations, baseline.ObjectiveValue),
            Summary(newOperations, next.ObjectiveValue),
            ResourceLoads(baselineOperations, newOperations),
            baselineOperations,
            newOperations);
    }

    private static PlanScenarioSummaryView Summary(
        IReadOnlyCollection<PlanScenarioOperationView> operations,
        long? objectiveValue)
    {
        if (operations.Count == 0)
            return new PlanScenarioSummaryView(0, 0, 0, null, null, 0, objectiveValue);

        var first = operations.Min(x => x.StartUtc);
        var last = operations.Max(x => x.EndUtc);
        return new PlanScenarioSummaryView(
            operations.Count,
            operations.Select(x => x.ResourceCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            operations.Sum(x => Math.Max(0, (x.EndUtc - x.StartUtc).TotalHours)),
            first,
            last,
            Math.Max(0, (last - first).TotalHours),
            objectiveValue);
    }

    private static IReadOnlyCollection<PlanResourceLoadComparisonView> ResourceLoads(
        IReadOnlyCollection<PlanScenarioOperationView> baseline,
        IReadOnlyCollection<PlanScenarioOperationView> next)
    {
        var left = LoadByResource(baseline);
        var right = LoadByResource(next);
        return left.Keys
            .Union(right.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(code => new PlanResourceLoadComparisonView(
                code,
                left.TryGetValue(code, out var baselineLoad) ? baselineLoad.Count : 0,
                right.TryGetValue(code, out var newLoad) ? newLoad.Count : 0,
                left.TryGetValue(code, out baselineLoad) ? baselineLoad.Hours : 0,
                right.TryGetValue(code, out newLoad) ? newLoad.Hours : 0))
            .ToArray();
    }

    private static Dictionary<string, (int Count, double Hours)> LoadByResource(
        IEnumerable<PlanScenarioOperationView> operations) =>
        operations
            .GroupBy(x => x.ResourceCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => (x.Count(), x.Sum(operation => Math.Max(0, (operation.EndUtc - operation.StartUtc).TotalHours))),
                StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyCollection<PlanAssumptionChangeView> BuildAssumptionChanges(
        PlanningAssumptions? baseline,
        PlanningAssumptions? next)
    {
        if (baseline is null && next is null) return Array.Empty<PlanAssumptionChangeView>();

        var result = new List<PlanAssumptionChangeView>();
        Add(result, "Plant", "Operating scenario", baseline?.ScenarioCode ?? "Configured baseline", next?.ScenarioCode ?? "Configured baseline");

        if (baseline?.CampaignPolicy is { } leftCampaign && next?.CampaignPolicy is { } rightCampaign)
        {
            Add(result, "Campaign", "Minimum heat MT", F(leftCampaign.MinimumHeatSizeMt), F(rightCampaign.MinimumHeatSizeMt));
            Add(result, "Campaign", "Nominal heat MT", F(leftCampaign.NominalHeatSizeMt), F(rightCampaign.NominalHeatSizeMt));
            Add(result, "Campaign", "Maximum heat MT", F(leftCampaign.MaximumHeatSizeMt), F(rightCampaign.MaximumHeatSizeMt));
            Add(result, "Campaign", "Target campaign MT", F(leftCampaign.TargetCampaignQuantityMt), F(rightCampaign.TargetCampaignQuantityMt));
            Add(result, "Campaign", "Maximum campaign MT", F(leftCampaign.MaximumCampaignQuantityMt), F(rightCampaign.MaximumCampaignQuantityMt));
            Add(result, "Campaign", "MTO/MTS mixing", YesNo(leftCampaign.AllowMtoMtsMixing), YesNo(rightCampaign.AllowMtoMtsMixing));
            Add(result, "Campaign", "Mixed grades in sequence class", YesNo(leftCampaign.AllowMixedGradesWithinSequenceClass), YesNo(rightCampaign.AllowMixedGradesWithinSequenceClass));
            Add(result, "Campaign", "Expected casting yield %", F(leftCampaign.ExpectedCastingYieldPct), F(rightCampaign.ExpectedCastingYieldPct));
        }
        else if (baseline?.CampaignPolicy is not null || next?.CampaignPolicy is not null)
        {
            Add(result, "Campaign", "Control snapshot", baseline?.CampaignPolicy is null ? "Legacy / not captured" : "Captured", next?.CampaignPolicy is null ? "Legacy / not captured" : "Captured");
        }

        var leftWeights = baseline?.CampaignObjectiveWeights;
        var rightWeights = next?.CampaignObjectiveWeights;
        if (leftWeights is not null && rightWeights is not null)
        {
            Add(result, "Objectives", "Late service / MT-day", F(leftWeights.ServiceRiskPerMtDay), F(rightWeights.ServiceRiskPerMtDay));
            Add(result, "Objectives", "Early production / MT-day", F(leftWeights.EarlyProductionPerMtDay), F(rightWeights.EarlyProductionPerMtDay));
            Add(result, "Objectives", "Campaign setup cost", F(leftWeights.CampaignSetupCost), F(rightWeights.CampaignSetupCost));
            Add(result, "Objectives", "Residual heat / MT", F(leftWeights.ResidualHeatPerMt), F(rightWeights.ResidualHeatPerMt));
            Add(result, "Objectives", "Below-min campaign / MT", F(leftWeights.BelowMinimumCampaignPerMt), F(rightWeights.BelowMinimumCampaignPerMt));
            Add(result, "Objectives", "Grade transition weight", F(leftWeights.GradeTransitionCostWeight), F(rightWeights.GradeTransitionCostWeight));
            Add(result, "Objectives", "Heat target deviation / MT", F(leftWeights.HeatTargetDeviationPerMt), F(rightWeights.HeatTargetDeviationPerMt));
            Add(result, "Objectives", "Campaign stability / MT", F(leftWeights.CampaignStabilityChangePerMt), F(rightWeights.CampaignStabilityChangePerMt));
        }

        if (baseline?.StructurePolicy is { } leftStructure && next?.StructurePolicy is { } rightStructure)
        {
            Add(result, "Structure", "Max heats / cast sequence", leftStructure.MaximumHeatsPerCastSequence.ToString(), rightStructure.MaximumHeatsPerCastSequence.ToString());
            Add(result, "Structure", "Casting min / heat", leftStructure.DefaultCastingMinutesPerHeat.ToString(), rightStructure.DefaultCastingMinutesPerHeat.ToString());
            Add(result, "Structure", "Sequence break penalty", leftStructure.SequenceBreakPenalty.ToString(), rightStructure.SequenceBreakPenalty.ToString());
            Add(result, "Structure", "Casting yield %", F(leftStructure.CastingYieldPct), F(rightStructure.CastingYieldPct));
            Add(result, "Structure", "Rolling min / 100 MT", leftStructure.DefaultRollingMinutesPer100Mt.ToString(), rightStructure.DefaultRollingMinutesPer100Mt.ToString());
            Add(result, "Structure", "Cross-campaign cast sequences", YesNo(leftStructure.AllowCrossCampaignCastSequences), YesNo(rightStructure.AllowCrossCampaignCastSequences));
            Add(result, "Structure", "Cross-campaign rolling plans", YesNo(leftStructure.AllowCrossCampaignRollingPlans), YesNo(rightStructure.AllowCrossCampaignRollingPlans));
        }

        if (baseline?.TimeFencePolicy is { } leftFence && next?.TimeFencePolicy is { } rightFence)
        {
            Add(result, "Stability", "Frozen minutes", leftFence.FrozenMinutes.ToString(), rightFence.FrozenMinutes.ToString());
            Add(result, "Stability", "Slushy minutes", leftFence.SlushyMinutes.ToString(), rightFence.SlushyMinutes.ToString());
            Add(result, "Stability", "Movement penalty / minute", leftFence.SlushyMovementPenaltyPerMinute.ToString(), rightFence.SlushyMovementPenaltyPerMinute.ToString());
            Add(result, "Stability", "Resource-change penalty", leftFence.SlushyResourceChangePenalty.ToString(), rightFence.SlushyResourceChangePenalty.ToString());
        }

        Add(result, "Solver", "Maximum solve seconds", baseline?.MaxSolverSeconds?.ToString() ?? "Legacy / not captured", next?.MaxSolverSeconds?.ToString() ?? "Legacy / not captured");
        Add(result, "Dispatch", "Commitment policies", AssignmentSummary(baseline?.AssignmentPolicies), AssignmentSummary(next?.AssignmentPolicies));

        var leftResources = (baseline?.ResourceScheduling ?? Array.Empty<ResourceSchedulingAssumption>())
            .ToDictionary(x => x.ResourceCode, StringComparer.OrdinalIgnoreCase);
        var rightResources = (next?.ResourceScheduling ?? Array.Empty<ResourceSchedulingAssumption>())
            .ToDictionary(x => x.ResourceCode, StringComparer.OrdinalIgnoreCase);
        foreach (var code in leftResources.Keys.Union(rightResources.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            leftResources.TryGetValue(code, out var left);
            rightResources.TryGetValue(code, out var right);
            var leftValue = left is null ? "Not present" : $"{left.OperatingState ?? ResourceOperatingState.Available} · {F(left.CapacityFactorPct)}%";
            var rightValue = right is null ? "Not present" : $"{right.OperatingState ?? ResourceOperatingState.Available} · {F(right.CapacityFactorPct)}%";
            Add(result, "Resource scenario", code, leftValue, rightValue);
        }

        return result;
    }

    private static void Add(List<PlanAssumptionChangeView> result, string area, string setting, string baseline, string next)
    {
        if (!string.Equals(baseline, next, StringComparison.OrdinalIgnoreCase))
            result.Add(new PlanAssumptionChangeView(area, setting, baseline, next));
    }

    private static string AssignmentSummary(IReadOnlyCollection<OperationAssignmentPolicy>? policies) =>
        policies is { Count: > 0 } ? $"{policies.Count} operation policy(s)" : "Default flexible";
    private static string YesNo(bool value) => value ? "Yes" : "No";
    private static string F(decimal value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    private static string? ResourceCode(Guid? resourceId, IReadOnlyDictionary<Guid, string> resources) =>
        resourceId.HasValue
            ? resources.TryGetValue(resourceId.Value, out var code) ? code : resourceId.Value.ToString("N")[..8]
            : null;
}
