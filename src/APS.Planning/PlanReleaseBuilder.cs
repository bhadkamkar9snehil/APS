using APS.Application;
using APS.Domain;

namespace APS.Planning;

public sealed class PlanReleaseBuilder : IPlanReleaseBuilder
{
    public PlanRelease Build(PlanReleaseBuildRequest request)
    {
        if (!request.Schedule.IsFeasible)
            throw new InvalidOperationException("Cannot release an infeasible schedule.");

        var workOrders = new List<WorkOrder>();
        var scheduledOperations = new List<ScheduledOperation>();
        var assignmentsBySource = request.Schedule.Assignments
            .GroupBy(a => a.SourceEntityId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.StartUtc).ToArray());
        var taskById = request.ProductionStructure.SchedulingTasks.ToDictionary(x => x.TaskId);
        var planningKeyByTask = PlanningTaskIdentityService.Build(request.ProductionStructure)
            .ToDictionary(x => x.TaskId, x => x.PlanningKey);

        BuildSteelmakingAndCastingWorkOrders(request, assignmentsBySource, taskById, planningKeyByTask, workOrders, scheduledOperations);
        BuildRollingWorkOrders(request, assignmentsBySource, taskById, planningKeyByTask, workOrders, scheduledOperations);
        BuildConfiguredRouteWorkOrders(request, assignmentsBySource, taskById, planningKeyByTask, workOrders, scheduledOperations);

        return new PlanRelease(request.PlanVersionId, workOrders, scheduledOperations);
    }

    private static void BuildSteelmakingAndCastingWorkOrders(
        PlanReleaseBuildRequest request,
        IReadOnlyDictionary<Guid, FiniteScheduleAssignment[]> assignmentsBySource,
        IReadOnlyDictionary<Guid, FiniteScheduleTask> taskById,
        IReadOnlyDictionary<Guid, string> planningKeyByTask,
        List<WorkOrder> workOrders,
        List<ScheduledOperation> scheduledOperations)
    {
        foreach (var campaign in request.Campaigns.OrderBy(c => c.RequiredDate).ThenBy(c => c.CampaignNumber))
        {
            foreach (var gradeSequence in campaign.GradeSequence.OrderBy(g => g.SequenceNumber))
            {
                if (gradeSequence.PlannedQuantityMt <= 0m) continue;

                var matchingAllocations = campaign.Allocations
                    .Where(a => a.ProductionOrder is { } po &&
                                string.Equals(po.GradeCode, gradeSequence.GradeCode, StringComparison.OrdinalIgnoreCase) &&
                                a.FreshSteelQuantityMt > 0m)
                    .ToArray();
                if (matchingAllocations.Length == 0) continue;

                var matchingHeats = campaign.Heats
                    .Where(h => string.Equals(h.GradeCode, gradeSequence.GradeCode, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(h => h.SequenceNumber)
                    .ToArray();
                var allAssignments = matchingHeats
                    .SelectMany(h => assignmentsBySource.TryGetValue(h.Id, out var assignments)
                        ? assignments
                        : Array.Empty<FiniteScheduleAssignment>())
                    .OrderBy(x => x.StartUtc)
                    .ToArray();

                var castingAssignments = allAssignments
                    .Where(a => taskById.TryGetValue(a.TaskId, out var task) && IsCasting(task))
                    .ToArray();
                var steelmakingAssignments = allAssignments
                    .Where(a => taskById.TryGetValue(a.TaskId, out var task) && !IsCasting(task))
                    .ToArray();

                var materialCodes = matchingAllocations
                    .Select(a => a.ProductionOrder!.MaterialCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (steelmakingAssignments.Length > 0)
                {
                    var sms = NewWorkOrder(
                        $"SMS-{campaign.CampaignNumber}-{gradeSequence.SequenceNumber:00}",
                        WorkOrderType.Steelmaking,
                        campaign.Id,
                        SingleResourceOrNull(steelmakingAssignments),
                        materialCodes,
                        gradeSequence.GradeCode,
                        campaign.CasterSectionCode,
                        gradeSequence.PlannedQuantityMt,
                        steelmakingAssignments);
                    AddAllocations(sms, matchingAllocations.Select(x =>
                        (x.ProductionOrderId, x.ProductionOrder, x.FreshSteelQuantityMt)));
                    workOrders.Add(sms);
                    AddScheduledOperations(request.PlanVersionId, sms.Id, steelmakingAssignments, planningKeyByTask, scheduledOperations);
                }

                if (castingAssignments.Length > 0)
                {
                    var ccm = NewWorkOrder(
                        $"CCM-{campaign.CampaignNumber}-{gradeSequence.SequenceNumber:00}",
                        WorkOrderType.Casting,
                        campaign.Id,
                        SingleResourceOrNull(castingAssignments),
                        materialCodes,
                        gradeSequence.GradeCode,
                        campaign.CasterSectionCode,
                        matchingAllocations.Sum(x => x.FreshSteelQuantityMt),
                        castingAssignments);
                    AddAllocations(ccm, matchingAllocations.Select(x =>
                        (x.ProductionOrderId, x.ProductionOrder, x.FreshSteelQuantityMt)));
                    workOrders.Add(ccm);
                    AddScheduledOperations(request.PlanVersionId, ccm.Id, castingAssignments, planningKeyByTask, scheduledOperations);
                }
            }
        }
    }

    private static void BuildRollingWorkOrders(
        PlanReleaseBuildRequest request,
        IReadOnlyDictionary<Guid, FiniteScheduleAssignment[]> assignmentsBySource,
        IReadOnlyDictionary<Guid, FiniteScheduleTask> taskById,
        IReadOnlyDictionary<Guid, string> planningKeyByTask,
        List<WorkOrder> workOrders,
        List<ScheduledOperation> scheduledOperations)
    {
        // Compatibility/demo-only RollingPlan tasks can still release through this path. Configured
        // production routes no longer source tasks from RollingPlan, so they naturally fall through to
        // BuildConfiguredRouteWorkOrders below without producing a duplicate first-mill Work Order.
        foreach (var rollingPlan in request.ProductionStructure.RollingPlans.OrderBy(p => p.SequenceNumber))
        {
            var materialCodes = rollingPlan.Allocations
                .Select(a => a.ProductionOrder?.MaterialCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            assignmentsBySource.TryGetValue(rollingPlan.Id, out var assignments);
            assignments ??= Array.Empty<FiniteScheduleAssignment>();
            var rollingAssignments = assignments
                .Where(a => taskById.TryGetValue(a.TaskId, out var task) &&
                            task.ProcessOperationType is ProcessOperationType.Reheat or ProcessOperationType.HotRoll)
                .OrderBy(x => x.StartUtc)
                .ToArray();
            if (rollingAssignments.Length == 0) continue;

            var hotRollAssignments = rollingAssignments
                .Where(a => taskById.TryGetValue(a.TaskId, out var task) && task.ProcessOperationType == ProcessOperationType.HotRoll)
                .ToArray();
            var workOrder = NewWorkOrder(
                $"RM-{rollingPlan.Id:N}",
                WorkOrderType.HotRolling,
                rollingPlan.CampaignId,
                SingleResourceOrNull(hotRollAssignments),
                materialCodes,
                rollingPlan.GradeCode,
                rollingPlan.OutputCrossSectionCode,
                rollingPlan.PlannedQuantityMt,
                rollingAssignments);

            AddAllocations(workOrder, rollingPlan.Allocations.Select(x =>
                (x.ProductionOrderId, x.ProductionOrder, x.PlannedQuantityMt)));
            workOrders.Add(workOrder);
            AddScheduledOperations(request.PlanVersionId, workOrder.Id, rollingAssignments, planningKeyByTask, scheduledOperations);
        }
    }

    private static void BuildConfiguredRouteWorkOrders(
        PlanReleaseBuildRequest request,
        IReadOnlyDictionary<Guid, FiniteScheduleAssignment[]> assignmentsBySource,
        IReadOnlyDictionary<Guid, FiniteScheduleTask> taskById,
        IReadOnlyDictionary<Guid, string> planningKeyByTask,
        List<WorkOrder> workOrders,
        List<ScheduledOperation> scheduledOperations)
    {
        foreach (var plan in request.ProductionStructure.RouteOperationPlans ?? Array.Empty<RouteOperationPlan>())
        {
            assignmentsBySource.TryGetValue(plan.Id, out var assignments);
            assignments ??= Array.Empty<FiniteScheduleAssignment>();
            assignments = assignments
                .Where(a => taskById.TryGetValue(a.TaskId, out var task) && task.ProcessOperationType == plan.ProcessOperationType)
                .OrderBy(x => x.StartUtc)
                .ToArray();
            if (assignments.Length == 0) continue;

            var materialCodes = plan.Allocations
                .Select(x => x.ProductionOrder?.MaterialCode)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var campaignIds = plan.Allocations.Select(x => x.CampaignId).Distinct().ToArray();
            var prefix = plan.ReleaseWorkOrderType switch
            {
                WorkOrderType.HotRolling => "RM",
                WorkOrderType.ColdRolling => "CRM",
                WorkOrderType.Finishing => "FIN",
                WorkOrderType.Casting => "CCM",
                WorkOrderType.Steelmaking => "SMS",
                _ => "PROC"
            };

            var workOrder = NewWorkOrder(
                $"{prefix}-{plan.Id:N}",
                plan.ReleaseWorkOrderType,
                campaignIds.Length == 1 ? campaignIds[0] : null,
                SingleResourceOrNull(assignments),
                materialCodes,
                plan.GradeCode,
                plan.OutputCrossSectionCode,
                plan.PlannedQuantityMt,
                assignments);

            AddAllocations(workOrder, plan.Allocations.Select(x =>
                (x.ProductionOrderId, x.ProductionOrder, x.PlannedQuantityMt)));
            workOrders.Add(workOrder);
            AddScheduledOperations(request.PlanVersionId, workOrder.Id, assignments, planningKeyByTask, scheduledOperations);
        }
    }

    private static bool IsCasting(FiniteScheduleTask task) =>
        task.ProcessOperationType == ProcessOperationType.Ccm || task.TaskType == FiniteScheduleTaskType.Casting;

    private static WorkOrder NewWorkOrder(
        string number,
        WorkOrderType type,
        Guid? campaignId,
        Guid? resourceId,
        IReadOnlyCollection<string> materialCodes,
        string gradeCode,
        string crossSectionCode,
        decimal plannedQuantityMt,
        IReadOnlyCollection<FiniteScheduleAssignment> assignments) => new()
    {
        WorkOrderNumber = number,
        WorkOrderType = type,
        CampaignId = campaignId,
        ResourceId = resourceId,
        MaterialCode = materialCodes.Count == 1 ? materialCodes.Single() : "MULTI",
        GradeCode = gradeCode,
        CrossSectionCode = crossSectionCode,
        PlannedQuantityMt = plannedQuantityMt,
        PlannedStart = assignments.Count == 0 ? null : assignments.Min(x => x.StartUtc),
        PlannedEnd = assignments.Count == 0 ? null : assignments.Max(x => x.EndUtc),
        Status = WorkOrderStatus.Planned
    };

    private static Guid? SingleResourceOrNull(IReadOnlyCollection<FiniteScheduleAssignment> assignments)
    {
        var resources = assignments.Select(x => x.ResourceId).Distinct().ToArray();
        return resources.Length == 1 ? resources[0] : null;
    }

    private static void AddAllocations(
        WorkOrder workOrder,
        IEnumerable<(Guid ProductionOrderId, ProductionOrder? ProductionOrder, decimal QuantityMt)> allocations)
    {
        foreach (var allocation in allocations.Where(x => x.QuantityMt > 0m))
        {
            workOrder.Allocations.Add(new WorkOrderAllocation
            {
                WorkOrderId = workOrder.Id,
                WorkOrder = workOrder,
                ProductionOrderId = allocation.ProductionOrderId,
                ProductionOrder = allocation.ProductionOrder,
                PlannedQuantityMt = allocation.QuantityMt
            });
        }
    }

    private static void AddScheduledOperations(
        Guid planVersionId,
        Guid workOrderId,
        IEnumerable<FiniteScheduleAssignment> assignments,
        IReadOnlyDictionary<Guid, string> planningKeyByTask,
        ICollection<ScheduledOperation> scheduledOperations)
    {
        foreach (var assignment in assignments)
        {
            planningKeyByTask.TryGetValue(assignment.TaskId, out var planningKey);
            scheduledOperations.Add(new ScheduledOperation
            {
                PlanVersionId = planVersionId,
                WorkOrderId = workOrderId,
                ResourceId = assignment.ResourceId,
                PlanningKey = planningKey,
                Start = assignment.StartUtc,
                End = assignment.EndUtc,
                IsFrozen = false
            });
        }
    }
}
