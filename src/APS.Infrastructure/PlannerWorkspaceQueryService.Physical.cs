using APS.Application;
using APS.Domain;
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

        var operationRows = await db.PlanOperationSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == plan.PlanVersionId)
            .OrderBy(x => x.StartUtc)
            .ToListAsync(cancellationToken);
        var operationViews = await BuildOperationViewsAsync(operationRows, cancellationToken);

        var lanes = operationViews
            .GroupBy(x => x.ResourceId)
            .Select(group =>
            {
                var ordered = group.OrderBy(x => x.StartUtc).ToArray();
                var resource = ordered[0];
                return new ScheduleResourceLaneView(
                    group.Key,
                    resource.ResourceCode,
                    resource.ResourceName,
                    resource.ProcessUnitType,
                    resource.ResourceOperatingState,
                    Math.Round(ordered.Sum(x => Math.Max(0d, (x.EndUtc - x.StartUtc).TotalHours)), 2),
                    ordered);
            })
            .OrderBy(x => ProcessOrder(x.ProcessUnitType))
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
        CancellationToken cancellationToken)
    {
        if (operations.Count == 0) return Array.Empty<ScheduledProcessOperationView>();
        var resourceIds = operations.Select(x => x.ResourceId).Distinct().ToArray();
        var resources = await db.Resources.AsNoTracking()
            .Where(x => resourceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return operations.Select(operation =>
        {
            resources.TryGetValue(operation.ResourceId, out var resource);
            return new ScheduledProcessOperationView(
                operation.Id,
                operation.PlanningKey,
                operation.SourceEntityId,
                operation.ProcessOperationType,
                operation.ResourceId,
                resource?.Code ?? operation.ResourceId.ToString("N")[..8],
                resource?.Name ?? "Unknown resource",
                resource?.ProcessUnitType ?? ProcessUnitType.Unknown,
                resource?.OperatingState ?? ResourceOperatingState.Disabled,
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
}
