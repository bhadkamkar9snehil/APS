using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class MaterialPlanFinalizer
{
    public static MaterialPlanningResult Finalize(
        PlanningRunRequest request,
        ProductionStructurePlanningResult structure,
        MaterialPlanningResult materialPlan,
        FiniteScheduleResult schedule)
    {
        if (!schedule.IsFeasible) return materialPlan;

        var latestNeed = LatestMaterialNeedTimeProjector.Build(request, structure, schedule);
        foreach (var requirement in materialPlan.Requirements ?? Array.Empty<MaterialRequirement>())
        {
            if (latestNeed.TryGetValue(requirement.SourceEntityId, out var target))
                requirement.TargetRequiredAtUtc = target;

            if (requirement.Status is MaterialRequirementStatus.Shortfall or MaterialRequirementStatus.Unsourced)
                continue;

            if (requirement.ExpectedFullyAvailableAtUtc.HasValue &&
                requirement.TargetRequiredAtUtc.HasValue &&
                requirement.ExpectedFullyAvailableAtUtc.Value > requirement.TargetRequiredAtUtc.Value)
            {
                requirement.Status = MaterialRequirementStatus.LateSupply;
                requirement.Explanation =
                    $"Qualified supply is expected at {requirement.ExpectedFullyAvailableAtUtc:O}, after latest service-feasible need {requirement.TargetRequiredAtUtc:O}; APS can delay the operation but service will be late unless supply improves.";
            }
        }

        var actions = DeduplicateMakeActions(materialPlan.SupplyRequirements ?? Array.Empty<MaterialSupplyRequirement>()).ToArray();
        foreach (var action in actions)
        {
            ApplyCommercialLotSizing(action, request.MaterialSourcingRules);
            if (action.ActionType == MaterialSupplyActionType.Unsourced)
            {
                action.PlannedOrderQuantityMt = 0m;
                action.ExcessQuantityMt = 0m;
            }
            else if (action.PlannedOrderQuantityMt <= 0m)
            {
                action.PlannedOrderQuantityMt = action.QuantityMt;
                action.ExcessQuantityMt = 0m;
            }
        }

        return materialPlan with { SupplyRequirements = actions };
    }

    private static IEnumerable<MaterialSupplyRequirement> DeduplicateMakeActions(
        IReadOnlyCollection<MaterialSupplyRequirement> actions)
    {
        // Keep heat-granular MAKE actions whenever they exist. Older/source-selection aggregate MAKE
        // actions have no UpstreamHeatId and would otherwise double-report the same internal production.
        var heatMakePoIds = actions
            .Where(x => x.ActionType == MaterialSupplyActionType.Make && x.UpstreamHeatId.HasValue && x.ProductionOrderId.HasValue)
            .Select(x => x.ProductionOrderId!.Value)
            .ToHashSet();

        foreach (var action in actions)
        {
            if (action.ActionType == MaterialSupplyActionType.Make &&
                !action.UpstreamHeatId.HasValue &&
                action.ProductionOrderId.HasValue &&
                heatMakePoIds.Contains(action.ProductionOrderId.Value))
                continue;
            yield return action;
        }
    }

    private static void ApplyCommercialLotSizing(
        MaterialSupplyRequirement action,
        IReadOnlyCollection<MaterialSourcingRule>? rules)
    {
        if (action.ActionType is not (MaterialSupplyActionType.Buy or MaterialSupplyActionType.Transfer))
        {
            action.PlannedOrderQuantityMt = action.QuantityMt;
            action.ExcessQuantityMt = 0m;
            return;
        }

        var rule = (rules ?? Array.Empty<MaterialSourcingRule>())
            .Where(x => x.IsActive &&
                        Matches(x.MaterialSpecificationCode, action.MaterialSpecificationCode) &&
                        Matches(x.MaterialCode, action.MaterialCode) &&
                        Matches(x.GradeCode, action.GradeCode) &&
                        Matches(x.CrossSectionCode, action.CrossSectionCode))
            .OrderByDescending(Specificity)
            .ThenBy(x => x.RuleCode, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var quantity = action.QuantityMt;
        if (action.ActionType == MaterialSupplyActionType.Buy)
        {
            if (rule?.MinimumBuyQuantityMt is { } minimum) quantity = Math.Max(quantity, minimum);
            if (rule?.BuyOrderMultipleMt is { } multiple && multiple > 0m) quantity = RoundUp(quantity, multiple);
        }
        else if (rule?.MinimumTransferQuantityMt is { } transferMinimum)
        {
            quantity = Math.Max(quantity, transferMinimum);
        }

        action.PlannedOrderQuantityMt = decimal.Round(quantity, 4, MidpointRounding.AwayFromZero);
        action.ExcessQuantityMt = decimal.Round(Math.Max(0m, quantity - action.QuantityMt), 4, MidpointRounding.AwayFromZero);
        if (action.ExcessQuantityMt > 0m)
        {
            action.Explanation = string.Concat(
                action.Explanation,
                $" Commercial lot sizing requires {action.PlannedOrderQuantityMt:0.####} MT; {action.ExcessQuantityMt:0.####} MT remains projected excess inventory and is not reserved to this demand.");
        }
    }

    private static decimal RoundUp(decimal quantity, decimal multiple) =>
        Math.Ceiling(quantity / multiple) * multiple;

    private static bool Matches(string? configured, string? actual) =>
        string.IsNullOrWhiteSpace(configured) || string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);

    private static int Specificity(MaterialSourcingRule rule)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(rule.MaterialSpecificationCode)) score += 16;
        if (!string.IsNullOrWhiteSpace(rule.MaterialCode)) score += 8;
        if (!string.IsNullOrWhiteSpace(rule.GradeCode)) score += 4;
        if (!string.IsNullOrWhiteSpace(rule.CrossSectionCode)) score += 2;
        if (rule.ProductForm.HasValue) score += 1;
        return score;
    }
}
