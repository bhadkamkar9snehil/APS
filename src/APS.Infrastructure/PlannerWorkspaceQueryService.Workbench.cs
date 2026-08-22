using APS.Application;
using APS.Domain;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed partial class PlannerWorkspaceQueryService
{
    public async Task<PlanningWorkbenchView?> GetPlanningWorkbenchAsync(
        Guid? planVersionId = null,
        Guid? baselinePlanVersionId = null,
        CancellationToken cancellationToken = default)
    {
        var plan = planVersionId.HasValue
            ? await GetPlanContextAsync(planVersionId.Value, cancellationToken)
            : await GetCurrentPlanAsync(cancellationToken);
        if (plan is null) return null;

        var demand = await GetDemandSupplyAsync(plan.PlanVersionId, cancellationToken);
        var campaigns = await GetCampaignStudioAsync(plan.PlanVersionId, cancellationToken);
        var schedule = await GetFiniteScheduleAsync(plan.PlanVersionId, cancellationToken);
        var material = await GetMaterialFlowAsync(plan.PlanVersionId, cancellationToken);
        if (demand is null || campaigns is null || schedule is null || material is null) return null;

        PlanContextView? baseline = null;
        FiniteScheduleWorkspaceView? baselineSchedule = null;
        PlanComparisonWorkspaceView? comparison = null;
        var effectiveBaselineId = baselinePlanVersionId ?? plan.ParentPlanVersionId;
        if (effectiveBaselineId.HasValue && effectiveBaselineId.Value != plan.PlanVersionId)
        {
            baseline = await GetPlanContextAsync(effectiveBaselineId.Value, cancellationToken);
            if (baseline is not null)
            {
                baselineSchedule = await GetFiniteScheduleAsync(effectiveBaselineId.Value, cancellationToken);
                comparison = await GetPlanComparisonAsync(
                    effectiveBaselineId.Value,
                    plan.PlanVersionId,
                    cancellationToken);
            }
        }

        var exceptions = BuildExceptions(plan, demand, schedule, material);
        var operationDetails = await BuildOperationDetailsAsync(plan.PlanVersionId, campaigns, cancellationToken);
        var dependencyLinks = BuildDependencyLinks(schedule, operationDetails);
        var calendarIntervals = await BuildCalendarIntervalsAsync(plan, schedule, cancellationToken);
        var baselinePlacements = baselineSchedule is null
            ? Array.Empty<PlanningBaselinePlacementView>()
            : BuildBaselinePlacements(baselineSchedule);
        var capacityBuckets = await BuildCapacityBucketsAsync(
            plan,
            schedule,
            calendarIntervals,
            cancellationToken);
        var lateDemand = demand.Rows.Count(row => row.RequiredDate < schedule.ScheduleEndUtc);
        var queue = new PlanningQueueView(
            demand.Rows.Count,
            demand.Rows.Count(row => row.Status != ProductionOrderStatus.Planned &&
                                     row.Status != ProductionOrderStatus.Firmed &&
                                     row.Status != ProductionOrderStatus.Released),
            lateDemand,
            campaigns.CampaignCount,
            material.Pools.Count,
            exceptions.Count(x => x.Severity == PlanningWorkbenchExceptionSeverity.Critical),
            exceptions.Count(x => x.Severity == PlanningWorkbenchExceptionSeverity.Warning));

        return new PlanningWorkbenchView(
            plan,
            baseline,
            demand,
            campaigns,
            schedule,
            material,
            comparison,
            queue,
            exceptions,
            operationDetails,
            dependencyLinks,
            calendarIntervals,
            baselinePlacements,
            capacityBuckets);
    }

    private static IReadOnlyCollection<PlanningDependencyLinkView> BuildDependencyLinks(
        FiniteScheduleWorkspaceView schedule,
        IReadOnlyCollection<PlanningOperationWorkbenchDetail> details)
    {
        var operations = schedule.ResourceLanes
            .SelectMany(x => x.Operations)
            .ToDictionary(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase);
        var result = new List<PlanningDependencyLinkView>();
        foreach (var successorDetail in details)
        {
            if (!operations.TryGetValue(successorDetail.PlanningKey, out var successor)) continue;
            foreach (var predecessorKey in successorDetail.PredecessorPlanningKeys.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!operations.TryGetValue(predecessorKey, out var predecessor)) continue;
                result.Add(new PlanningDependencyLinkView(
                    predecessor.OperationSnapshotId,
                    predecessor.PlanningKey,
                    successor.OperationSnapshotId,
                    successor.PlanningKey,
                    PlanningDependencyType.FinishStart,
                    PlanningDependencyCategory.Routing,
                    null,
                    (int)Math.Round((successor.StartUtc - predecessor.EndUtc).TotalMinutes)));
            }
        }

        return result
            .OrderBy(x => x.SuccessorPlanningKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PredecessorPlanningKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyCollection<PlanningResourceCalendarIntervalView>> BuildCalendarIntervalsAsync(
        PlanContextView plan,
        FiniteScheduleWorkspaceView schedule,
        CancellationToken cancellationToken)
    {
        var resourceIds = schedule.ResourceLanes.Select(x => x.ResourceId).ToArray();
        if (resourceIds.Length == 0) return Array.Empty<PlanningResourceCalendarIntervalView>();

        return await db.ResourceCalendars.AsNoTracking()
            .Where(x => resourceIds.Contains(x.ResourceId) &&
                        x.End > plan.HorizonStartUtc &&
                        x.Start < plan.HorizonEndUtc)
            .OrderBy(x => x.ResourceId)
            .ThenBy(x => x.Start)
            .Select(x => new PlanningResourceCalendarIntervalView(
                x.ResourceId,
                x.Start,
                x.End,
                x.IsAvailable,
                x.CapacityFactorPct,
                x.ReasonCode,
                "ResourceCalendar"))
            .ToArrayAsync(cancellationToken);
    }

    private static IReadOnlyCollection<PlanningBaselinePlacementView> BuildBaselinePlacements(
        FiniteScheduleWorkspaceView baselineSchedule) =>
        baselineSchedule.ResourceLanes
            .SelectMany(lane => lane.Operations.Select(operation => new PlanningBaselinePlacementView(
                baselineSchedule.Plan.PlanVersionId,
                operation.OperationSnapshotId,
                operation.PlanningKey,
                lane.ResourceId,
                lane.ResourceCode,
                lane.ResourceName,
                lane.ProcessUnitType,
                lane.OperatingState,
                lane.SchedulingMode,
                operation.StartUtc,
                operation.EndUtc,
                operation.ProcessOperationType,
                operation.GradeCode,
                operation.CrossSectionCode,
                lane.PlantId,
                lane.PlantCode,
                lane.PlantName,
                lane.AreaId,
                lane.AreaCode,
                lane.AreaName,
                lane.ProcessStageId,
                lane.ProcessStageCode,
                lane.ProcessStageName,
                lane.DisplayOrder)))
            .OrderBy(x => x.StartUtc)
            .ThenBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private async Task<IReadOnlyCollection<PlanningCapacityBucketView>> BuildCapacityBucketsAsync(
        PlanContextView plan,
        FiniteScheduleWorkspaceView schedule,
        IReadOnlyCollection<PlanningResourceCalendarIntervalView> calendars,
        CancellationToken cancellationToken)
    {
        if (schedule.ResourceLanes.Count == 0) return Array.Empty<PlanningCapacityBucketView>();
        var resourceIds = schedule.ResourceLanes.Select(x => x.ResourceId).ToArray();
        var resources = await db.Resources.AsNoTracking()
            .Where(x => resourceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var calendarsByResource = calendars
            .GroupBy(x => x.ResourceId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var bucketStart = new DateTime(
            plan.HorizonStartUtc.Year,
            plan.HorizonStartUtc.Month,
            plan.HorizonStartUtc.Day,
            plan.HorizonStartUtc.Hour,
            0,
            0,
            DateTimeKind.Utc);
        var result = new List<PlanningCapacityBucketView>();

        foreach (var lane in schedule.ResourceLanes)
        {
            resources.TryGetValue(lane.ResourceId, out var resource);
            calendarsByResource.TryGetValue(lane.ResourceId, out var laneCalendars);
            for (var start = bucketStart; start < plan.HorizonEndUtc; start = start.AddHours(1))
            {
                var end = start.AddHours(1) < plan.HorizonEndUtc ? start.AddHours(1) : plan.HorizonEndUtc;
                if (end <= plan.HorizonStartUtc) continue;
                var effectiveStart = start < plan.HorizonStartUtc ? plan.HorizonStartUtc : start;
                var baseFactor = Math.Clamp(resource?.CapacityFactorPct ?? 100m, 0m, 100m) / 100m;
                var capacityWindow = CapacityWindow(
                    effectiveStart,
                    end,
                    lane.OperatingState,
                    baseFactor,
                    laneCalendars ?? Array.Empty<PlanningResourceCalendarIntervalView>());
                var unavailableMinutes = capacityWindow.UnavailableMinutes;
                var availableClockMinutes = capacityWindow.AvailableMinutes;
                var capacityMultiplier = lane.SchedulingMode == ResourceSchedulingMode.Cumulative
                    ? (double)Math.Max(0m, lane.NominalConcurrentCapacity ?? 0m)
                    : 1d;
                var availableMinutes = availableClockMinutes * capacityMultiplier;
                var processingMinutes = lane.SchedulingMode == ResourceSchedulingMode.Cumulative
                    ? lane.Operations.Sum(operation =>
                        OverlapMinutes(operation.StartUtc, operation.EndUtc, effectiveStart, end) *
                        (double)CapacityDemand(resource, operation.QuantityMt))
                    : MergedOverlapMinutes(
                        lane.Operations.Select(x => (x.StartUtc, x.EndUtc)),
                        effectiveStart,
                        end);
                var occupancyRatio = availableMinutes > 0d
                    ? (decimal)(processingMinutes / availableMinutes)
                    : processingMinutes > 0d ? 1m : 0m;

                result.Add(new PlanningCapacityBucketView(
                    lane.ResourceId,
                    effectiveStart,
                    end,
                    Math.Round(availableMinutes, 3),
                    Math.Round(processingMinutes, 3),
                    Math.Round(unavailableMinutes, 3),
                    decimal.Round(occupancyRatio, 4),
                    CapacityBasis(resource),
                    lane.SchedulingMode));
            }
        }

        return result;
    }

    private static (double AvailableMinutes, double UnavailableMinutes) CapacityWindow(
        DateTime rangeStart,
        DateTime rangeEnd,
        ResourceOperatingState operatingState,
        decimal resourceFactor,
        IReadOnlyCollection<PlanningResourceCalendarIntervalView> calendars)
    {
        var wallClockMinutes = (rangeEnd - rangeStart).TotalMinutes;
        if (operatingState != ResourceOperatingState.Available) return (0d, wallClockMinutes);

        var overlapping = calendars
            .Where(x => x.EndUtc > rangeStart && x.StartUtc < rangeEnd)
            .ToArray();
        var boundaries = overlapping
            .SelectMany(x => new[]
            {
                x.StartUtc > rangeStart ? x.StartUtc : rangeStart,
                x.EndUtc < rangeEnd ? x.EndUtc : rangeEnd
            })
            .Append(rangeStart)
            .Append(rangeEnd)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        var available = 0d;
        var unavailable = 0d;
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var segmentStart = boundaries[index];
            var segmentEnd = boundaries[index + 1];
            if (segmentEnd <= segmentStart) continue;
            var segmentMinutes = (segmentEnd - segmentStart).TotalMinutes;
            var active = overlapping
                .Where(x => x.StartUtc < segmentEnd && x.EndUtc > segmentStart)
                .ToArray();
            if (active.Any(x => !x.IsAvailable))
            {
                unavailable += segmentMinutes;
                continue;
            }

            var calendarFactor = active
                .Where(x => x.IsAvailable && x.CapacityFactorPct.HasValue)
                .Select(x => Math.Clamp(x.CapacityFactorPct!.Value, 0m, 100m) / 100m)
                .DefaultIfEmpty(1m)
                .Min();
            available += segmentMinutes * (double)Math.Min(resourceFactor, calendarFactor);
        }

        return (available, unavailable);
    }

    private static decimal CapacityDemand(Resource? resource, decimal quantityMt) => resource?.CapacityBasis switch
    {
        ResourceCapacityBasis.MassEquivalentMt => Math.Max(0m, quantityMt),
        _ => 1m
    };

    private static PlanningCapacityBasis CapacityBasis(Resource? resource) => resource?.CapacityBasis switch
    {
        ResourceCapacityBasis.Slots => PlanningCapacityBasis.Slots,
        ResourceCapacityBasis.MassEquivalentMt => PlanningCapacityBasis.MassEquivalentMt,
        ResourceCapacityBasis.Positions => PlanningCapacityBasis.Positions,
        _ => PlanningCapacityBasis.MachineTime
    };

    private static double OverlapMinutes(DateTime start, DateTime end, DateTime rangeStart, DateTime rangeEnd)
    {
        var overlapStart = start > rangeStart ? start : rangeStart;
        var overlapEnd = end < rangeEnd ? end : rangeEnd;
        return overlapEnd > overlapStart ? (overlapEnd - overlapStart).TotalMinutes : 0d;
    }

    private static double MergedOverlapMinutes(
        IEnumerable<(DateTime Start, DateTime End)> spans,
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        var clipped = spans
            .Select(x => (Start: x.Start > rangeStart ? x.Start : rangeStart,
                          End: x.End < rangeEnd ? x.End : rangeEnd))
            .Where(x => x.End > x.Start)
            .OrderBy(x => x.Start)
            .ToArray();
        if (clipped.Length == 0) return 0d;

        var total = TimeSpan.Zero;
        var currentStart = clipped[0].Start;
        var currentEnd = clipped[0].End;
        foreach (var span in clipped.Skip(1))
        {
            if (span.Start <= currentEnd)
            {
                if (span.End > currentEnd) currentEnd = span.End;
                continue;
            }
            total += currentEnd - currentStart;
            currentStart = span.Start;
            currentEnd = span.End;
        }
        total += currentEnd - currentStart;
        return total.TotalMinutes;
    }

    private async Task<IReadOnlyCollection<PlanningOperationWorkbenchDetail>> BuildOperationDetailsAsync(
        Guid planVersionId,
        CampaignStudioView campaigns,
        CancellationToken cancellationToken)
    {
        var operations = await db.PlanOperationSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId)
            .OrderBy(x => x.StartUtc)
            .ToArrayAsync(cancellationToken);
        var options = await db.PlanOperationResourceOptionSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId)
            .OrderBy(x => x.AssignmentPenalty)
            .ThenBy(x => x.ResourceId)
            .ToArrayAsync(cancellationToken);
        var resourceIds = options.Select(x => x.ResourceId)
            .Concat(operations.Select(x => x.ResourceId))
            .Distinct()
            .ToArray();
        var resources = await db.Resources.AsNoTracking()
            .Where(x => resourceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var optionsByKey = options
            .GroupBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyCollection<PlanningOperationResourceOptionView>)x.Select(option =>
                {
                    resources.TryGetValue(option.ResourceId, out var resource);
                    return new PlanningOperationResourceOptionView(
                        option.ResourceId,
                        resource?.Code ?? "Unavailable resource",
                        resource?.Name ?? "Unavailable resource",
                        option.DurationMinutes,
                        option.AssignmentPenalty,
                        option.WasSelected,
                        option.EligibilityBasisCode);
                }).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var heatLookup = campaigns.Campaigns
            .SelectMany(campaign => campaign.Heats.Select(heat => new { Campaign = campaign, Heat = heat }))
            .ToDictionary(x => x.Heat.CampaignHeatId);

        return operations.Select(operation =>
        {
            optionsByKey.TryGetValue(operation.PlanningKey, out var resourceOptions);
            heatLookup.TryGetValue(operation.SourceEntityId, out var heat);
            return new PlanningOperationWorkbenchDetail(
                operation.Id,
                operation.PlanningKey,
                operation.SourceEntityId,
                operation.AssignmentCommitmentState,
                operation.ExecutionStatus,
                operation.ActualStartUtc,
                operation.ActualEndUtc,
                operation.ActualQuantityMt,
                DeserializeKeys(operation.PredecessorPlanningKeysJson),
                resourceOptions ?? Array.Empty<PlanningOperationResourceOptionView>(),
                heat?.Campaign.CampaignNumber,
                heat?.Heat.SequenceNumber,
                // Heat-scoped, not campaign-scoped (#UI-depth): a campaign pools every order sharing its
                // grade/section/route across all its heats, so attributing every heat's operations to the
                // whole campaign's order list makes distinct heats indistinguishable in the workbench -
                // every block picks the same alphabetically-first order regardless of which heat it is.
                heat?.Heat.Allocations
                    .Select(x => x.ProductionOrderNumber)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? Array.Empty<string>());
        }).ToArray();
    }

    private static IReadOnlyCollection<string> DeserializeKeys(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyCollection<PlanningWorkbenchException> BuildExceptions(
        PlanContextView plan,
        DemandSupplyView demand,
        FiniteScheduleWorkspaceView schedule,
        MaterialFlowWorkspaceView material)
    {
        var result = new List<PlanningWorkbenchException>();

        if (plan.Status == PlanVersionStatus.Failed)
        {
            result.Add(new PlanningWorkbenchException(
                "PLAN-INFEASIBLE",
                PlanningWorkbenchExceptionKind.InfeasiblePlan,
                PlanningWorkbenchExceptionSeverity.Critical,
                "Plan is infeasible",
                plan.Reason ?? "The solver could not produce a feasible schedule.",
                new PlannerEntityRef(PlannerEntityType.PlanVersion, plan.PlanVersionId, plan.VersionNumber)));
        }

        foreach (var row in demand.Rows.Where(x => x.UncoveredQuantityMt > 0m))
        {
            result.Add(new PlanningWorkbenchException(
                $"DEMAND-UNCOVERED-{row.ProductionOrderNumber}",
                PlanningWorkbenchExceptionKind.UncoveredDemand,
                PlanningWorkbenchExceptionSeverity.Critical,
                $"{row.ProductionOrderNumber} has uncovered demand",
                $"{row.UncoveredQuantityMt:0.##} MT is not covered by stock, supply, or fresh production.",
                new PlannerEntityRef(PlannerEntityType.ProductionOrder, row.ProductionOrderId, row.ProductionOrderNumber)));
        }

        foreach (var salesOrder in demand.EffectiveSalesOrders().Where(x => x.PlannerAttentionRequired))
        {
            result.Add(new PlanningWorkbenchException(
                $"DEMAND-ATTENTION-{salesOrder.SalesOrderNumber}-{salesOrder.SalesOrderItemNumber}",
                PlanningWorkbenchExceptionKind.DemandAttention,
                PlanningWorkbenchExceptionSeverity.Warning,
                $"{salesOrder.SalesOrderNumber}/{salesOrder.SalesOrderItemNumber} needs attention",
                salesOrder.ReasonCode ?? "The reconciled sales-order requirement needs a planner decision.",
                new PlannerEntityRef(PlannerEntityType.SalesOrder, salesOrder.SalesOrderId, $"{salesOrder.SalesOrderNumber}/{salesOrder.SalesOrderItemNumber}")));
        }

        foreach (var lane in schedule.ResourceLanes.Where(x => x.OperatingState != ResourceOperatingState.Available))
        {
            result.Add(new PlanningWorkbenchException(
                $"RESOURCE-{lane.ResourceCode}-{lane.OperatingState}",
                PlanningWorkbenchExceptionKind.ResourceUnavailable,
                PlanningWorkbenchExceptionSeverity.Critical,
                $"{lane.ResourceCode} is {lane.OperatingState}",
                $"{lane.Operations.Count} scheduled operation(s) use this resource.",
                new PlannerEntityRef(PlannerEntityType.Resource, lane.ResourceId, lane.ResourceCode)));
        }

        foreach (var pool in material.Pools.Where(x => x.ClosingBalanceMt < 0m))
        {
            result.Add(new PlanningWorkbenchException(
                $"MATERIAL-{pool.MaterialPoolKey}",
                PlanningWorkbenchExceptionKind.MaterialShortage,
                PlanningWorkbenchExceptionSeverity.Critical,
                $"{pool.GradeCode} / {pool.CrossSectionCode} is short",
                $"Closing balance is {pool.ClosingBalanceMt:0.##} MT.",
                new PlannerEntityRef(PlannerEntityType.MaterialSupply, Guid.Empty, $"{pool.GradeCode}/{pool.CrossSectionCode}")));
        }

        return result;
    }
}

internal static class DemandSupplyViewExtensions
{
    public static IReadOnlyCollection<SalesOrderDemandRowView> EffectiveSalesOrders(this DemandSupplyView view) =>
        view.SalesOrders ?? Array.Empty<SalesOrderDemandRowView>();
}
