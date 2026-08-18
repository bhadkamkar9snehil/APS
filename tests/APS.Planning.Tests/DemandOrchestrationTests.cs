using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace APS.Planning.Tests;

public sealed class DemandOrchestrationTests
{
    private static readonly DateTime ReferenceUtc = new(2026, 8, 18, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DueUtc = new(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Full_finished_goods_coverage_creates_no_mto_production_order()
    {
        await using var db = NewDb();
        var service = NewService(db);
        await ReconcileAsync(service, So("450001", "10", 100m, DueUtc));

        var result = await PrepareAsync(service, Inventory(100m));

        var demand = Assert.Single(result.MakeToOrderDemand);
        Assert.Equal(100m, demand.OpenDemandQuantityMt);
        Assert.Equal(100m, demand.FinishedGoodsCoveredQuantityMt);
        Assert.Equal(0m, demand.ManufacturingRequirementQuantityMt);
        Assert.Null(demand.ProductionOrderId);
        Assert.Equal(DemandReconciliationDisposition.FullyCoveredByFinishedGoods, demand.Disposition);
        Assert.Empty(result.ProductionOrders);
        Assert.Empty(await db.ProductionOrders.ToArrayAsync());
    }

    [Fact]
    public async Task Partial_finished_goods_coverage_creates_exact_net_mto_po_and_service_date()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var confirmed = DueUtc.AddHours(-2);
        await ReconcileAsync(service, So("450002", "20", 100m, DueUtc) with { ConfirmedDeliveryDate = confirmed, Priority = 7 });

        var result = await PrepareAsync(
            service,
            Inventory(30m),
            new DemandServiceDatePolicy(QualityLeadMinutes: 120, PackingLeadMinutes: 60, DispatchLeadMinutes: 180));

        var demand = Assert.Single(result.MakeToOrderDemand);
        var po = Assert.Single(result.ProductionOrders);
        Assert.Equal(30m, demand.FinishedGoodsCoveredQuantityMt);
        Assert.Equal(70m, demand.ManufacturingRequirementQuantityMt);
        Assert.Equal(70m, po.PlannedQuantityMt);
        Assert.Equal(70m, po.RemainingQuantityMt);
        Assert.Equal(demand.SalesOrderId, po.SalesOrderId);
        Assert.Equal(confirmed.AddHours(-6), demand.ProductionRequiredByDate);
        Assert.Equal(demand.ProductionRequiredByDate, po.RequiredDate);
        Assert.Equal(7, po.Priority);
        Assert.Equal("MTO-450002-20", po.ProductionOrderNumber);
    }

    [Fact]
    public async Task No_finished_goods_creates_full_manufacturing_requirement_without_component_availability_check()
    {
        await using var db = NewDb();
        var service = NewService(db);
        await ReconcileAsync(service, So("450003", "10", 100m, DueUtc));

        // #45 deliberately receives no billet/raw-material facts here. Component availability belongs to #33/#14.
        var result = await PrepareAsync(service, Array.Empty<InventoryPosition>());

        var po = Assert.Single(result.ProductionOrders);
        Assert.Equal(100m, po.RemainingQuantityMt);
        Assert.Equal(DemandSourceType.MakeToOrder, po.DemandSource);
    }

    [Fact]
    public async Task Compatible_sales_order_items_remain_separate_mto_pos()
    {
        await using var db = NewDb();
        var service = NewService(db);
        await ReconcileAsync(service,
            So("450010", "10", 40m, DueUtc),
            So("450011", "10", 60m, DueUtc.AddDays(2)));

        var result = await PrepareAsync(service, Array.Empty<InventoryPosition>());

        Assert.Equal(2, result.ProductionOrders.Count);
        Assert.Equal(2, result.ProductionOrders.Select(x => x.SalesOrderId).Distinct().Count());
        Assert.Contains(result.ProductionOrders, x => x.ProductionOrderNumber == "MTO-450010-10" && x.RemainingQuantityMt == 40m);
        Assert.Contains(result.ProductionOrders, x => x.ProductionOrderNumber == "MTO-450011-10" && x.RemainingQuantityMt == 60m);
    }

    [Fact]
    public async Task Customer_specific_requirement_is_preserved_before_po_and_copied_to_po_requirement()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var requirement = new SalesOrderRequirementInput(
            QualityClassCode: "Q-CERT",
            SegregationPolicy: SegregationPolicy.SameCustomerOnly,
            RequireVd: true,
            RequiredRouteCode: "R1",
            CutLengthM: 12m,
            MarkingRequirementCode: "MARK-A",
            ChemistryOverrides: new[] { new SalesOrderChemistryRequirementInput("C", MaximumPct: 0.25m) },
            ProcessOverrides: new[]
            {
                new SalesOrderProcessRequirementInput(ProcessOperationType.Vd, RequirementDisposition.Required, "VD-CERT")
            });
        await ReconcileAsync(service, So("450020", "10", 100m, DueUtc) with
        {
            CustomerCode = "CUST-1",
            CustomerGroupCode = "GROUP-A",
            Requirement = requirement
        });

        // Stock has the same material/grade/section, but current inventory evidence cannot prove the special
        // certification fingerprint. APS must not guess that it satisfies the customer requirement.
        var result = await PrepareAsync(service, Inventory(100m));

        var demand = Assert.Single(result.MakeToOrderDemand);
        var po = Assert.Single(result.ProductionOrders);
        Assert.True(demand.RequiresCertifiedFinishedGoodsMatch);
        Assert.NotNull(demand.RequirementQualificationFingerprint);
        Assert.Equal(0m, demand.FinishedGoodsCoveredQuantityMt);
        Assert.Equal(100m, demand.ManufacturingRequirementQuantityMt);
        Assert.NotNull(po.Requirement);
        Assert.Equal("CUST-1", po.Requirement!.CustomerCode);
        Assert.Equal("GROUP-A", po.Requirement.CustomerGroupCode);
        Assert.Equal("Q-CERT", po.Requirement.QualityClassCode);
        Assert.Equal(SegregationPolicy.SameCustomerOnly, po.Requirement.SegregationPolicy);
        Assert.True(po.Requirement.RequireVd);
        Assert.Equal(12m, po.Requirement.CutLengthM);
        Assert.Equal("MARK-A", po.Requirement.MarkingRequirementCode);
        Assert.Single(po.Requirement.ChemistryOverrides);
        Assert.Single(po.Requirement.ProcessOverrides);
    }

    [Fact]
    public async Task Repeated_sales_order_sync_and_prepare_are_idempotent()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var input = So("450030", "10", 100m, DueUtc);

        var firstSync = await ReconcileAsync(service, input);
        var secondSync = await ReconcileAsync(service, input);
        await PrepareAsync(service, Inventory(25m));
        var firstPo = Assert.Single(await db.ProductionOrders.Where(x => x.Status != ProductionOrderStatus.Cancelled).ToArrayAsync());
        await PrepareAsync(service, Inventory(25m));
        var secondPo = Assert.Single(await db.ProductionOrders.Where(x => x.Status != ProductionOrderStatus.Cancelled).ToArrayAsync());

        Assert.Equal(1, firstSync.Created);
        Assert.Equal(1, secondSync.Unchanged);
        Assert.Equal(firstPo.Id, secondPo.Id);
        Assert.Equal(75m, secondPo.RemainingQuantityMt);
    }

    [Fact]
    public async Task Closed_sales_order_cancels_only_uncommitted_planned_po()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var input = So("450040", "10", 100m, DueUtc);
        await ReconcileAsync(service, input);
        await PrepareAsync(service, Array.Empty<InventoryPosition>());
        var po = Assert.Single(await db.ProductionOrders.ToArrayAsync());
        Assert.Equal(ProductionOrderStatus.Planned, po.Status);

        await ReconcileAsync(service, input with { OpenQuantityMt = 0m, ExternalStatus = "CANCELLED" });
        var result = await PrepareAsync(service, Array.Empty<InventoryPosition>());

        var demand = Assert.Single(result.MakeToOrderDemand);
        Assert.Equal(DemandReconciliationDisposition.ProductionOrderCancelled, demand.Disposition);
        Assert.Equal(ProductionOrderStatus.Cancelled, (await db.ProductionOrders.SingleAsync()).Status);
        Assert.Empty(result.ProductionOrders);
    }

    [Fact]
    public async Task Closed_sales_order_does_not_silently_cancel_firmed_po()
    {
        await using var db = NewDb();
        var service = NewService(db);
        var input = So("450041", "10", 100m, DueUtc);
        await ReconcileAsync(service, input);
        await PrepareAsync(service, Array.Empty<InventoryPosition>());
        var po = await db.ProductionOrders.SingleAsync();
        po.Status = ProductionOrderStatus.Firmed;
        await db.SaveChangesAsync();

        await ReconcileAsync(service, input with { OpenQuantityMt = 0m, ExternalStatus = "CANCELLED" });
        var result = await PrepareAsync(service, Array.Empty<InventoryPosition>());

        var demand = Assert.Single(result.MakeToOrderDemand);
        Assert.True(demand.PlannerAttentionRequired);
        Assert.Equal(DemandReconciliationDisposition.CommittedProductionOrderProtected, demand.Disposition);
        Assert.Equal(ProductionOrderStatus.Firmed, (await db.ProductionOrders.SingleAsync()).Status);
        Assert.Contains(po.Id, result.ProductionOrders.Select(x => x.Id));
    }

    [Fact]
    public async Task Held_or_late_finished_goods_do_not_cover_customer_demand()
    {
        await using var db = NewDb();
        var service = NewService(db);
        await ReconcileAsync(service, So("450050", "10", 100m, DueUtc));
        var inventory = new[]
        {
            Inventory(50m, quality: MaterialQualityStatus.QualityHold).Single(),
            Inventory(50m, availableFromUtc: DueUtc.AddHours(1)).Single()
        };

        var result = await PrepareAsync(service, inventory);

        var demand = Assert.Single(result.MakeToOrderDemand);
        Assert.Equal(0m, demand.FinishedGoodsCoveredQuantityMt);
        Assert.Equal(100m, demand.ManufacturingRequirementQuantityMt);
    }

    [Fact]
    public async Task Shared_finished_goods_pool_is_consumed_once_in_service_order()
    {
        await using var db = NewDb();
        var service = NewService(db);
        await ReconcileAsync(service,
            So("450060", "10", 60m, DueUtc),
            So("450061", "10", 60m, DueUtc.AddDays(3)));

        var result = await PrepareAsync(service, Inventory(100m));
        var early = result.MakeToOrderDemand.Single(x => x.SalesOrderNumber == "450060");
        var later = result.MakeToOrderDemand.Single(x => x.SalesOrderNumber == "450061");

        Assert.Equal(60m, early.FinishedGoodsCoveredQuantityMt);
        Assert.Equal(0m, early.ManufacturingRequirementQuantityMt);
        Assert.Equal(40m, later.FinishedGoodsCoveredQuantityMt);
        Assert.Equal(20m, later.ManufacturingRequirementQuantityMt);
        var po = Assert.Single(result.ProductionOrders);
        Assert.Equal(later.SalesOrderId, po.SalesOrderId);
        Assert.Equal(20m, po.RemainingQuantityMt);
    }

    private static ProductionDemandOrchestrationService NewService(ApsDbContext db) =>
        new(db, NullLogger<ProductionDemandOrchestrationService>.Instance);

    private static async Task<SalesOrderReconciliationResult> ReconcileAsync(
        IProductionDemandOrchestrationService service,
        params SalesOrderDemandInput[] inputs) =>
        await service.ReconcileSalesOrdersAsync(inputs);

    private static async Task<DemandOrchestrationResult> PrepareAsync(
        IProductionDemandOrchestrationService service,
        IReadOnlyCollection<InventoryPosition> inventory,
        DemandServiceDatePolicy? serviceDatePolicy = null) =>
        await service.PrepareAsync(
            new PlanningDemandSelection(
                RequiredThroughUtc: DueUtc.AddMonths(2),
                IncludeMakeToStock: false,
                ServiceDatePolicy: serviceDatePolicy),
            inventory,
            Masters(),
            ReferenceUtc,
            DueUtc.AddMonths(2));

    private static SalesOrderDemandInput So(string so, string item, decimal qty, DateTime due) =>
        new(
            so,
            item,
            "FG-16",
            "G1",
            "16MM",
            qty,
            qty,
            due,
            CustomerCode: "CUST",
            CustomerGroupCode: "GROUP",
            ExternalStatus: "OPEN",
            Priority: 1);

    private static IReadOnlyCollection<InventoryPosition> Inventory(
        decimal qty,
        MaterialQualityStatus quality = MaterialQualityStatus.Available,
        DateTime? availableFromUtc = null) =>
        new[]
        {
            new InventoryPosition
            {
                MaterialCode = "FG-16",
                GradeCode = "G1",
                CrossSectionCode = "16MM",
                Stage = InventoryStage.FinishedGoods,
                LocationCode = "FG-YARD",
                AvailableFromUtc = availableFromUtc,
                QualityStatus = quality,
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
            .UseInMemoryDatabase($"aps-demand-{Guid.NewGuid():N}")
            .Options;
        return new ApsDbContext(options);
    }
}
