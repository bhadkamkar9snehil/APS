using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class WorkOrderExecutionServiceTests
{
    [Fact]
    public async Task Manual_running_and_completion_updates_actual_state_and_history()
    {
        await using var db = CreateDb();
        var workOrder = NewWorkOrder();
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();
        var service = new WorkOrderExecutionService(db);
        var start = new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);

        await service.ApplyAsync(new WorkOrderExecutionUpdate(
            workOrder.Id,
            null,
            WorkOrderStatus.Running,
            start,
            null,
            0m,
            start,
            ExecutionUpdateSource.Manual));

        var completed = await service.ApplyAsync(new WorkOrderExecutionUpdate(
            workOrder.Id,
            null,
            WorkOrderStatus.Completed,
            null,
            start.AddHours(2),
            98.5m,
            start.AddHours(2),
            ExecutionUpdateSource.Manual));

        Assert.Equal(WorkOrderStatus.Completed, completed.Status);
        Assert.Equal(start, completed.ActualStart);
        Assert.Equal(start.AddHours(2), completed.ActualEnd);
        Assert.Equal(98.5m, completed.ActualQuantityMt);
        Assert.Equal(2, await db.WorkOrderStatusHistory.CountAsync());
    }

    [Fact]
    public async Task Completed_work_order_cannot_move_back_without_correction()
    {
        await using var db = CreateDb();
        var workOrder = NewWorkOrder();
        workOrder.Status = WorkOrderStatus.Completed;
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();
        var service = new WorkOrderExecutionService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(new WorkOrderExecutionUpdate(
            workOrder.Id,
            null,
            WorkOrderStatus.Running,
            null,
            null,
            null,
            DateTime.UtcNow,
            ExecutionUpdateSource.Manual)));
    }

    [Fact]
    public async Task Duplicate_mes_event_id_is_idempotent()
    {
        await using var db = CreateDb();
        var workOrder = NewWorkOrder();
        workOrder.ExternalExecutionId = "MES-WO-1001";
        db.WorkOrders.Add(workOrder);
        await db.SaveChangesAsync();
        var service = new WorkOrderExecutionService(db);
        var changed = new DateTime(2026, 8, 17, 10, 15, 0, DateTimeKind.Utc);
        var update = new WorkOrderExecutionUpdate(
            null,
            "MES-WO-1001",
            WorkOrderStatus.Running,
            changed,
            null,
            null,
            changed,
            ExecutionUpdateSource.MesApi,
            "EVT-001");

        await service.ApplyAsync(update);
        await service.ApplyAsync(update);

        Assert.Single(await db.WorkOrderStatusHistory.ToListAsync());
        Assert.Equal(WorkOrderStatus.Running, (await db.WorkOrders.SingleAsync()).Status);
    }

    private static ApsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-tests-{Guid.NewGuid():N}")
            .Options;
        return new ApsDbContext(options);
    }

    private static WorkOrder NewWorkOrder() => new()
    {
        WorkOrderNumber = $"WO-{Guid.NewGuid():N}",
        WorkOrderType = WorkOrderType.HotRolling,
        MaterialCode = "FG-16",
        GradeCode = "G1",
        CrossSectionCode = "16MM",
        PlannedQuantityMt = 100m,
        Status = WorkOrderStatus.Planned
    };
}
