using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class MaterialBalanceValidator
{
    public static IReadOnlyCollection<PlanningIssue> Validate(IReadOnlyCollection<MaterialBalanceEvent> events)
    {
        var issues = new List<PlanningIssue>();

        foreach (var pool in events.GroupBy(x => x.MaterialPoolKey, StringComparer.OrdinalIgnoreCase))
        {
            decimal balance = 0m;
            foreach (var item in pool
                         .OrderBy(x => x.EffectiveAtUtc)
                         .ThenByDescending(x => x.QuantityDeltaMt)) // same-timestamp receipt is available before consumption.
            {
                balance += item.QuantityDeltaMt;
                if (balance >= -0.0001m) continue;

                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "TIME_PHASED_MATERIAL_SHORTAGE",
                    $"Material pool {pool.Key} becomes negative by {-balance:0.####} MT at {item.EffectiveAtUtc:O}. " +
                    $"Triggering event: {item.EventType} {item.QuantityDeltaMt:0.####} MT ({item.Explanation}).",
                    item.ProductionOrderId));
                break;
            }
        }

        return issues;
    }
}
