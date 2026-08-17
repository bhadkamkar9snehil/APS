using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class HeatLevelScheduleProjector
{
    public static ProductionStructurePlanningResult Apply(
        ProductionStructurePlanningResult structure,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyCollection<ResourceCapability> capabilities,
        IReadOnlyCollection<PlantFlowLink> flowLinks,
        ProductionStructurePlanningPolicy policy)
    {
        var issues = structure.Issues.ToList();
        var resourceById = resources.ToDictionary(x => x.Id);
        var capabilitiesByResource = capabilities
            .GroupBy(x => x.ResourceId)
            .ToDictionary(x => x.Key, x => x.ToArray());

        var originalRollingTasks = structure.SchedulingTasks
            .Where(x => x.TaskType is FiniteScheduleTaskType.HotRolling or FiniteScheduleTaskType.ColdRolling)
            .ToDictionary(x => x.SourceEntityId);

        var heatTasks = new List<FiniteScheduleTask>();
        var heatTaskByHeatId = new Dictionary<Guid, FiniteScheduleTask>();
        var materialUnits = new List<PlannedStrandMaterialUnit>();

        foreach (var sequence in structure.CastSequences
                     .OrderBy(x => x.CasterResourceId)
                     .ThenBy(x => x.SequenceNumber))
        {
            if (!resourceById.TryGetValue(sequence.CasterResourceId, out var caster)) continue;
            var strands = Math.Max(1, caster.StrandCount ?? 1);
            Guid? previousTaskId = null;

            foreach (var sequenceHeat in sequence.Heats.OrderBy(x => x.Position))
            {
                var heat = sequenceHeat.CampaignHeat;
                var campaign = heat.Campaign;
                if (campaign is null) continue;

                var duration = HeatDurationMinutes(
                    heat,
                    campaign,
                    sequence.CasterResourceId,
                    capabilitiesByResource,
                    policy.DefaultCastingMinutesPerHeat);

                var taskId = Guid.NewGuid();
                var dependencies = previousTaskId.HasValue
                    ? new[] { new FiniteScheduleDependency(previousTaskId.Value) }
                    : Array.Empty<FiniteScheduleDependency>();

                var gradeAllocations = campaign.Allocations
                    .Where(a => a.ProductionOrder is not null &&
                                a.FreshSteelQuantityMt > 0m &&
                                string.Equals(a.ProductionOrder.GradeCode, heat.GradeCode, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var due = gradeAllocations
                    .Select(a => a.ProductionOrder!.RequiredDate)
                    .DefaultIfEmpty(campaign.RequiredDate)
                    .Min();
                var priority = gradeAllocations
                    .Select(a => a.ProductionOrder!.Priority)
                    .DefaultIfEmpty(0)
                    .Max();

                var task = new FiniteScheduleTask(
                    taskId,
                    heat.Id,
                    FiniteScheduleTaskType.Casting,
                    $"Heat {campaign.CampaignNumber}/{heat.SequenceNumber:00}",
                    heat.GradeCode,
                    campaign.CasterSectionCode,
                    heat.PlannedQuantityMt,
                    null,
                    due,
                    priority,
                    new[] { new FiniteScheduleResourceOption(sequence.CasterResourceId, duration) },
                    dependencies);

                heatTasks.Add(task);
                heatTaskByHeatId[heat.Id] = task;
                previousTaskId = taskId;

                var plannedOutput = structure.PlannedBilletSupplies
                    .Where(x => x.CampaignHeatId == heat.Id)
                    .Sum(x => x.QuantityMt);
                var strandQuantity = decimal.Round(plannedOutput / strands, 4, MidpointRounding.AwayFromZero);
                var allocated = 0m;

                for (var strand = 1; strand <= strands; strand++)
                {
                    var quantity = strand == strands ? plannedOutput - allocated : strandQuantity;
                    allocated += quantity;
                    materialUnits.Add(new PlannedStrandMaterialUnit(
                        $"CAST:{campaign.CampaignNumber}:H{heat.SequenceNumber:00}:S{strand:00}",
                        campaign.Id,
                        heat.Id,
                        sequence.Id,
                        sequence.CasterResourceId,
                        strand,
                        1,
                        heat.GradeCode,
                        campaign.CasterSectionCode,
                        quantity,
                        taskId));
                }
            }
        }

        var remainingSupplyByHeat = structure.PlannedBilletSupplies
            .GroupBy(x => x.CampaignHeatId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.QuantityMt));
        var supplyByHeat = structure.PlannedBilletSupplies
            .GroupBy(x => x.CampaignHeatId)
            .ToDictionary(x => x.Key, x => x.First());

        var rollingTasks = new List<FiniteScheduleTask>();
        foreach (var plan in structure.RollingPlans
                     .OrderBy(x => x.RollingMillResourceId)
                     .ThenBy(x => x.SequenceNumber))
        {
            if (!originalRollingTasks.TryGetValue(plan.Id, out var original)) continue;
            if (!plan.RollingMillResourceId.HasValue || plan.FreshSteelQuantityMt <= 0m)
            {
                rollingTasks.Add(original);
                continue;
            }

            var eligibleCampaigns = plan.Allocations
                .Where(x => x.FreshSteelQuantityMt > 0m)
                .Select(x => x.CampaignId)
                .ToHashSet();
            var candidateHeats = structure.PlannedBilletSupplies
                .Where(x => eligibleCampaigns.Contains(x.CampaignId) &&
                            string.Equals(x.GradeCode, plan.GradeCode, StringComparison.OrdinalIgnoreCase) &&
                            heatTaskByHeatId.ContainsKey(x.CampaignHeatId))
                .Select(x => x.CampaignHeatId)
                .Distinct()
                .OrderBy(heatId => heatTasks.FindIndex(t => t.SourceEntityId == heatId))
                .ToArray();

            var remainingRequirement = plan.FreshSteelQuantityMt;
            var feedBlock = 0;
            foreach (var heatId in candidateHeats)
            {
                if (remainingRequirement <= 0m) break;
                if (!remainingSupplyByHeat.TryGetValue(heatId, out var available) || available <= 0m) continue;
                if (!supplyByHeat.TryGetValue(heatId, out var supply)) continue;
                if (!heatTaskByHeatId.TryGetValue(heatId, out var predecessor)) continue;

                var blockQuantity = Math.Min(remainingRequirement, available);
                remainingRequirement -= blockQuantity;
                remainingSupplyByHeat[heatId] = available - blockQuantity;
                feedBlock++;

                var link = flowLinks.FirstOrDefault(x =>
                    x.IsEnabled &&
                    x.FromResourceId == supply.CasterResourceId &&
                    x.ToResourceId == plan.RollingMillResourceId.Value);
                var maxLag = link?.MaximumTransferTime is { } maximumTransfer
                    ? Minutes(maximumTransfer)
                    : (int?)null;
                var dependency = new FiniteScheduleDependency(
                    predecessor.TaskId,
                    link is null ? 0 : Minutes(link.MinimumTransferTime),
                    maxLag);

                var baseOption = original.ResourceOptions.Single(x => x.ResourceId == plan.RollingMillResourceId.Value);
                var blockDuration = Math.Max(1, (int)Math.Ceiling(
                    baseOption.DurationMinutes * (double)(blockQuantity / Math.Max(plan.PlannedQuantityMt, 0.0001m))));

                rollingTasks.Add(original with
                {
                    TaskId = Guid.NewGuid(),
                    Name = $"{original.Name} / Feed {feedBlock:00}",
                    QuantityMt = blockQuantity,
                    ResourceOptions = new[]
                    {
                        baseOption with { DurationMinutes = blockDuration }
                    },
                    Dependencies = new[] { dependency }
                });
            }

            if (remainingRequirement > 0.0001m)
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "INSUFFICIENT_PLANNED_CAST_OUTPUT",
                    $"Rolling plan {plan.Id} requires {plan.FreshSteelQuantityMt:0.####} MT fresh feed but only {plan.FreshSteelQuantityMt - remainingRequirement:0.####} MT planned cast output is available after yield and prior allocations.",
                    plan.Id));
            }
        }

        return structure with
        {
            SchedulingTasks = heatTasks.Concat(rollingTasks).ToArray(),
            PlannedStrandMaterialUnits = materialUnits,
            Issues = issues
        };
    }

    private static int HeatDurationMinutes(
        CampaignHeat heat,
        Campaign campaign,
        Guid casterResourceId,
        IReadOnlyDictionary<Guid, ResourceCapability[]> capabilitiesByResource,
        int fallbackMinutes)
    {
        if (!capabilitiesByResource.TryGetValue(casterResourceId, out var capabilities))
        {
            return Math.Max(1, fallbackMinutes);
        }

        var throughput = capabilities
            .Where(x =>
                Matches(x.RouteCode, campaign.RouteCode) &&
                Matches(x.GradeCode, heat.GradeCode) &&
                Matches(x.OutputCrossSectionCode, campaign.CasterSectionCode) &&
                x.ThroughputMtPerHour.HasValue &&
                x.ThroughputMtPerHour.Value > 0m)
            .Select(x => x.ThroughputMtPerHour!.Value)
            .DefaultIfEmpty(0m)
            .Max();

        return throughput <= 0m
            ? Math.Max(1, fallbackMinutes)
            : Math.Max(1, (int)Math.Ceiling((double)(heat.PlannedQuantityMt / throughput * 60m)));
    }

    private static bool Matches(string? configured, string actual) =>
        string.IsNullOrWhiteSpace(configured) ||
        string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);

    private static int Minutes(TimeSpan value) => Math.Max(0, (int)Math.Ceiling(value.TotalMinutes));
}
