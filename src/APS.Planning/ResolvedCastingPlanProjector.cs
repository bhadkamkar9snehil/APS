using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class ResolvedCastingPlanProjector
{
    public static ProductionStructurePlanningResult Apply(
        ProductionStructurePlanningResult structure,
        FiniteScheduleResult schedule,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyCollection<CampaignHeatAllocation>? heatAllocations)
    {
        if (!schedule.IsFeasible) return structure;

        var issues = structure.Issues.ToList();
        var resourceById = resources.ToDictionary(x => x.Id);
        var assignmentByTask = schedule.Assignments.ToDictionary(x => x.TaskId);
        var ccmTaskByHeat = structure.SchedulingTasks
            .Where(x => x.ProcessOperationType == ProcessOperationType.Ccm)
            .GroupBy(x => x.SourceEntityId)
            .ToDictionary(x => x.Key, x => x.First());
        var outputByHeat = (heatAllocations ?? Array.Empty<CampaignHeatAllocation>())
            .GroupBy(x => x.CampaignHeatId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.PlannedOutputQuantityMt));

        var resolvedCasterByHeat = new Dictionary<Guid, Guid>();
        var strandUnits = new List<PlannedStrandMaterialUnit>();

        foreach (var sequence in structure.CastSequences)
        {
            var sequenceAssignments = new List<FiniteScheduleAssignment>();
            foreach (var sequenceHeat in sequence.Heats.OrderBy(x => x.Position))
            {
                var heat = sequenceHeat.CampaignHeat;
                if (!ccmTaskByHeat.TryGetValue(heat.Id, out var task) ||
                    !assignmentByTask.TryGetValue(task.TaskId, out var assignment))
                {
                    issues.Add(new PlanningIssue(
                        PlanningIssueSeverity.Error,
                        "CCM_ASSIGNMENT_MISSING",
                        $"Heat {heat.Id} has no resolved CCM assignment in the finite schedule.",
                        heat.Id));
                    continue;
                }

                resolvedCasterByHeat[heat.Id] = assignment.ResourceId;
                sequenceAssignments.Add(assignment);
            }

            var distinctCasters = sequenceAssignments.Select(x => x.ResourceId).Distinct().ToArray();
            if (distinctCasters.Length == 0) continue;
            if (distinctCasters.Length != 1)
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "CAST_SEQUENCE_SPLIT_ACROSS_CCMS",
                    $"Logical cast sequence {sequence.SequenceNumber} was assigned across {distinctCasters.Length} physical CCMs. Continuous sequence heats must remain on one CCM.",
                    sequence.Id));
                continue;
            }

            var casterId = distinctCasters[0];
            sequence.CasterResourceId = casterId;
            sequence.PlannedStart = sequenceAssignments.Min(x => x.StartUtc);
            sequence.PlannedEnd = sequenceAssignments.Max(x => x.EndUtc);

            if (!resourceById.TryGetValue(casterId, out var caster))
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "CCM_RESOURCE_MISSING",
                    $"Resolved CCM resource {casterId} is not present in the planning resource snapshot.",
                    sequence.Id));
                continue;
            }

            var strandCount = Math.Max(1, caster.StrandCount ?? 1);
            foreach (var sequenceHeat in sequence.Heats.OrderBy(x => x.Position))
            {
                var heat = sequenceHeat.CampaignHeat;
                if (!ccmTaskByHeat.TryGetValue(heat.Id, out var task)) continue;

                var outputQuantity = outputByHeat.TryGetValue(heat.Id, out var exactOutput)
                    ? exactOutput
                    : structure.PlannedBilletSupplies
                        .Where(x => x.CampaignHeatId == heat.Id)
                        .Sum(x => x.QuantityMt);
                if (outputQuantity <= 0m) continue;

                var nominalStrandQuantity = decimal.Round(
                    outputQuantity / strandCount,
                    4,
                    MidpointRounding.AwayFromZero);
                var allocated = 0m;
                for (var strand = 1; strand <= strandCount; strand++)
                {
                    var quantity = strand == strandCount
                        ? outputQuantity - allocated
                        : nominalStrandQuantity;
                    allocated += quantity;

                    var campaign = heat.Campaign;
                    strandUnits.Add(new PlannedStrandMaterialUnit(
                        campaign is null
                            ? $"CAST:{heat.Id:N}:S{strand:00}"
                            : $"CAST:{campaign.CampaignNumber}:H{heat.SequenceNumber:00}:S{strand:00}",
                        heat.CampaignId,
                        heat.Id,
                        sequence.Id,
                        casterId,
                        strand,
                        1,
                        heat.GradeCode,
                        sequence.CasterSectionCode,
                        quantity,
                        task.TaskId));
                }
            }
        }

        var resolvedSupplies = structure.PlannedBilletSupplies
            .Select(supply => resolvedCasterByHeat.TryGetValue(supply.CampaignHeatId, out var casterId)
                ? supply with { CasterResourceId = casterId }
                : supply)
            .ToArray();

        return structure with
        {
            PlannedBilletSupplies = resolvedSupplies,
            PlannedStrandMaterialUnits = strandUnits,
            Issues = issues
        };
    }
}
