using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

/// <summary>
/// Builds the production release exclusively from immutable Plan Version snapshots. The caller
/// supplies only the Plan Version identity, so released WOs cannot drift from the approved plan.
/// </summary>
public sealed class PersistedPlanReleaseService(
    ApsDbContext db,
    IPlanReleaseRepository releaseRepository) : IPersistedPlanReleaseService
{
    public async Task<PlanRelease> ReleaseAsync(
        Guid planVersionId,
        CancellationToken cancellationToken = default)
    {
        var version = await db.PlanVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == planVersionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Plan Version {planVersionId} was not found.");

        if (version.IsReleased)
            return await LoadExistingReleaseAsync(planVersionId, cancellationToken);

        var state = await db.PlanVersionStates
            .AsNoTracking()
            .SingleAsync(x => x.PlanVersionId == planVersionId, cancellationToken);
        if (state.Status != PlanVersionStatus.Feasible)
            throw new InvalidOperationException(
                $"Plan Version {version.VersionNumber} is {state.Status}; only a feasible persisted plan can be released.");

        var productionOrders = await db.PlanProductionOrderSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId)
            .ToDictionaryAsync(x => x.ProductionOrderId, cancellationToken);
        var campaigns = await db.PlanCampaignSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId)
            .OrderBy(x => x.RequiredDate)
            .ThenBy(x => x.CampaignNumber)
            .ToArrayAsync(cancellationToken);
        var campaignAllocations = await db.PlanCampaignAllocationSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId)
            .ToArrayAsync(cancellationToken);
        var gradeSequences = await db.PlanCampaignGradeSequenceSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId)
            .OrderBy(x => x.SequenceNumber)
            .ToArrayAsync(cancellationToken);
        var heats = await db.PlanHeatSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId)
            .ToArrayAsync(cancellationToken);
        var rollingPlans = await db.PlanRollingPlanSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId)
            .OrderBy(x => x.SequenceNumber)
            .ToArrayAsync(cancellationToken);
        var rollingAllocations = await db.PlanRollingPlanAllocationSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId)
            .ToArrayAsync(cancellationToken);
        var routePlans = await db.PlanRouteOperationSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId)
            .OrderBy(x => x.SequenceNumber)
            .ToArrayAsync(cancellationToken);
        var routeAllocations = await db.PlanRouteOperationAllocationSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId)
            .ToArrayAsync(cancellationToken);
        var operations = await db.PlanOperationSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId)
            .OrderBy(x => x.StartUtc)
            .ToArrayAsync(cancellationToken);

        var workOrders = new List<WorkOrder>();
        var scheduledOperations = new List<ScheduledOperation>();
        var planSuffix = planVersionId.ToString("N")[..6].ToUpperInvariant();

        BuildSteelmakingAndCasting(
            planVersionId,
            planSuffix,
            campaigns,
            campaignAllocations,
            gradeSequences,
            heats,
            productionOrders,
            operations,
            workOrders,
            scheduledOperations);
        BuildRolling(
            planVersionId,
            rollingPlans,
            rollingAllocations,
            productionOrders,
            operations,
            workOrders,
            scheduledOperations);
        BuildConfiguredRoute(
            planVersionId,
            routePlans,
            routeAllocations,
            productionOrders,
            operations,
            workOrders,
            scheduledOperations);

        if (operations.Length > 0 && scheduledOperations.Count == 0)
            throw new InvalidOperationException(
                "The persisted plan contains scheduled operations but no releaseable production structure. Plan Version snapshot persistence is incomplete.");

        return await releaseRepository.PersistAsync(
            new PlanRelease(planVersionId, workOrders, scheduledOperations),
            cancellationToken);
    }

    private async Task<PlanRelease> LoadExistingReleaseAsync(
        Guid planVersionId,
        CancellationToken cancellationToken)
    {
        var operations = await db.ScheduledOperations.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId)
            .OrderBy(x => x.Start)
            .ToArrayAsync(cancellationToken);
        var workOrderIds = operations.Select(x => x.WorkOrderId).Distinct().ToArray();
        var workOrders = await db.WorkOrders.AsNoTracking()
            .Where(x => workOrderIds.Contains(x.Id))
            .Include(x => x.Allocations)
            .OrderBy(x => x.PlannedStart)
            .ToArrayAsync(cancellationToken);

        if (workOrderIds.Length > 0 && workOrders.Length == 0)
            throw new InvalidOperationException("Plan Version is marked released but its persisted Work Orders cannot be loaded.");

        return new PlanRelease(planVersionId, workOrders, operations);
    }

    private static void BuildSteelmakingAndCasting(
        Guid planVersionId,
        string planSuffix,
        IReadOnlyCollection<PlanCampaignSnapshot> campaigns,
        IReadOnlyCollection<PlanCampaignAllocationSnapshot> allocations,
        IReadOnlyCollection<PlanCampaignGradeSequenceSnapshot> gradeSequences,
        IReadOnlyCollection<PlanHeatSnapshot> heats,
        IReadOnlyDictionary<Guid, PlanProductionOrderSnapshot> productionOrders,
        IReadOnlyCollection<PlanOperationSnapshot> operations,
        ICollection<WorkOrder> workOrders,
        ICollection<ScheduledOperation> scheduledOperations)
    {
        foreach (var campaign in campaigns)
        {
            var campaignAllocations = allocations.Where(x => x.CampaignId == campaign.CampaignId).ToArray();
            foreach (var gradeSequence in gradeSequences
                         .Where(x => x.CampaignId == campaign.CampaignId)
                         .OrderBy(x => x.SequenceNumber))
            {
                if (gradeSequence.PlannedQuantityMt <= 0m) continue;

                var matchingAllocations = campaignAllocations
                    .Where(x => x.FreshSteelQuantityMt > 0m &&
                                productionOrders.TryGetValue(x.ProductionOrderId, out var po) &&
                                string.Equals(po.GradeCode, gradeSequence.GradeCode, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (matchingAllocations.Length == 0) continue;

                var heatIds = heats
                    .Where(x => x.CampaignId == campaign.CampaignId &&
                                string.Equals(x.GradeCode, gradeSequence.GradeCode, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.CampaignHeatId)
                    .ToHashSet();
                var heatOperations = operations.Where(x => heatIds.Contains(x.SourceEntityId)).ToArray();
                var steelmaking = heatOperations
                    .Where(x => x.ProcessOperationType is ProcessOperationType.Eaf or ProcessOperationType.Lrf or ProcessOperationType.Vd)
                    .OrderBy(x => x.StartUtc)
                    .ToArray();
                var casting = heatOperations
                    .Where(x => x.ProcessOperationType == ProcessOperationType.Ccm || x.OperationType == PlanOperationType.Casting)
                    .OrderBy(x => x.StartUtc)
                    .ToArray();
                var materialCodes = matchingAllocations
                    .Select(x => productionOrders[x.ProductionOrderId].MaterialCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (steelmaking.Length > 0)
                {
                    var wo = NewWorkOrder(
                        $"SMS-{campaign.CampaignNumber}-{gradeSequence.SequenceNumber:00}-{planSuffix}",
                        WorkOrderType.Steelmaking,
                        campaign.CampaignId,
                        steelmaking,
                        materialCodes,
                        gradeSequence.GradeCode,
                        campaign.CasterSectionCode,
                        gradeSequence.PlannedQuantityMt);
                    AddAllocations(wo, matchingAllocations.Select(x => (x.ProductionOrderId, x.FreshSteelQuantityMt)));
                    workOrders.Add(wo);
                    AddScheduledOperations(planVersionId, wo.Id, steelmaking, scheduledOperations);
                }

                if (casting.Length > 0)
                {
                    var wo = NewWorkOrder(
                        $"CCM-{campaign.CampaignNumber}-{gradeSequence.SequenceNumber:00}-{planSuffix}",
                        WorkOrderType.Casting,
                        campaign.CampaignId,
                        casting,
                        materialCodes,
                        gradeSequence.GradeCode,
                        campaign.CasterSectionCode,
                        matchingAllocations.Sum(x => x.FreshSteelQuantityMt));
                    AddAllocations(wo, matchingAllocations.Select(x => (x.ProductionOrderId, x.FreshSteelQuantityMt)));
                    workOrders.Add(wo);
                    AddScheduledOperations(planVersionId, wo.Id, casting, scheduledOperations);
                }
            }
        }
    }

    private static void BuildRolling(
        Guid planVersionId,
        IReadOnlyCollection<PlanRollingPlanSnapshot> rollingPlans,
        IReadOnlyCollection<PlanRollingPlanAllocationSnapshot> allocations,
        IReadOnlyDictionary<Guid, PlanProductionOrderSnapshot> productionOrders,
        IReadOnlyCollection<PlanOperationSnapshot> operations,
        ICollection<WorkOrder> workOrders,
        ICollection<ScheduledOperation> scheduledOperations)
    {
        foreach (var plan in rollingPlans)
        {
            var planOperations = operations
                .Where(x => x.SourceEntityId == plan.RollingPlanId &&
                            x.ProcessOperationType is ProcessOperationType.Reheat or ProcessOperationType.HotRoll)
                .OrderBy(x => x.StartUtc)
                .ToArray();
            if (planOperations.Length == 0) continue;

            var planAllocations = allocations.Where(x => x.RollingPlanId == plan.RollingPlanId).ToArray();
            var materialCodes = planAllocations
                .Where(x => productionOrders.ContainsKey(x.ProductionOrderId))
                .Select(x => productionOrders[x.ProductionOrderId].MaterialCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var hotRollOperations = planOperations.Where(x => x.ProcessOperationType == ProcessOperationType.HotRoll).ToArray();
            var wo = NewWorkOrder(
                $"RM-{plan.RollingPlanId:N}",
                WorkOrderType.HotRolling,
                plan.CampaignId,
                hotRollOperations.Length > 0 ? hotRollOperations : planOperations,
                materialCodes,
                plan.GradeCode,
                plan.OutputCrossSectionCode,
                plan.PlannedQuantityMt);
            AddAllocations(wo, planAllocations.Select(x => (x.ProductionOrderId, x.PlannedQuantityMt)));
            workOrders.Add(wo);
            AddScheduledOperations(planVersionId, wo.Id, planOperations, scheduledOperations);
        }
    }

    private static void BuildConfiguredRoute(
        Guid planVersionId,
        IReadOnlyCollection<PlanRouteOperationSnapshot> routePlans,
        IReadOnlyCollection<PlanRouteOperationAllocationSnapshot> allocations,
        IReadOnlyDictionary<Guid, PlanProductionOrderSnapshot> productionOrders,
        IReadOnlyCollection<PlanOperationSnapshot> operations,
        ICollection<WorkOrder> workOrders,
        ICollection<ScheduledOperation> scheduledOperations)
    {
        foreach (var plan in routePlans)
        {
            var planOperations = operations
                .Where(x => x.SourceEntityId == plan.RouteOperationPlanId &&
                            x.ProcessOperationType == plan.ProcessOperationType)
                .OrderBy(x => x.StartUtc)
                .ToArray();
            if (planOperations.Length == 0) continue;

            var planAllocations = allocations.Where(x => x.RouteOperationPlanId == plan.RouteOperationPlanId).ToArray();
            var materialCodes = planAllocations
                .Where(x => productionOrders.ContainsKey(x.ProductionOrderId))
                .Select(x => productionOrders[x.ProductionOrderId].MaterialCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var campaignIds = planAllocations.Select(x => x.CampaignId).Distinct().ToArray();
            var prefix = plan.ReleaseWorkOrderType switch
            {
                WorkOrderType.ColdRolling => "CRM",
                WorkOrderType.Finishing => "FIN",
                WorkOrderType.HotRolling => "RM",
                _ => "PROC"
            };

            var wo = NewWorkOrder(
                $"{prefix}-{plan.RouteOperationPlanId:N}",
                plan.ReleaseWorkOrderType,
                campaignIds.Length == 1 ? campaignIds[0] : null,
                planOperations,
                materialCodes,
                plan.GradeCode,
                plan.OutputCrossSectionCode,
                plan.PlannedQuantityMt);
            AddAllocations(wo, planAllocations.Select(x => (x.ProductionOrderId, x.PlannedQuantityMt)));
            workOrders.Add(wo);
            AddScheduledOperations(planVersionId, wo.Id, planOperations, scheduledOperations);
        }
    }

    private static WorkOrder NewWorkOrder(
        string number,
        WorkOrderType type,
        Guid? campaignId,
        IReadOnlyCollection<PlanOperationSnapshot> operations,
        IReadOnlyCollection<string> materialCodes,
        string gradeCode,
        string crossSectionCode,
        decimal plannedQuantityMt)
    {
        var resources = operations.Select(EffectiveResourceId).Distinct().ToArray();
        return new WorkOrder
        {
            WorkOrderNumber = number,
            WorkOrderType = type,
            CampaignId = campaignId,
            ResourceId = resources.Length == 1 ? resources[0] : null,
            MaterialCode = materialCodes.Count == 1 ? materialCodes.Single() : "MULTI",
            GradeCode = gradeCode,
            CrossSectionCode = crossSectionCode,
            PlannedQuantityMt = plannedQuantityMt,
            PlannedStart = operations.Count == 0 ? null : operations.Min(x => x.StartUtc),
            PlannedEnd = operations.Count == 0 ? null : operations.Max(x => x.EndUtc),
            Status = WorkOrderStatus.Planned
        };
    }

    private static void AddAllocations(
        WorkOrder workOrder,
        IEnumerable<(Guid ProductionOrderId, decimal QuantityMt)> allocations)
    {
        foreach (var allocation in allocations.Where(x => x.QuantityMt > 0m))
        {
            workOrder.Allocations.Add(new WorkOrderAllocation
            {
                WorkOrderId = workOrder.Id,
                WorkOrder = workOrder,
                ProductionOrderId = allocation.ProductionOrderId,
                PlannedQuantityMt = allocation.QuantityMt
            });
        }
    }

    private static void AddScheduledOperations(
        Guid planVersionId,
        Guid workOrderId,
        IEnumerable<PlanOperationSnapshot> operations,
        ICollection<ScheduledOperation> output)
    {
        foreach (var operation in operations)
        {
            output.Add(new ScheduledOperation
            {
                PlanVersionId = planVersionId,
                WorkOrderId = workOrderId,
                ResourceId = EffectiveResourceId(operation),
                ProcessOperationType = operation.ProcessOperationType,
                PlanningKey = operation.PlanningKey,
                Start = operation.StartUtc,
                End = operation.EndUtc,
                IsFrozen = operation.AssignmentCommitmentState != OperationAssignmentCommitmentState.Flexible
            });
        }
    }

    private static Guid EffectiveResourceId(PlanOperationSnapshot operation) =>
        operation.ActualResourceId ?? operation.CommittedResourceId ?? operation.ResourceId;
}
