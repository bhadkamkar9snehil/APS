using APS.Application;
using APS.Domain;

namespace APS.Planning;

public sealed class PlanReleaseBuilder : IPlanReleaseBuilder
{
    public PlanRelease Build(PlanReleaseBuildRequest request)
    {
        if (!request.Schedule.IsFeasible)
        {
            throw new InvalidOperationException("Cannot release an infeasible schedule.");
        }

        var workOrders = new List<WorkOrder>();
        var scheduledOperations = new List<ScheduledOperation>();
        var assignmentsBySource = request.Schedule.Assignments
            .GroupBy(a => a.SourceEntityId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.StartUtc).ToArray());
        var planningKeyByTask = PlanningTaskIdentityService.Build(request.ProductionStructure)
            .ToDictionary(x => x.TaskId, x => x.PlanningKey);

        BuildSmsWorkOrders(request, assignmentsBySource, planningKeyByTask, workOrders, scheduledOperations);
        BuildRollingWorkOrders(request, assignmentsBySource, planningKeyByTask, workOrders, scheduledOperations);

        return new PlanRelease(request.PlanVersionId, workOrders, scheduledOperations);
    }

    private static void BuildSmsWorkOrders(
        PlanReleaseBuildRequest request,
        IReadOnlyDictionary<Guid, FiniteScheduleAssignment[]> assignmentsBySource,
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
                var matchingHeats = campaign.Heats
                    .Where(h => string.Equals(h.GradeCode, gradeSequence.GradeCode, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(h => h.SequenceNumber)
                    .ToArray();
                var heatAssignments = matchingHeats
                    .SelectMany(h => assignmentsBySource.TryGetValue(h.Id, out var assignments)
                        ? assignments
                        : Array.Empty<FiniteScheduleAssignment>())
                    .OrderBy(x => x.StartUtc)
                    .ToArray();

                var materialCodes = matchingAllocations
                    .Select(a => a.ProductionOrder!.MaterialCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var assignedResources = heatAssignments.Select(x => x.ResourceId).Distinct().ToArray();

                var workOrder = new WorkOrder
                {
                    WorkOrderNumber = $"SMS-{campaign.CampaignNumber}-{gradeSequence.SequenceNumber:00}",
                    WorkOrderType = WorkOrderType.Steelmaking,
                    CampaignId = campaign.Id,
                    ResourceId = assignedResources.Length == 1 ? assignedResources[0] : null,
                    MaterialCode = materialCodes.Length == 1 ? materialCodes[0] : "MULTI",
                    GradeCode = gradeSequence.GradeCode,
                    CrossSectionCode = campaign.CasterSectionCode,
                    PlannedQuantityMt = gradeSequence.PlannedQuantityMt,
                    PlannedStart = heatAssignments.Length == 0 ? null : heatAssignments.Min(x => x.StartUtc),
                    PlannedEnd = heatAssignments.Length == 0 ? null : heatAssignments.Max(x => x.EndUtc),
                    Status = WorkOrderStatus.Planned
                };

                foreach (var allocation in matchingAllocations)
                {
                    workOrder.Allocations.Add(new WorkOrderAllocation
                    {
                        WorkOrderId = workOrder.Id,
                        WorkOrder = workOrder,
                        ProductionOrderId = allocation.ProductionOrderId,
                        ProductionOrder = allocation.ProductionOrder,
                        PlannedQuantityMt = allocation.FreshSteelQuantityMt
                    });
                }

                workOrders.Add(workOrder);

                foreach (var assignment in heatAssignments)
                {
                    planningKeyByTask.TryGetValue(assignment.TaskId, out var planningKey);
                    scheduledOperations.Add(new ScheduledOperation
                    {
                        PlanVersionId = request.PlanVersionId,
                        WorkOrderId = workOrder.Id,
                        ResourceId = assignment.ResourceId,
                        PlanningKey = planningKey,
                        Start = assignment.StartUtc,
                        End = assignment.EndUtc,
                        IsFrozen = false
                    });
                }
            }
        }
    }

    private static void BuildRollingWorkOrders(
        PlanReleaseBuildRequest request,
        IReadOnlyDictionary<Guid, FiniteScheduleAssignment[]> assignmentsBySource,
        IReadOnlyDictionary<Guid, string> planningKeyByTask,
        List<WorkOrder> workOrders,
        List<ScheduledOperation> scheduledOperations)
    {
        foreach (var rollingPlan in request.ProductionStructure.RollingPlans
                     .OrderBy(p => p.RollingMillResourceId)
                     .ThenBy(p => p.SequenceNumber))
        {
            if (!rollingPlan.RollingMillResourceId.HasValue) continue;

            var materialCodes = rollingPlan.Allocations
                .Select(a => a.ProductionOrder?.MaterialCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            assignmentsBySource.TryGetValue(rollingPlan.Id, out var assignments);
            assignments ??= Array.Empty<FiniteScheduleAssignment>();

            var workOrder = new WorkOrder
            {
                WorkOrderNumber = $"RM-{rollingPlan.Id:N}",
                WorkOrderType = WorkOrderType.HotRolling,
                CampaignId = rollingPlan.CampaignId,
                ResourceId = rollingPlan.RollingMillResourceId,
                MaterialCode = materialCodes.Length == 1 ? materialCodes[0] : "MULTI",
                GradeCode = rollingPlan.GradeCode,
                CrossSectionCode = rollingPlan.OutputCrossSectionCode,
                PlannedQuantityMt = rollingPlan.PlannedQuantityMt,
                PlannedStart = assignments.Length == 0 ? null : assignments.Min(x => x.StartUtc),
                PlannedEnd = assignments.Length == 0 ? null : assignments.Max(x => x.EndUtc),
                Status = WorkOrderStatus.Planned
            };

            foreach (var allocation in rollingPlan.Allocations)
            {
                workOrder.Allocations.Add(new WorkOrderAllocation
                {
                    WorkOrderId = workOrder.Id,
                    WorkOrder = workOrder,
                    ProductionOrderId = allocation.ProductionOrderId,
                    ProductionOrder = allocation.ProductionOrder,
                    PlannedQuantityMt = allocation.PlannedQuantityMt
                });
            }

            workOrders.Add(workOrder);

            foreach (var assignment in assignments)
            {
                planningKeyByTask.TryGetValue(assignment.TaskId, out var planningKey);
                scheduledOperations.Add(new ScheduledOperation
                {
                    PlanVersionId = request.PlanVersionId,
                    WorkOrderId = workOrder.Id,
                    ResourceId = assignment.ResourceId,
                    PlanningKey = planningKey,
                    Start = assignment.StartUtc,
                    End = assignment.EndUtc,
                    IsFrozen = false
                });
            }
        }
    }
}
