using APS.Application;
using APS.Domain;
using APS.Planning;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed partial class PlannerWorkspaceQueryService
{
    public async Task<SteelmakingCastingWorkspaceView?> GetSteelmakingCastingAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default)
    {
        var plan = await ResolvePlanAsync(planVersionId, cancellationToken);
        if (plan is null) return null;

        var heats = await db.PlanHeatSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .OrderBy(x => x.CampaignId)
            .ThenBy(x => x.SequenceNumber)
            .ToListAsync(cancellationToken);
        var campaigns = await db.PlanCampaignSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .ToListAsync(cancellationToken);
        var castSequences = await db.PlanCastSequenceSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .ToListAsync(cancellationToken);
        var castSequenceHeats = await db.PlanCastSequenceHeatSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .ToListAsync(cancellationToken);
        var materialUnits = await db.PlanMaterialUnitSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .ToListAsync(cancellationToken);

        var processTypes = new[]
        {
            ProcessOperationType.Eaf,
            ProcessOperationType.Lrf,
            ProcessOperationType.Vd,
            ProcessOperationType.Ccm
        };
        var operationRows = await db.PlanOperationSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId && processTypes.Contains(x.ProcessOperationType))
            .OrderBy(x => x.StartUtc)
            .ToListAsync(cancellationToken);
        var operationViews = await BuildOperationViewsAsync(operationRows, cancellationToken);

        var campaignById = campaigns.ToDictionary(x => x.CampaignId);
        var sequenceById = castSequences.ToDictionary(x => x.CastSequenceId);
        var sequenceLinkByHeat = castSequenceHeats
            .GroupBy(x => x.CampaignHeatId)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.Position).First());

        var ccmResourceIds = castSequences
            .Select(x => x.CasterResourceId)
            .Where(x => x != Guid.Empty)
            .Concat(operationViews.Where(x => x.ProcessOperationType == ProcessOperationType.Ccm).Select(x => x.ResourceId))
            .Distinct()
            .ToArray();
        var ccmResources = ccmResourceIds.Length == 0
            ? new Dictionary<Guid, Resource>()
            : await db.Resources.AsNoTracking()
                .Where(x => ccmResourceIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var result = heats.Select(heat =>
        {
            campaignById.TryGetValue(heat.CampaignId, out var campaign);
            PlanCastSequenceSnapshot? sequence = null;
            if (sequenceLinkByHeat.TryGetValue(heat.CampaignHeatId, out var link))
            {
                sequenceById.TryGetValue(link.CastSequenceId, out sequence);
            }

            var heatOperations = operationViews
                .Where(x => x.SourceEntityId == heat.CampaignHeatId)
                .OrderBy(x => x.StartUtc)
                .ToArray();
            var ccmOperation = heatOperations.FirstOrDefault(x => x.ProcessOperationType == ProcessOperationType.Ccm);
            var casterId = sequence?.CasterResourceId is { } sequenceCaster && sequenceCaster != Guid.Empty
                ? sequenceCaster
                : ccmOperation?.ResourceId;
            string? casterCode = null;
            if (casterId.HasValue && ccmResources.TryGetValue(casterId.Value, out var caster)) casterCode = caster.Code;

            var strands = materialUnits
                .Where(x => x.CampaignHeatId == heat.CampaignHeatId)
                .OrderBy(x => x.StrandNumber)
                .ThenBy(x => x.UnitSequence)
                .Select(x => new StrandOutputView(
                    x.Id,
                    x.PlanningKey,
                    x.StrandNumber,
                    x.UnitSequence,
                    x.GradeCode,
                    x.CrossSectionCode,
                    x.QuantityMt,
                    x.AvailableOnUtc))
                .ToArray();

            return new HeatProcessView(
                heat.CampaignId,
                campaign?.CampaignNumber ?? heat.CampaignId.ToString("N")[..8],
                heat.CampaignHeatId,
                heat.SequenceNumber,
                heat.GradeCode,
                heat.PlannedQuantityMt,
                sequence?.CastSequenceId,
                sequence?.SequenceNumber,
                casterId,
                casterCode,
                sequence?.TundishNumber,
                heatOperations,
                strands);
        }).ToArray();

        return new SteelmakingCastingWorkspaceView(
            plan,
            result.Length,
            castSequences.Count,
            heats.Sum(x => x.PlannedQuantityMt),
            materialUnits.Sum(x => x.QuantityMt),
            result);
    }

    public async Task<FiniteScheduleWorkspaceView?> GetFiniteScheduleAsync(
        Guid? planVersionId = null,
        CancellationToken cancellationToken = default)
    {
        var plan = await ResolvePlanAsync(planVersionId, cancellationToken);
        if (plan is null) return null;

        var assumptions = await GetPlanningAssumptionsAsync(plan.PlanVersionId, cancellationToken);
        var resourceAssumptions = assumptions?.ResourceScheduling
            .GroupBy(x => x.ResourceId)
            .ToDictionary(x => x.Key, x => x.Last())
            ?? new Dictionary<Guid, ResourceSchedulingAssumption>();
        var operationRows = await db.PlanOperationSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .OrderBy(x => x.StartUtc)
            .ToListAsync(cancellationToken);
        var operationViews = await BuildOperationViewsAsync(operationRows, cancellationToken, resourceAssumptions);

        var laneResourceIds = operationViews.Select(x => x.ResourceId).Distinct().ToArray();
        var laneResources = laneResourceIds.Length == 0
            ? new Dictionary<Guid, Resource>()
            : (await db.Resources.AsNoTracking()
                    .Where(x => laneResourceIds.Contains(x.Id))
                    .ToListAsync(cancellationToken))
                .ToDictionary(x => x.Id);
        var stageIds = laneResources.Values.Select(x => x.ProcessStageId).Where(x => x != Guid.Empty).Distinct().ToArray();
        var stages = stageIds.Length == 0
            ? new Dictionary<Guid, ProcessStage>()
            : await db.ProcessStages.AsNoTracking()
                .Where(x => stageIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
        var areaIds = stages.Values.Select(x => x.PlantAreaId).OfType<Guid>().Distinct().ToArray();
        var areas = areaIds.Length == 0
            ? new Dictionary<Guid, PlantArea>()
            : await db.PlantAreas.AsNoTracking()
                .Where(x => areaIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
        var plantIds = laneResources.Values.Select(x => x.PlantId)
            .Concat(stages.Values.Select(x => x.PlantId))
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();
        var plants = plantIds.Length == 0
            ? new Dictionary<Guid, Plant>()
            : await db.Plants.AsNoTracking()
                .Where(x => plantIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
        var stageResourceRanks = stageIds.Length == 0
            ? new Dictionary<Guid, int>()
            : (await db.Resources.AsNoTracking()
                    .Where(x => stageIds.Contains(x.ProcessStageId))
                    .OrderBy(x => x.Code)
                    .ToArrayAsync(cancellationToken))
                .GroupBy(x => x.ProcessStageId)
                .SelectMany(group => group.Select((resource, index) => new { resource.Id, Index = index }))
                .ToDictionary(x => x.Id, x => x.Index);

        var lanes = operationViews
            .GroupBy(x => x.ResourceId)
            .Select(group =>
            {
                var ordered = group.OrderBy(x => x.StartUtc).ToArray();
                var resource = ordered[0];
                laneResources.TryGetValue(group.Key, out var master);
                resourceAssumptions.TryGetValue(group.Key, out var resourceAssumption);
                ProcessStage? stage = null;
                PlantArea? area = null;
                Plant? plant = null;
                if (master is not null)
                {
                    stages.TryGetValue(master.ProcessStageId, out stage);
                    if (stage?.PlantAreaId is { } areaId) areas.TryGetValue(areaId, out area);
                    var plantId = master.PlantId != Guid.Empty ? master.PlantId : stage?.PlantId ?? Guid.Empty;
                    if (plantId != Guid.Empty) plants.TryGetValue(plantId, out plant);
                }
                var displayOrder = HierarchyDisplayOrder(
                    area?.SequenceNumber,
                    stage?.SequenceNumber,
                    stageResourceRanks.GetValueOrDefault(group.Key));
                // A cumulative lane's busy time is the merged span of its blocks, not their sum (#35).
                var spans = ordered.Select(x => (Start: x.StartUtc, End: x.EndUtc)).ToArray();
                return new ScheduleResourceLaneView(
                    group.Key,
                    resource.ResourceCode,
                    resource.ResourceName,
                    resource.ProcessUnitType,
                    resourceAssumption?.OperatingState ?? resource.ResourceOperatingState,
                    Math.Round(ordered.Sum(x => Math.Max(0d, (x.EndUtc - x.StartUtc).TotalHours)), 2),
                    ordered,
                    resourceAssumption?.SchedulingMode ?? master?.SchedulingMode ?? ResourceSchedulingMode.Disjunctive,
                    Math.Round(ResourceCapacityModel.OccupiedHours(spans), 2),
                    ResourceCapacityModel.PeakConcurrency(spans),
                    resourceAssumption?.NominalConcurrentCapacity ?? master?.NominalConcurrentCapacity,
                    plant?.Id,
                    plant?.Code,
                    plant?.Name,
                    area?.Id,
                    area?.Code,
                    area?.Name,
                    stage?.Id,
                    stage?.Code,
                    stage?.Name,
                    displayOrder);
            })
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => ProcessOrder(x.ProcessUnitType))
            .ThenBy(x => x.ResourceCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var scheduleStart = operationViews.Count == 0 ? plan.HorizonStartUtc : operationViews.Min(x => x.StartUtc);
        var scheduleEnd = operationViews.Count == 0 ? plan.HorizonEndUtc : operationViews.Max(x => x.EndUtc);

        return new FiniteScheduleWorkspaceView(
            plan,
            scheduleStart,
            scheduleEnd,
            operationViews.Count,
            lanes.Length,
            lanes);
    }

    private async Task<IReadOnlyCollection<ScheduledProcessOperationView>> BuildOperationViewsAsync(
        IReadOnlyCollection<PlanOperationSnapshot> operations,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<Guid, ResourceSchedulingAssumption>? resourceAssumptions = null)
    {
        if (operations.Count == 0) return Array.Empty<ScheduledProcessOperationView>();
        var resourceIds = operations.Select(x => x.ResourceId).Distinct().ToArray();
        var resources = await db.Resources.AsNoTracking()
            .Where(x => resourceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return operations.Select(operation =>
        {
            resources.TryGetValue(operation.ResourceId, out var resource);
            resourceAssumptions?.TryGetValue(operation.ResourceId, out var resourceAssumption);
            return new ScheduledProcessOperationView(
                operation.Id,
                operation.PlanningKey,
                operation.SourceEntityId,
                operation.ProcessOperationType,
                operation.ResourceId,
                resourceAssumption?.ResourceCode ?? resource?.Code ?? operation.ResourceId.ToString("N")[..8],
                resource?.Name ?? resourceAssumption?.ResourceCode ?? "Unknown resource",
                resource?.ProcessUnitType ?? ProcessUnitType.Unknown,
                resourceAssumption?.OperatingState ?? resource?.OperatingState ?? ResourceOperatingState.Disabled,
                operation.StartUtc,
                operation.EndUtc,
                operation.QuantityMt,
                operation.GradeCode,
                operation.CrossSectionCode);
        }).ToArray();
    }

    private static int ProcessOrder(ProcessUnitType type) => type switch
    {
        ProcessUnitType.Eaf => 10,
        ProcessUnitType.Lrf => 20,
        ProcessUnitType.Vd => 30,
        ProcessUnitType.Ccm => 40,
        ProcessUnitType.ReheatingFurnace => 50,
        ProcessUnitType.HotRollingMill => 60,
        ProcessUnitType.ColdRollingMill => 70,
        ProcessUnitType.TmtWaterBox => 80,
        ProcessUnitType.CoolingBed => 90,
        ProcessUnitType.Shear => 100,
        ProcessUnitType.BundlingLine => 110,
        ProcessUnitType.Coiler => 120,
        ProcessUnitType.FinishingLine => 130,
        _ => 999
    };

    private static int HierarchyDisplayOrder(int? areaSequence, int? stageSequence, int resourceRank)
    {
        if (!areaSequence.HasValue || !stageSequence.HasValue) return int.MaxValue;
        var value = ((long)Math.Max(0, areaSequence.Value) * 1_000_000L) +
                    ((long)Math.Max(0, stageSequence.Value) * 1_000L) +
                    Math.Max(0, resourceRank);
        return (int)Math.Min(value, int.MaxValue - 1L);
    }
}
