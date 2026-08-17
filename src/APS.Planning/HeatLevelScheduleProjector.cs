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
        var resourceById = resources.ToDictionary(x => x.Id);
        var capabilitiesByResource = capabilities
            .GroupBy(x => x.ResourceId)
            .ToDictionary(x => x.Key, x => x.ToArray());

        var originalRollingTasks = structure.SchedulingTasks
            .Where(x => x.TaskType is FiniteScheduleTaskType.HotRolling or FiniteScheduleTaskType.ColdRolling)
            .ToDictionary(x => x.SourceEntityId);

        var heatTasks = new List<FiniteScheduleTask>();
        var heatTaskByHeatId = new Dictionary<Guid, FiniteScheduleTask>();
        var heatCasterByTaskId = new Dictionary<Guid, Guid>();
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
                heatCasterByTaskId[taskId] = sequence.CasterResourceId;
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

        var rollingTasks = new List<FiniteScheduleTask>();
        foreach (var plan in structure.RollingPlans)
        {
            if (!originalRollingTasks.TryGetValue(plan.Id, out var original)) continue;
            if (!plan.RollingMillResourceId.HasValue)
            {
                rollingTasks.Add(original);
                continue;
            }

            var dependencies = new List<FiniteScheduleDependency>();
            foreach (var allocation in plan.Allocations.Where(x => x.FreshSteelQuantityMt > 0m))
            {
                foreach (var sequence in structure.CastSequences)
                {
                    foreach (var sequenceHeat in sequence.Heats.Where(x =>
                                 x.CampaignHeat.CampaignId == allocation.CampaignId &&
                                 string.Equals(x.CampaignHeat.GradeCode, plan.GradeCode, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (!heatTaskByHeatId.TryGetValue(sequenceHeat.CampaignHeatId, out var predecessor)) continue;
                        if (!heatCasterByTaskId.TryGetValue(predecessor.TaskId, out var casterId)) continue;

                        var link = flowLinks.FirstOrDefault(x =>
                            x.IsEnabled &&
                            x.FromResourceId == casterId &&
                            x.ToResourceId == plan.RollingMillResourceId.Value);

                        dependencies.Add(new FiniteScheduleDependency(
                            predecessor.TaskId,
                            link is null ? 0 : Minutes(link.MinimumTransferTime),
                            link?.MaximumTransferTime is null ? null : Minutes(link.MaximumTransferTime.Value)));
                    }
                }
            }

            rollingTasks.Add(original with
            {
                Dependencies = dependencies
                    .GroupBy(x => x.PredecessorTaskId)
                    .Select(g => g.OrderByDescending(x => x.MinimumLagMinutes).First())
                    .ToArray()
            });
        }

        return structure with
        {
            SchedulingTasks = heatTasks.Concat(rollingTasks).ToArray(),
            PlannedStrandMaterialUnits = materialUnits
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
