using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class OrderServicePolicyTests
{
    [Fact]
    public async Task Flexible_policy_persists_explicit_window_on_demand_state()
    {
        await using var db = NewDb();
        var state = AddDemandState(db);
        await db.SaveChangesAsync();
        var target = state.ConfirmedDeliveryDate ?? state.CustomerRequiredDate;
        var service = new OrderServicePolicyService(db);

        var saved = await service.UpdateAsync(new UpdateOrderServicePolicyRequest(
            state.SalesOrderId,
            ServiceCommitmentClass.Flexible,
            target.AddDays(-1),
            target.AddDays(3)));

        Assert.Equal(ServiceCommitmentClass.Flexible, saved.ServiceCommitment);
        Assert.Equal(target.AddDays(-1), saved.EarliestAcceptableDeliveryDate);
        Assert.Equal(target.AddDays(3), saved.LatestAcceptableDeliveryDate);
        var persisted = await db.SalesOrderDemandStates.SingleAsync();
        Assert.Equal(ServiceCommitmentClass.Flexible, persisted.ServiceCommitment);
        Assert.Equal(target.AddDays(3), persisted.LatestAcceptableDeliveryDate);
    }

    [Fact]
    public async Task Hard_policy_rejects_a_later_delivery_boundary()
    {
        await using var db = NewDb();
        var state = AddDemandState(db);
        await db.SaveChangesAsync();
        var target = state.ConfirmedDeliveryDate ?? state.CustomerRequiredDate;
        var service = new OrderServicePolicyService(db);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(
            new UpdateOrderServicePolicyRequest(
                state.SalesOrderId,
                ServiceCommitmentClass.Hard,
                LatestAcceptableDeliveryDate: target.AddDays(1))));

        Assert.Contains("Hard commitments", error.Message, StringComparison.Ordinal);
    }

    private static SalesOrderDemandState AddDemandState(ApsDbContext db)
    {
        var order = new SalesOrder
        {
            SalesOrderNumber = "SO-1",
            ItemNumber = "10",
            MaterialCode = "FG-16",
            GradeCode = "G1",
            FinalCrossSectionCode = "16MM",
            OrderQuantityMt = 100m,
            OpenQuantityMt = 100m,
            RequiredDate = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
            CustomerCode = "CUST",
            ExternalStatus = "OPEN"
        };
        var state = new SalesOrderDemandState
        {
            SalesOrderId = order.Id,
            SalesOrder = order,
            CustomerRequiredDate = order.RequiredDate,
            ConfirmedDeliveryDate = order.RequiredDate.AddDays(1),
            ProductionRequiredByDate = order.RequiredDate,
            Priority = 4,
            Disposition = DemandReconciliationDisposition.Unchanged
        };
        db.SalesOrders.Add(order);
        db.SalesOrderDemandStates.Add(state);
        return state;
    }

    private static ApsDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"order-service-{Guid.NewGuid():N}")
            .Options;
        return new ApsDbContext(options);
    }
}
