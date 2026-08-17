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
        ProductionStructurePlanningPolicy policy,
        IReadOnlyCollection<CampaignHeatAllocation>? heatAllocations = null)
    {
        var issues = structure.Issues.ToList();
        var resourceById = resources.ToDictionary(x => x.Id);
        var capabilitiesByResource = capabilities.GroupBy(x => x.ResourceId).ToDictionary(x => x.Key, x => x.ToArray());
        var allocationsByHeat = (heatAllocations ?? Array.Empty<CampaignHeatAllocation>())
            .GroupBy(x => x.CampaignHeatId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var explicitSteelTopology = resources.Any(x => x.ProcessUnitType != ProcessUnitType.Unknown);

        var originalRollingTasks = structure.SchedulingTasks
            .Where(x => x.TaskType is FiniteScheduleTaskType.HotRolling or FiniteScheduleTaskType.ColdRolling)
            .ToDictionary(x => x.SourceEntityId);

        var heatTasks = new List<FiniteScheduleTask>();
        var heatTaskByHeatId = new Dictionary<Guid, FiniteScheduleTask>();
        var materialUnits = new List<PlannedStrandMaterialUnit>();

        foreach (var sequence in structure.CastSequences.OrderBy(x => x.CasterResourceId).ThenBy(x => x.SequenceNumber))
        {
            if (!resourceById.TryGetValue(sequence.CasterResourceId, out var caster)) continue;
            var strands = Math.Max(1, caster.StrandCount ?? 1);
            Guid? previousTaskId = null;

            foreach (var sequenceHeat in sequence.Heats.OrderBy(x => x.Position))
            {
                var heat = sequenceHeat.CampaignHeat;
                var campaign = heat.Campaign;
                if (campaign is null) continue;
                var duration = HeatDurationMinutes(heat, campaign, sequence.CasterResourceId, capabilitiesByResource, policy.DefaultCastingMinutesPerHeat);
                var taskId = Guid.NewGuid();
                var dependencies = previousTaskId.HasValue ? new[] { new FiniteScheduleDependency(previousTaskId.Value) } : Array.Empty<FiniteScheduleDependency>();

                var exact = allocationsByHeat.TryGetValue(heat.Id, out var heatAllocationsForHeat)
                    ? heatAllocationsForHeat.Where(x => x.ProductionOrder is not null).ToArray()
                    : Array.Empty<CampaignHeatAllocation>();
                var due = exact.Length > 0
                    ? exact.Min(x => x.ProductionOrder!.RequiredDate)
                    : campaign.RequiredDate;
                var priority = exact.Length > 0
                    ? exact.Max(x => x.ProductionOrder!.Priority)
                    : campaign.Allocations.Where(x => x.ProductionOrder is not null && string.Equals(x.ProductionOrder.GradeCode, heat.GradeCode, StringComparison.OrdinalIgnoreCase)).Select(x => x.ProductionOrder!.Priority).DefaultIfEmpty(0).Max();

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
                    dependencies,
                    ProcessOperationType.Ccm);

                heatTasks.Add(task);
                heatTaskByHeatId[heat.Id] = task;
                previousTaskId = taskId;

                var plannedOutput = exact.Length > 0
                    ? exact.Sum(x => x.PlannedOutputQuantityMt)
                    : structure.PlannedBilletSupplies.Where(x => x.CampaignHeatId == heat.Id).Sum(x => x.QuantityMt);
                var strandQuantity = decimal.Round(plannedOutput / strands, 4, MidpointRounding.AwayFromZero);
                var allocated = 0m;
                for (var strand = 1; strand <= strands; strand++)
                {
                    var quantity = strand == strands ? plannedOutput - allocated : strandQuantity;
                    allocated += quantity;
                    materialUnits.Add(new PlannedStrandMaterialUnit(
                        $"CAST:{campaign.CampaignNumber}:H{heat.SequenceNumber:00}:S{strand:00}",
                        campaign.Id, heat.Id, sequence.Id, sequence.CasterResourceId, strand, 1,
                        heat.GradeCode, campaign.CasterSectionCode, quantity, taskId));
                }
            }
        }

        var remainingSupplyByHeat = heatTasks.ToDictionary(
            x => x.SourceEntityId,
            x => allocationsByHeat.TryGetValue(x.SourceEntityId, out var exact)
                ? exact.Sum(y => y.PlannedOutputQuantityMt)
                : structure.PlannedBilletSupplies.Where(y => y.CampaignHeatId == x.SourceEntityId).Sum(y => y.QuantityMt));

        var rollingTasks = new List<FiniteScheduleTask>();
        foreach (var plan in structure.RollingPlans.OrderBy(x => x.SequenceNumber))
        {
            if (!originalRollingTasks.TryGetValue(plan.Id, out var original)) continue;
            if (plan.FreshSteelQuantityMt <= 0m)
            {
                rollingTasks.Add(original with { ProcessOperationType = ProcessOperationType.HotRoll });
                continue;
            }

            var eligibleCampaigns = plan.Allocations.Where(x => x.FreshSteelQuantityMt > 0m).Select(x => x.CampaignId).ToHashSet();
            var candidateHeats = structure.CastSequences
                .SelectMany(sequence => sequence.Heats)
                .Where(x => eligibleCampaigns.Contains(x.CampaignHeat.CampaignId) &&
                            string.Equals(x.CampaignHeat.GradeCode, plan.GradeCode, StringComparison.OrdinalIgnoreCase) &&
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
                if (!heatTaskByHeatId.TryGetValue(heatId, out var predecessor)) continue;

                var blockQuantity = Math.Min(remainingRequirement, available);
                remainingRequirement -= blockQuantity;
                remainingSupplyByHeat[heatId] = available - blockQuantity;
                feedBlock++;

                var options = original.ResourceOptions
                    .Select(option => option with
                    {
                        DurationMinutes = Math.Max(1, (int)Math.Ceiling(option.DurationMinutes * (double)(blockQuantity / Math.Max(plan.PlannedQuantityMt, 0.0001m))))
                    })
                    .ToArray();
                var pairs = new List<FiniteScheduleDependencyResourcePair>();
                foreach (var option in options)
                {
                    var link = flowLinks.FirstOrDefault(x => x.IsEnabled && x.FromResourceId == predecessor.ResourceOptions.Single().ResourceId && x.ToResourceId == option.ResourceId);
                    if (link is null) continue;
                    pairs.Add(new FiniteScheduleDependencyResourcePair(
                        predecessor.ResourceOptions.Single().ResourceId,
                        option.ResourceId,
                        Minutes(link.MinimumTransferTime),
                        link.MaximumTransferTime.HasValue ? Minutes(link.MaximumTransferTime.Value) : null));
                }

                if (explicitSteelTopology && pairs.Count == 0)
                {
                    issues.Add(new PlanningIssue(PlanningIssueSeverity.Error, "CAST_TO_MILL_FLOW_MISSING", $"No enabled physical path can move heat {heatId} to an eligible hot rolling mill for plan {plan.Id}.", plan.Id));
                    continue;
                }

                var dependency = pairs.Count > 0
                    ? new FiniteScheduleDependency(predecessor.TaskId, 0, null, pairs)
                    : new FiniteScheduleDependency(predecessor.TaskId);

                rollingTasks.Add(original with
                {
                    TaskId = Guid.NewGuid(),
                    Name = $"{original.Name} / Feed {feedBlock:00}",
                    QuantityMt = blockQuantity,
                    ResourceOptions = options,
                    Dependencies = new[] { dependency },
                    ProcessOperationType = ProcessOperationType.HotRoll
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
        if (!capabilitiesByResource.TryGetValue(casterResourceId, out var capabilities)) return Math.Max(1, fallbackMinutes);
        var fixedDuration = capabilities
            .Where(x => (!x.ProcessOperationType.HasValue || x.ProcessOperationType == ProcessOperationType.Ccm) && Matches(x.RouteCode, campaign.RouteCode) && Matches(x.GradeCode, heat.GradeCode) && Matches(x.OutputCrossSectionCode, campaign.CasterSectionCode) && x.FixedDurationMinutes.HasValue)
            .Select(x => x.FixedDurationMinutes!.Value)
            .DefaultIfEmpty(0)
            .Max();
        if (fixedDuration > 0) return fixedDuration;

        var throughput = capabilities
            .Where(x => (!x.ProcessOperationType.HasValue || x.ProcessOperationType == ProcessOperationType.Ccm) && Matches(x.RouteCode, campaign.RouteCode) && Matches(x.GradeCode, heat.GradeCode) && Matches(x.OutputCrossSectionCode, campaign.CasterSectionCode) && x.ThroughputMtPerHour.HasValue && x.ThroughputMtPerHour.Value > 0m)
            .Select(x => x.ThroughputMtPerHour!.Value)
            .DefaultIfEmpty(0m)
            .Max();
        return throughput <= 0m ? Math.Max(1, fallbackMinutes) : Math.Max(1, (int)Math.Ceiling((double)(heat.PlannedQuantityMt / throughput * 60m)));
    }

    private static bool Matches(string? configured, string actual) =>
        string.IsNullOrWhiteSpace(configured) || string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);
    private static int Minutes(TimeSpan value) => Math.Max(0, (int)Math.Ceiling(value.TotalMinutes));
}
