using APS.Application;
using APS.Domain;

namespace APS.Integrations.XStudio;

public sealed class XStudioPlanReleaseMapper : IXStudioPlanReleaseMapper
{
    public XStudioPlanReleaseEnvelope Map(
        PlanRelease release,
        IReadOnlyCollection<CastSequence> castSequences,
        IReadOnlyCollection<Resource> resources)
    {
        var resourceCodes = resources.ToDictionary(r => r.Id, r => r.Code);
        var workOrders = release.WorkOrders
            .OrderBy(w => w.PlannedStart ?? DateTime.MaxValue)
            .ThenBy(w => w.WorkOrderNumber)
            .Select(workOrder => new XStudioWorkOrderPlan(
                workOrder.WorkOrderNumber,
                workOrder.WorkOrderType.ToString(),
                workOrder.MaterialCode,
                workOrder.GradeCode,
                workOrder.CrossSectionCode,
                workOrder.PlannedQuantityMt,
                workOrder.PlannedStart,
                workOrder.PlannedEnd,
                workOrder.ResourceId.HasValue && resourceCodes.TryGetValue(workOrder.ResourceId.Value, out var resourceCode)
                    ? resourceCode
                    : null,
                workOrder.Allocations
                    .Where(a => a.ProductionOrder is not null)
                    .Select(a =>
                    {
                        var po = a.ProductionOrder!;
                        return new XStudioWorkOrderAllocation(
                            po.ProductionOrderNumber,
                            po.SalesOrder?.SalesOrderNumber,
                            po.SalesOrder?.ItemNumber,
                            po.DemandSource,
                            a.PlannedQuantityMt);
                    })
                    .ToArray()))
            .ToArray();

        var sequences = castSequences
            .OrderBy(s => resourceCodes.TryGetValue(s.CasterResourceId, out var code) ? code : s.CasterResourceId.ToString())
            .ThenBy(s => s.SequenceNumber)
            .Select(sequence => new XStudioCastSequencePlan(
                sequence.Id,
                resourceCodes.TryGetValue(sequence.CasterResourceId, out var casterCode)
                    ? casterCode
                    : sequence.CasterResourceId.ToString(),
                sequence.SequenceNumber,
                sequence.CasterSectionCode,
                sequence.RouteCode,
                sequence.PlannedStart,
                sequence.PlannedEnd,
                sequence.Heats
                    .OrderBy(h => h.Position)
                    .Select(h => new XStudioCastHeatPlan(
                        h.CampaignHeatId,
                        h.Position,
                        h.CampaignHeat.GradeCode,
                        h.CampaignHeat.PlannedQuantityMt,
                        h.CampaignHeat.CampaignId))
                    .ToArray()))
            .ToArray();

        return new XStudioPlanReleaseEnvelope(
            release.PlanVersionId,
            DateTime.UtcNow,
            workOrders,
            sequences);
    }
}
