using APS.Application;
using APS.Domain;

namespace APS.UI.Sample;

/// <summary>
/// A small, self-contained long-products (billet caster + bar/rod mill) scenario used by the
/// Planning Sandbox page so the planning pipeline can be exercised without a configured database.
/// </summary>
public static class SteelPlantSampleScenario
{
    private const string RouteCode = "STD";
    private const string BilletSection = "BLT130";
    private const string Rebar12 = "REBAR-12MM";
    private const string Rebar16 = "REBAR-16MM";
    private const string MerchantBar50 = "MBAR-50";

    public const string GradeRebar = "B500B";
    public const string GradeStructural = "S355";
    public const string GradeUnconfigured = "X999";

    public static PlanningRunRequest BuildRequest(DateTime horizonStartUtc, int horizonDays, int maxSolverSeconds, bool includeInfeasibleOrder)
    {
        var plantId = Guid.NewGuid();
        var smsStageId = Guid.NewGuid();
        var rollingStageId = Guid.NewGuid();

        var caster1 = NewResource(plantId, smsStageId, "CASTER-1", "Caster 1", ResourceType.Caster, strands: 4);
        var caster2 = NewResource(plantId, smsStageId, "CASTER-2", "Caster 2", ResourceType.Caster, strands: 6);
        var millA = NewResource(plantId, rollingStageId, "MILL-A", "Rebar Mill A", ResourceType.RollingMill);
        var millB = NewResource(plantId, rollingStageId, "MILL-B", "Merchant Bar Mill B", ResourceType.RollingMill);
        var resources = new[] { caster1, caster2, millA, millB };

        var capabilities = new List<ResourceCapability>
        {
            Capability(caster1.Id, route: RouteCode, output: BilletSection, throughputMtPerHour: 90m),
            Capability(caster2.Id, route: RouteCode, output: BilletSection, throughputMtPerHour: 110m),
            Capability(millA.Id, route: RouteCode, grade: GradeRebar, input: BilletSection, output: Rebar12, throughputMtPerHour: 60m),
            Capability(millA.Id, route: RouteCode, grade: GradeRebar, input: BilletSection, output: Rebar16, throughputMtPerHour: 55m),
            Capability(millB.Id, route: RouteCode, grade: GradeStructural, input: BilletSection, output: MerchantBar50, throughputMtPerHour: 50m),
        };

        var transitionRules = new[]
        {
            new TransitionRule
            {
                ResourceType = ResourceType.Caster,
                Dimension = TransitionDimension.Grade,
                FromCode = GradeRebar,
                ToCode = GradeStructural,
                IsAllowed = true,
                Penalty = 300,
                TransitionTime = TimeSpan.FromMinutes(45)
            },
            new TransitionRule
            {
                ResourceType = ResourceType.Caster,
                Dimension = TransitionDimension.Grade,
                FromCode = GradeStructural,
                ToCode = GradeRebar,
                IsAllowed = true,
                Penalty = 300,
                TransitionTime = TimeSpan.FromMinutes(45)
            },
            new TransitionRule
            {
                ResourceType = ResourceType.RollingMill,
                Dimension = TransitionDimension.CrossSection,
                FromCode = Rebar12,
                ToCode = Rebar16,
                IsAllowed = true,
                Penalty = 100,
                TransitionTime = TimeSpan.FromMinutes(20)
            },
            new TransitionRule
            {
                ResourceType = ResourceType.RollingMill,
                Dimension = TransitionDimension.CrossSection,
                FromCode = Rebar16,
                ToCode = Rebar12,
                IsAllowed = true,
                Penalty = 100,
                TransitionTime = TimeSpan.FromMinutes(20)
            }
        };

        var flowLinks = new[]
        {
            FlowLink(caster1.Id, millA.Id),
            FlowLink(caster2.Id, millA.Id),
            FlowLink(caster1.Id, millB.Id),
            FlowLink(caster2.Id, millB.Id),
        };

        var calendars = new[]
        {
            new ResourceCalendar
            {
                ResourceId = caster1.Id,
                Start = horizonStartUtc.AddHours(30),
                End = horizonStartUtc.AddHours(34),
                IsAvailable = false,
                ReasonCode = "PLANNED_MAINTENANCE"
            }
        };

        var now = horizonStartUtc;
        var productionOrders = new List<ProductionOrder>
        {
            Order("PO-1001", DemandSourceType.MakeToOrder, GradeRebar, Rebar12, 320m, now.AddDays(3), priority: 5),
            Order("PO-1002", DemandSourceType.MakeToOrder, GradeRebar, Rebar16, 180m, now.AddDays(2), priority: 8),
            Order("PO-1003", DemandSourceType.MakeToOrder, GradeStructural, MerchantBar50, 250m, now.AddDays(5), priority: 4),
            Order("PO-1004", DemandSourceType.MakeToStock, GradeRebar, Rebar12, 150m, now.AddDays(6), priority: 2, stockPolicyCode: "STK-REBAR12"),
            Order("PO-1005", DemandSourceType.MakeToOrder, GradeStructural, MerchantBar50, 90m, now.AddDays(4), priority: 6),
            Order("PO-1006", DemandSourceType.MakeToStock, GradeRebar, Rebar16, 60m, now.AddDays(7), priority: 1, stockPolicyCode: "STK-REBAR16"),
        };

        if (includeInfeasibleOrder)
        {
            productionOrders.Add(Order("PO-9999", DemandSourceType.MakeToOrder, GradeUnconfigured, "UNKNOWN-SECTION", 40m, now.AddDays(2), priority: 9));
        }

        var inventory = new[]
        {
            new InventoryPosition
            {
                MaterialCode = "FG-" + Rebar12,
                GradeCode = GradeRebar,
                CrossSectionCode = Rebar12,
                Stage = InventoryStage.FinishedGoods,
                LocationCode = "FG-YARD",
                AvailableQuantityMt = 40m
            },
            new InventoryPosition
            {
                MaterialCode = "BLT-" + GradeStructural,
                GradeCode = GradeStructural,
                CrossSectionCode = BilletSection,
                Stage = InventoryStage.CastIntermediate,
                LocationCode = "SMS-YARD",
                AvailableQuantityMt = 30m
            }
        };

        var campaignPolicy = new CampaignPlanningPolicy(
            NominalHeatSizeMt: 60m,
            MinimumHeatSizeMt: 45m,
            MaximumHeatSizeMt: 75m,
            TargetCampaignQuantityMt: 300m,
            MaximumCampaignQuantityMt: 400m,
            AllowMtoMtsMixing: true,
            AllowMixedGradesWithinSequenceClass: true,
            ExpectedCastingYieldPct: 96m);

        var structurePolicy = new ProductionStructurePlanningPolicy(
            MaximumHeatsPerCastSequence: 8,
            DefaultCastingMinutesPerHeat: 55,
            SequenceBreakPenalty: 500,
            CastingYieldPct: 96m,
            DefaultRollingMinutesPer100Mt: 110,
            AllowCrossCampaignCastSequences: true,
            AllowCrossCampaignRollingPlans: true);

        return new PlanningRunRequest(
            productionOrders,
            inventory,
            resources,
            capabilities,
            calendars,
            transitionRules,
            flowLinks,
            campaignPolicy,
            structurePolicy,
            horizonStartUtc,
            horizonStartUtc.AddDays(horizonDays),
            maxSolverSeconds,
            "CMP");
    }

    private static Resource NewResource(Guid plantId, Guid stageId, string code, string name, ResourceType type, int? strands = null) => new()
    {
        PlantId = plantId,
        ProcessStageId = stageId,
        Code = code,
        Name = name,
        ResourceType = type,
        StrandCount = strands,
        IsActive = true
    };

    private static ResourceCapability Capability(
        Guid resourceId, string route, decimal throughputMtPerHour,
        string? grade = null, string? input = null, string? output = null) => new()
    {
        ResourceId = resourceId,
        RouteCode = route,
        GradeCode = grade,
        InputCrossSectionCode = input,
        OutputCrossSectionCode = output,
        ThroughputMtPerHour = throughputMtPerHour
    };

    private static PlantFlowLink FlowLink(Guid fromResourceId, Guid toResourceId) => new()
    {
        FromResourceId = fromResourceId,
        ToResourceId = toResourceId,
        CouplingType = FlowCouplingType.HotTransfer,
        MinimumTransferTime = TimeSpan.FromMinutes(20),
        MaximumTransferTime = TimeSpan.FromHours(8),
        SupportsHotTransfer = true,
        IsEnabled = true
    };

    private static ProductionOrder Order(
        string number, DemandSourceType source, string grade, string finalSection,
        decimal quantityMt, DateTime requiredDate, int priority, string? stockPolicyCode = null) => new()
    {
        ProductionOrderNumber = number,
        DemandSource = source,
        MaterialCode = $"{grade}-{finalSection}",
        GradeCode = grade,
        GradeSequenceClassCode = "SEQ-A",
        FinalCrossSectionCode = finalSection,
        CasterSectionCode = BilletSection,
        RouteCode = RouteCode,
        PlannedQuantityMt = quantityMt,
        RemainingQuantityMt = quantityMt,
        RequiredDate = requiredDate,
        Priority = priority,
        Status = ProductionOrderStatus.Planned,
        StockPolicyCode = stockPolicyCode
    };
}
