using APS.Application;
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
            assumptionChanges);
    }

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
