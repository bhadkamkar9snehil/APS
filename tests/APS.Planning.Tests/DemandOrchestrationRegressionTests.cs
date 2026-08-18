using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace APS.Planning.Tests;

public sealed class DemandOrchestrationRegressionTests
{
    private static readonly DateTime ReferenceUtc = new(2026, 8, 18, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DueUtc = new(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Changed_open_demand_resizes_same_uncommitted_mto_production_order()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var input = So("451000", "10", 100m);

        await service.ReconcileSalesOrdersAsync(new[] { input });
        await PrepareAsync(service, Array.Empty<InventoryPosition>());
        var original = await db.ProductionOrders.SingleAsync();
        var originalId = original.Id;

        await service.ReconcileSalesOrdersAsync(new[] { input with { OpenQuantityMt = 80m } });
        var result = await PrepareAsync(service, Array.Empty<InventoryPosition>());

        var demand = Assert.Single(result.MakeToOrderDemand);
        var po = Assert.Single(result.ProductionOrders);
        Assert.Equal(originalId, po.Id);
        Assert.Equal(80m, demand.ManufacturingRequirementQuantityMt);
        Assert.Equal(80m, po.PlannedQuantityMt);
        Assert.Equal(80m, po.RemainingQuantityMt);
        Assert.Equal(ProductionOrderStatus.Planned, po.Status);
        Assert.Equal(DemandReconciliationDisposition.ProductionOrderUpdated, demand.Disposition);
        Assert.False(demand.PlannerAttentionRequired);
    }

    [Fact]
    public async Task Later_finished_goods_does_not_resize_or_cancel_firmed_mto_production_order()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var input = So("451001", "10", 100m);

        await service.ReconcileSalesOrdersAsync(new[] { input });
        await PrepareAsync(service, Array.Empty<InventoryPosition>());
        var po = await db.ProductionOrders.SingleAsync();
        po.Status = ProductionOrderStatus.Firmed;
        await db.SaveChangesAsync();

        var result = await PrepareAsync(service, Inventory(100m));

        var demand = Assert.Single(result.MakeToOrderDemand);
        var protectedPo = Assert.Single(result.ProductionOrders);
        Assert.Equal(po.Id, protectedPo.Id);
        Assert.Equal(100m, demand.FinishedGoodsCoveredQuantityMt);
        Assert.Equal(0m, demand.ManufacturingRequirementQuantityMt);
        Assert.Equal(100m, protectedPo.PlannedQuantityMt);
        Assert.Equal(100m, protectedPo.RemainingQuantityMt);
        Assert.Equal(ProductionOrderStatus.Firmed, protectedPo.Status);
        Assert.Equal(DemandReconciliationDisposition.CommittedProductionOrderProtected, demand.Disposition);
        Assert.True(demand.PlannerAttentionRequired);
        Assert.Equal("COMMITTED_MTO_NOW_EXCEEDS_CURRENT_MANUFACTURING_REQUIREMENT", demand.ReasonCode);
    }

    [Fact]
    public async Task Inherited_default_route_does_not_false_flag_unchanged_firmed_special_requirement()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var input = So("451002", "10", 100m) with
        {
            Requirement = new SalesOrderRequirementInput(
                QualityClassCode: "Q-CERT",
                RequireVd: true)
        };

        await service.ReconcileSalesOrdersAsync(new[] { input });
        await PrepareAsync(service, Array.Empty<InventoryPosition>());
        var po = await db.ProductionOrders.Include(x => x.Requirement).SingleAsync();
        Assert.Equal("R1", po.Requirement!.RequiredRouteCode);
        po.Status = ProductionOrderStatus.Firmed;
        await db.SaveChangesAsync();

        var result = await PrepareAsync(service, Array.Empty<InventoryPosition>());

        var demand = Assert.Single(result.MakeToOrderDemand);
        var protectedPo = Assert.Single(result.ProductionOrders);
        Assert.Equal(po.Id, protectedPo.Id);
        Assert.Equal(ProductionOrderStatus.Firmed, protectedPo.Status);
        Assert.Equal(DemandReconciliationDisposition.CommittedProductionOrderProtected, demand.Disposition);
        Assert.False(demand.PlannerAttentionRequired);
        Assert.Equal("COMMITTED_MTO_MATCHES_CURRENT_DEMAND", demand.ReasonCode);
    }

    private static ProductionDemandOrchestrationService NewService(ApsDbContext db) =>
        new(db, NullLogger<ProductionDemandOrchestrationService>.Instance);

    private static async Task<DemandOrchestrationResult> PrepareAsync(
        IProductionDemandOrchestrationService service,
        IReadOnlyCollection<InventoryPosition> inventory) =>
        await service.PrepareAsync(
            new PlanningDemandSelection(
                RequiredThroughUtc: DueUtc.AddMonths(2),
                IncludeMakeToStock: false),
            inventory,
            Masters(),
            ReferenceUtc,
            DueUtc.AddMonths(2));

    private static SalesOrderDemandInput So(string so, string item, decimal qty) =>
        new(
            so,
            item,
            "FG-16",
            "G1",
            "16MM",
            qty,
            qty,
            DueUtc,
            CustomerCode: "CUST",
            CustomerGroupCode: "GROUP",
            ExternalStatus: "OPEN",
            Priority: 1);

    private static IReadOnlyCollection<InventoryPosition> Inventory(decimal qty) =>
        new[]
        {
            new InventoryPosition
            {
                MaterialCode = "FG-16",
                GradeCode = "G1",
                CrossSectionCode = "16MM",
                Stage = InventoryStage.FinishedGoods,
                LocationCode = "FG-YARD",
                QualityStatus = MaterialQualityStatus.Available,
                AvailableQuantityMt = qty
            }
        };

    private static PlanningMasterDataSnapshot Masters()
    {
        var grade = new SteelGrade
        {
            GradeCode = "G1",
            Description = "Test grade",
            GradeFamilyCode = "F1",
            SequenceClassCode = "SEQ1",
            DefaultCasterSectionCode = "150X150",
            DefaultRouteCode = "R1",
            IsActive = true
        };
        var routeOperation = new ManufacturingRouteOperation
        {
            ManufacturingRouteId = Guid.NewGuid(),
            RouteCode = "R1",
            SequenceNumber = 1,
            ProcessOperationType = ProcessOperationType.HotRoll,
            ReleaseWorkOrderType = WorkOrderType.HotRolling,
            InputCrossSectionCode = "150X150",
            OutputCrossSectionCode = "16MM"
        };
        var material = new MaterialSpecification
        {
            MaterialSpecificationCode = "FG-16",
            SapMaterialCode = "FG-16",
            Name = "16mm finished bar",
            Stage = SteelMaterialStage.FinishedGoods,
            ProductForm = SteelProductForm.Bar,
            GradeCode = "G1",
            CrossSectionCode = "16MM",
            ProductFamilyCode = "TMT",
            IsActive = true
        };
        return new PlanningMasterDataSnapshot(
            Array.Empty<Plant>(),
            Array.Empty<ProcessStage>(),
            Array.Empty<Resource>(),
            Array.Empty<ResourceCapability>(),
            Array.Empty<ResourceCalendar>(),
            Array.Empty<PlantFlowLink>(),
            Array.Empty<TransitionRule>(),
            Array.Empty<ManufacturingRoute>(),
            new[] { routeOperation },
            Array.Empty<RouteResourceCapability>(),
            SteelGrades: new[] { grade },
            MaterialSpecifications: new[] { material });
    }

    private static ApsDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-demand-regression-{Guid.NewGuid():N}")
            .Options;
        return new ApsDbContext(options);
    }
}
