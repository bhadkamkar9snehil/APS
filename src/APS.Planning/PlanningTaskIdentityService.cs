using System.Security.Cryptography;
using System.Text;
using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class PlanningTaskIdentityService
{
    public static IReadOnlyCollection<PlanningTaskIdentity> Build(ProductionStructurePlanningResult structure)
    {
        var heats = structure.CastSequences
            .SelectMany(sequence => sequence.Heats)
            .Select(x => x.CampaignHeat)
            .DistinctBy(x => x.Id)
            .ToDictionary(x => x.Id);
        var rollingPlans = structure.RollingPlans.ToDictionary(x => x.Id);
        var routePlans = (structure.RouteOperationPlans ?? Array.Empty<RouteOperationPlan>())
            .ToDictionary(x => x.Id);
        var ordinalByTask = structure.SchedulingTasks
            .GroupBy(x => x.SourceEntityId)
            .SelectMany(group => group.Select((task, index) => new { task.TaskId, Ordinal = index + 1 }))
            .ToDictionary(x => x.TaskId, x => x.Ordinal);

        return structure.SchedulingTasks
            .Select(task => new PlanningTaskIdentity(
                task.TaskId,
                task.SourceEntityId,
                StableKey(task, ordinalByTask[task.TaskId], heats, rollingPlans, routePlans),
                task.TaskType))
            .ToArray();
    }

    private static string StableKey(
        FiniteScheduleTask task,
        int sourceOrdinal,
        IReadOnlyDictionary<Guid, CampaignHeat> heats,
        IReadOnlyDictionary<Guid, RollingPlan> rollingPlans,
        IReadOnlyDictionary<Guid, RouteOperationPlan> routePlans)
    {
        if (task.TaskType == FiniteScheduleTaskType.Casting && heats.TryGetValue(task.SourceEntityId, out var heat))
        {
            var campaign = heat.Campaign;
            var productionOrders = campaign?.Allocations
                .Where(x => x.ProductionOrder is not null &&
                            x.FreshSteelQuantityMt > 0m &&
                            string.Equals(x.ProductionOrder.GradeCode, heat.GradeCode, StringComparison.OrdinalIgnoreCase))
                .Select(x => $"{x.ProductionOrder!.ProductionOrderNumber}:{x.FreshSteelQuantityMt:0.####}")
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();

            var gradeOrdinal = campaign?.Heats
                .Where(x => string.Equals(x.GradeCode, heat.GradeCode, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.SequenceNumber)
                .Select((x, index) => new { x.Id, Ordinal = index + 1 })
                .FirstOrDefault(x => x.Id == heat.Id)?.Ordinal ?? heat.SequenceNumber;

            return Key("CAST", string.Join("|",
                heat.GradeCode,
                campaign?.CasterSectionCode ?? task.CrossSectionCode,
                campaign?.RouteCode ?? string.Empty,
                gradeOrdinal,
                string.Join(",", productionOrders)));
        }

        // #58: configured downstream tasks are route-operation tasks, including the first HotRoll.
        // Their identity follows route position + PO allocation membership rather than a special
        // RollingPlan-first-mill identity, so replan stability survives route-driven projection.
        if (routePlans.TryGetValue(task.SourceEntityId, out var routePlan))
        {
            var allocations = routePlan.Allocations
                .Where(x => x.ProductionOrder is not null)
                .Select(x => $"{x.ProductionOrder!.ProductionOrderNumber}:{x.PlannedQuantityMt:0.####}")
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return Key("ROUTE", string.Join("|",
                routePlan.RouteCode,
                routePlan.SequenceNumber,
                routePlan.ProcessOperationType,
                routePlan.GradeCode,
                routePlan.InputCrossSectionCode,
                routePlan.OutputCrossSectionCode,
                sourceOrdinal,
                task.QuantityMt.ToString("0.####"),
                string.Join(",", allocations)));
        }

        // Compatibility/demo mode still has direct RollingPlan tasks; keep their established identity.
        if (task.TaskType is FiniteScheduleTaskType.HotRolling or FiniteScheduleTaskType.ColdRolling &&
            rollingPlans.TryGetValue(task.SourceEntityId, out var plan))
        {
            var allocations = plan.Allocations
                .Where(x => x.ProductionOrder is not null)
                .Select(x => $"{x.ProductionOrder!.ProductionOrderNumber}:{x.PlannedQuantityMt:0.####}")
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var feed = plan.FreshSteelQuantityMt > 0m ? "FRESH" : "INVENTORY";

            return Key("ROLL", string.Join("|",
                plan.GradeCode,
                plan.InputCrossSectionCode,
                plan.OutputCrossSectionCode,
                plan.RouteCode,
                feed,
                sourceOrdinal,
                task.QuantityMt.ToString("0.####"),
                string.Join(",", allocations)));
        }

        return Key(task.TaskType.ToString().ToUpperInvariant(), string.Join("|",
            task.Name,
            task.GradeCode,
            task.CrossSectionCode,
            task.QuantityMt.ToString("0.####"),
            sourceOrdinal));
    }

    private static string Key(string prefix, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"{prefix}:{Convert.ToHexString(hash.AsSpan(0, 12))}";
    }
}
