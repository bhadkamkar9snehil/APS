using APS.Application;
using APS.Domain;

namespace APS.Infrastructure;

internal static class DemandSnapshotProjector
{
    public static void AddToContext(
        ApsDbContext db,
        Guid planVersionId,
        DemandOrchestrationResult? demand)
    {
        if (demand is null) return;

        foreach (var item in demand.MakeToOrderDemand)
        {
            db.PlanDemandSnapshots.Add(new PlanDemandSnapshot
            {
                PlanVersionId = planVersionId,
                SalesOrderId = item.SalesOrderId,
                ProductionOrderId = item.ProductionOrderId,
                SalesOrderNumber = item.SalesOrderNumber,
                SalesOrderItemNumber = item.SalesOrderItemNumber,
                CustomerCode = item.CustomerCode,
                MaterialCode = item.MaterialCode,
                GradeCode = item.GradeCode,
                FinalCrossSectionCode = item.FinalCrossSectionCode,
                OpenDemandQuantityMt = item.OpenDemandQuantityMt,
                FinishedGoodsCoveredQuantityMt = item.FinishedGoodsCoveredQuantityMt,
                ManufacturingRequirementQuantityMt = item.ManufacturingRequirementQuantityMt,
                CustomerRequiredDate = item.CustomerRequiredDate,
                ConfirmedDeliveryDate = item.ConfirmedDeliveryDate,
                ProductionRequiredByDate = item.ProductionRequiredByDate,
                Priority = item.Priority,
                Disposition = item.Disposition,
                PlannerAttentionRequired = item.PlannerAttentionRequired,
                ReasonCode = item.ReasonCode
            });

            foreach (var coverage in item.FinishedGoodsCoverage)
            {
                db.PlanDemandCoverageSnapshots.Add(new PlanDemandCoverageSnapshot
                {
                    PlanVersionId = planVersionId,
                    SalesOrderId = item.SalesOrderId,
                    ProductionOrderId = item.ProductionOrderId,
                    MaterialCode = coverage.MaterialCode,
                    GradeCode = coverage.GradeCode,
                    CrossSectionCode = coverage.CrossSectionCode,
                    LocationCode = coverage.LocationCode,
                    AvailableFromUtc = coverage.AvailableFromUtc,
                    QualityStatus = coverage.QualityStatus,
                    QuantityMt = coverage.QuantityMt
                });
            }
        }
    }
}
