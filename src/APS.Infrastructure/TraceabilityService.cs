using APS.Application;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class TraceabilityService(ApsDbContext db) : ITraceabilityService
{
    public async Task<WorkOrderTrace?> GetWorkOrderTraceAsync(
        Guid workOrderId,
        CancellationToken cancellationToken = default)
    {
        var workOrder = await db.WorkOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == workOrderId, cancellationToken);

        if (workOrder is null) return null;

        var productionOrders = await (
            from allocation in db.WorkOrderAllocations.AsNoTracking()
            join po in db.ProductionOrders.AsNoTracking()
                on allocation.ProductionOrderId equals po.Id
            join so0 in db.SalesOrders.AsNoTracking()
                on po.SalesOrderId equals so0.Id into salesOrders
            from so in salesOrders.DefaultIfEmpty()
            where allocation.WorkOrderId == workOrderId
            orderby po.ProductionOrderNumber
            select new ProductionOrderTrace(
                po.Id,
                po.ProductionOrderNumber,
                po.DemandSource,
                allocation.PlannedQuantityMt,
                so == null ? null : so.SalesOrderNumber,
                so == null ? null : so.ItemNumber,
                po.SalesOrderId))
            .ToListAsync(cancellationToken);

        var producedLots = await db.MaterialLots
            .AsNoTracking()
            .Where(x => x.ProducedByWorkOrderId == workOrderId)
            .OrderBy(x => x.LotNumber)
            .Select(x => new ProducedLotTrace(
                x.Id,
                x.LotNumber,
                x.QuantityMt,
                x.GradeCode,
                x.CrossSectionCode))
            .ToListAsync(cancellationToken);

        return new WorkOrderTrace(
            workOrder.Id,
            workOrder.WorkOrderNumber,
            workOrder.WorkOrderType,
            workOrder.CampaignId,
            workOrder.PlannedQuantityMt,
            workOrder.ActualQuantityMt,
            productionOrders,
            producedLots);
    }

    public async Task<MaterialLotTrace?> GetMaterialLotTraceAsync(
        Guid materialLotId,
        CancellationToken cancellationToken = default)
    {
        var lot = await db.MaterialLots
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == materialLotId, cancellationToken);

        if (lot is null) return null;

        var allocations = await (
            from allocation in db.MaterialLotAllocations.AsNoTracking()
            join po in db.ProductionOrders.AsNoTracking()
                on allocation.ProductionOrderId equals po.Id
            join so0 in db.SalesOrders.AsNoTracking()
                on po.SalesOrderId equals so0.Id into salesOrders
            from so in salesOrders.DefaultIfEmpty()
            where allocation.MaterialLotId == materialLotId
               && allocation.Status != Domain.LotAllocationStatus.Cancelled
            orderby po.ProductionOrderNumber
            select new ProductionOrderTrace(
                po.Id,
                po.ProductionOrderNumber,
                po.DemandSource,
                allocation.AllocatedQuantityMt,
                so == null ? null : so.SalesOrderNumber,
                so == null ? null : so.ItemNumber,
                po.SalesOrderId))
            .ToListAsync(cancellationToken);

        var parents = await (
            from genealogy in db.LotGenealogy.AsNoTracking()
            join parent in db.MaterialLots.AsNoTracking()
                on genealogy.ParentLotId equals parent.Id
            where genealogy.ChildLotId == materialLotId
            orderby parent.LotNumber
            select new MaterialLotParentTrace(parent.Id, parent.LotNumber, genealogy.QuantityMt))
            .ToListAsync(cancellationToken);

        var children = await (
            from genealogy in db.LotGenealogy.AsNoTracking()
            join child in db.MaterialLots.AsNoTracking()
                on genealogy.ChildLotId equals child.Id
            where genealogy.ParentLotId == materialLotId
            orderby child.LotNumber
            select new MaterialLotChildTrace(child.Id, child.LotNumber, genealogy.QuantityMt))
            .ToListAsync(cancellationToken);

        return new MaterialLotTrace(
            lot.Id,
            lot.LotNumber,
            lot.MaterialCode,
            lot.GradeCode,
            lot.CrossSectionCode,
            lot.QuantityMt,
            lot.ProducedByWorkOrderId,
            allocations,
            parents,
            children);
    }
}
