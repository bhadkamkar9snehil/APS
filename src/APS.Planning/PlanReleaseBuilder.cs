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
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.StartUtc).First());

        BuildSmsWorkOrders(request.Campaigns, workOrders);
        BuildRollingWorkOrders(request, assignmentsBySource, workOrders, scheduledOperations);

        return new PlanRelease(request.PlanVersionId, workOrders, scheduledOperations);
    }

    private static void BuildSmsWorkOrders(
        IReadOnlyCollection<Campaign> campaigns,
        List<WorkOrder> workOrders)
    {
        foreach (var campaign in campaigns.OrderBy(c => c.RequiredDate).ThenBy(c => c.CampaignNumber))
        {
            foreach (var gradeSequence in campaign.GradeSequence.OrderBy(g => g.SequenceNumber))
            {
                if (gradeSequence.PlannedQuantityMt <= 0m) continue;

                var matchingAllocations = campaign.Allocations
                    .Where(a => a.ProductionOrder is { } po &&
                                string.Equals(po.GradeCode, gradeSequence.GradeCode, StringComparison.OrdinalIgnoreCase) &&
                                a.FreshSteelQuantityMt > 0m)
                    .ToArray();

                var materialCodes = matchingAllocations
                    .Select(a => a.ProductionOrder!.MaterialCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var workOrder = new WorkOrder
                {
                    WorkOrderNumber = $"SMS-{campaign.CampaignNumber}-{gradeSequence.SequenceNumber:00}",
                    WorkOrderType = WorkOrderType.Steelmaking,
                    CampaignId = campaign.Id,
                    MaterialCode = materialCodes.Length == 1 ? materialCodes[0] : "MULTI",
                    GradeCode = gradeSequence.GradeCode,
                    CrossSectionCode = campaign.CasterSectionCode,
                    PlannedQuantityMt = gradeSequence.PlannedQuantityMt,
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
            }
        }
    }

    private static void BuildRollingWorkOrders(
        PlanReleaseBuildRequest request,
        IReadOnlyDictionary<Guid, FiniteScheduleAssignment> assignmentsBySource,
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

            assignmentsBySource.TryGetValue(rollingPlan.Id, out var assignment);

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
                PlannedStart = assignment?.StartUtc,
                PlannedEnd = assignment?.EndUtc,
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

            if (assignment is not null)
            {
                scheduledOperations.Add(new ScheduledOperation
                {
                    PlanVersionId = request.PlanVersionId,
                    WorkOrderId = workOrder.Id,
                    ResourceId = assignment.ResourceId,
                    Start = assignment.StartUtc,
                    End = assignment.EndUtc,
                    IsFrozen = false
                });
            }
        }
    }
}
