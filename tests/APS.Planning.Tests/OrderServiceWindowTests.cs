using APS.Application;
using APS.Domain;
using Xunit;

namespace APS.Planning.Tests;

public sealed class OrderServiceWindowTests
{
    [Fact]
    public void Flexible_window_preserves_target_and_derives_separate_production_boundaries()
    {
        var salesOrderId = Guid.NewGuid();
        var productionOrderId = Guid.NewGuid();
        var targetDelivery = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        var productionTarget = targetDelivery.AddHours(-12);
        var canonical = ProductionOrder(productionOrderId, salesOrderId, productionTarget, priority: 10);
        var item = DemandItem(salesOrderId, productionOrderId, targetDelivery, productionTarget, priority: 10);
        var policy = new OrderServicePolicy(
            salesOrderId,
            "SO-RUSH",
            "10",
            "CUST",
            targetDelivery,
            null,
            10,
            "OPEN",
            ServiceCommitmentClass.Flexible,
            targetDelivery.AddDays(-1),
            targetDelivery.AddDays(3));

        var projected = OrderServiceWindow.Apply(
            new DemandOrchestrationResult(
                new[] { canonical },
                new[] { item },
                Array.Empty<ProductionOrder>(),
                Array.Empty<PlanningIssue>()),
            new[] { policy });

        var planned = Assert.Single(projected.ProductionOrders);
        var service = Assert.Single(projected.MakeToOrderDemand);
        Assert.Same(canonical, planned);
        Assert.Equal(productionTarget, planned.RequiredDate);
        Assert.Equal(productionTarget.AddDays(3), service.ProductionLatestAcceptableDate);
        Assert.Equal(productionTarget.AddDays(-1), service.ProductionEarliestAcceptableDate);
        Assert.Equal(10, planned.Priority);
        Assert.Equal(ServiceCommitmentClass.Flexible, service.ServiceCommitment);
    }

    [Fact]
    public void Hard_commitment_ignores_late_tolerance_but_keeps_rush_priority_separate()
    {
        var salesOrderId = Guid.NewGuid();
        var productionOrderId = Guid.NewGuid();
        var targetDelivery = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        var productionTarget = targetDelivery.AddHours(-6);
        var canonical = ProductionOrder(productionOrderId, salesOrderId, productionTarget, priority: 10);
        var item = DemandItem(salesOrderId, productionOrderId, targetDelivery, productionTarget, priority: 10);
        var policy = new OrderServicePolicy(
            salesOrderId,
            "SO-HARD-RUSH",
            "10",
            "CUST",
            targetDelivery,
            null,
            10,
            "OPEN",
            ServiceCommitmentClass.Hard,
            null,
            targetDelivery.AddDays(7));

        var projected = OrderServiceWindow.Apply(
            new DemandOrchestrationResult(
                new[] { canonical },
                new[] { item },
                Array.Empty<ProductionOrder>(),
                Array.Empty<PlanningIssue>()),
            new[] { policy });

        var planned = Assert.Single(projected.ProductionOrders);
        var service = Assert.Single(projected.MakeToOrderDemand);
        Assert.Equal(productionTarget, planned.RequiredDate);
        Assert.Equal(targetDelivery, service.LatestAcceptableDeliveryDate);
        Assert.Equal(productionTarget, service.ProductionLatestAcceptableDate);
        Assert.Equal(10, planned.Priority);
        Assert.Equal(ServiceCommitmentClass.Hard, service.ServiceCommitment);
    }

    [Theory]
    [InlineData(ServiceCommitmentClass.Flexible, -1, 3, null)]
    [InlineData(ServiceCommitmentClass.Standard, null, null, null)]
    [InlineData(ServiceCommitmentClass.Hard, null, null, null)]
    [InlineData(ServiceCommitmentClass.Flexible, null, null, "Flexible commitments require at least one acceptable delivery boundary.")]
    [InlineData(ServiceCommitmentClass.Standard, 1, null, "Earliest acceptable delivery must be on or before the requested/confirmed target date.")]
    [InlineData(ServiceCommitmentClass.Standard, null, -1, "Latest acceptable delivery must be on or after the requested/confirmed target date.")]
    [InlineData(ServiceCommitmentClass.Hard, null, 1, "Hard commitments cannot move later than the requested/confirmed target date.")]
    public void Service_window_validation_has_one_shared_rule_set(
        ServiceCommitmentClass commitment,
        int? earliestOffsetDays,
        int? latestOffsetDays,
        string? expectedError)
    {
        var target = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        var earliest = earliestOffsetDays.HasValue ? target.AddDays(earliestOffsetDays.Value) : null;
        var latest = latestOffsetDays.HasValue ? target.AddDays(latestOffsetDays.Value) : null;

        var error = OrderServicePolicyRules.ValidationError(commitment, target, earliest, latest);

        Assert.Equal(expectedError, error);
    }

    private static ProductionOrder ProductionOrder(Guid id, Guid salesOrderId, DateTime due, int priority) => new()
    {
        Id = id,
        ProductionOrderNumber = "MTO-TEST-10",
        DemandSource = DemandSourceType.MakeToOrder,
        MaterialCode = "FG-16",
        GradeCode = "G1",
        FinalCrossSectionCode = "16MM",
        CasterSectionCode = "150X150",
        RouteCode = "R1",
        PlannedQuantityMt = 100m,
        RemainingQuantityMt = 100m,
        RequiredDate = due,
        Priority = priority,
        SalesOrderId = salesOrderId
    };

    private static DemandOrchestrationItem DemandItem(
        Guid salesOrderId,
        Guid productionOrderId,
        DateTime targetDelivery,
        DateTime productionTarget,
        int priority) => new(
            salesOrderId,
            "SO-TEST",
            "10",
            "FG-16",
            "G1",
            "16MM",
            "CUST",
            null,
            productionOrderId,
            "MTO-TEST-10",
            100m,
            0m,
            100m,
            targetDelivery,
            null,
            productionTarget,
            priority,
            DemandReconciliationDisposition.ProductionOrderCreated,
            false,
            null,
            Array.Empty<DemandCoverageEvidence>());
}
