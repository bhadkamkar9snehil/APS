using APS.Application;
using APS.Domain;
using Xunit;

namespace APS.Planning.Tests;

/// <summary>
/// GitHub #15: compatibility answers "can these coexist?", not "should they coexist in this campaign
/// now?". Campaign composition is an optimization decision over service, campaign economics and the
/// physical grade/heat structure that the selected grouping will actually require.
/// </summary>
public sealed class CampaignCompositionTests
{
    private static readonly DateTime Week1 = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Far_future_order_is_not_pulled_weeks_early_just_because_capacity_remains()
    {
        var due = Order("PO-DUE-NOW", 100m, Week1);
        var distant = Order("PO-SIX-WEEKS-OUT", 100m, Week1.AddDays(42));

        var result = Plan(due, distant);

        Assert.Equal(2, result.Campaigns.Count);
        var nearCampaign = CampaignFor(result, due);
        var farCampaign = CampaignFor(result, distant);
        Assert.NotEqual(nearCampaign.Id, farCampaign.Id);
        Assert.Equal(Week1, nearCampaign.RequiredDate);
        Assert.Equal(Week1.AddDays(42), farCampaign.RequiredDate);
    }

    [Fact]
    public void Two_near_due_orders_are_still_grouped_because_splitting_them_buys_nothing()
    {
        var first = Order("PO-MON", 100m, Week1);
        var second = Order("PO-TUE", 100m, Week1.AddDays(1));

        var result = Plan(first, second);

        var campaign = Assert.Single(result.Campaigns);
        Assert.Equal(200m, campaign.PlannedQuantityMt);
        Assert.Equal(2, campaign.Allocations.Count);
    }

    [Fact]
    public void Composition_does_not_depend_on_the_order_the_requirements_arrive_in()
    {
        var a = Order("PO-A", 90m, Week1);
        var b = Order("PO-B", 80m, Week1.AddDays(10));
        var c = Order("PO-C", 120m, Week1.AddDays(1));
        var d = Order("PO-D", 70m, Week1.AddDays(30));

        var forward = Plan(a, b, c, d);
        var reversed = Plan(d, c, b, a);
        var shuffled = Plan(c, a, d, b);

        Assert.Equal(Signature(forward), Signature(reversed));
        Assert.Equal(Signature(forward), Signature(shuffled));
    }

    [Fact]
    public void Every_order_keeps_its_own_required_date_after_sharing_a_campaign()
    {
        var first = Order("PO-MON", 100m, Week1);
        var second = Order("PO-TUE", 100m, Week1.AddDays(1));

        var result = Plan(first, second);
        var campaign = Assert.Single(result.Campaigns);

        var allocations = campaign.Allocations.ToDictionary(x => x.ProductionOrder!.ProductionOrderNumber);
        Assert.Equal(100m, allocations["PO-MON"].PlannedQuantityMt);
        Assert.Equal(100m, allocations["PO-TUE"].PlannedQuantityMt);
        Assert.Equal(Week1, allocations["PO-MON"].ProductionOrder!.RequiredDate);
        Assert.Equal(Week1.AddDays(1), allocations["PO-TUE"].ProductionOrder!.RequiredDate);
    }

    [Fact]
    public void Make_to_stock_may_fill_an_uneconomic_residual_heat()
    {
        var mto = Order("PO-MTO", 90m, Week1);
        var mts = Order("PO-MTS", 10m, Week1.AddDays(7), DemandSourceType.MakeToStock);

        var result = Plan(mto, mts);

        var campaign = Assert.Single(result.Campaigns);
        Assert.Equal(100m, campaign.PlannedQuantityMt);
        Assert.Equal(2, campaign.Heats.Count);
    }

    [Fact]
    public void Campaign_quantity_in_the_heat_envelope_dead_band_does_not_crash_the_plan()
    {
        var result = Plan(Order("PO-DEAD-BAND", 70m, Week1));

        var campaign = Assert.Single(result.Campaigns);
        Assert.Equal(70m, campaign.PlannedQuantityMt);
        Assert.Equal(2, campaign.Heats.Count);
        Assert.All(campaign.Heats, heat => Assert.True(heat.PlannedQuantityMt <= 55m));
    }

    [Fact]
    public void Chosen_composition_reports_the_alternatives_it_beat_and_why()
    {
        var due = Order("PO-DUE-NOW", 100m, Week1);
        var distant = Order("PO-SIX-WEEKS-OUT", 100m, Week1.AddDays(42));

        var result = Plan(due, distant);

        var decision = Assert.Single(result.CompositionDecisions!);
        Assert.Equal(2, decision.Selected.CampaignCount);
        Assert.Equal(0m, decision.Selected.ServiceRiskMtDays);
        Assert.Equal(0m, decision.Selected.EarlyProductionMtDays);
        Assert.True(decision.Considered.Count > 1);
        Assert.Contains(decision.Considered, x => x.StrategyCode == "DYNAMIC_PARTITION");

        var fillToCapacity = Assert.Single(decision.Considered, x => x.StrategyCode == "FILL_TO_CAPACITY");
        Assert.Equal(1, fillToCapacity.CampaignCount);
        Assert.True(fillToCapacity.EarlyProductionMtDays > decision.Selected.EarlyProductionMtDays);
        Assert.True(fillToCapacity.TotalCost > decision.Selected.TotalCost);
    }

    [Fact]
    public void Weighting_early_production_at_zero_restores_the_old_fill_to_capacity_behaviour()
    {
        var due = Order("PO-DUE-NOW", 100m, Week1);
        var distant = Order("PO-SIX-WEEKS-OUT", 100m, Week1.AddDays(42));

        var result = Plan(
            new[] { due, distant },
            CampaignObjectiveWeights.Default with { EarlyProductionPerMtDay = 0m });

        Assert.Single(result.Campaigns);
    }

    [Fact]
    public void Effective_grade_transition_rules_change_the_selected_grade_sequence()
    {
        var g1 = Order("PO-G1", 50m, Week1, gradeCode: "G1");
        var g2 = Order("PO-G2", 50m, Week1, gradeCode: "G2");
        var g3 = Order("PO-G3", 50m, Week1, gradeCode: "G3");
        var rules = new[]
        {
            GradeRule("G1", "G2", allowed: true),
            GradeRule("G2", "G3", allowed: true),
            GradeRule("G1", "G3", allowed: false),
            GradeRule("G3", "G1", allowed: false),
            GradeRule("G2", "G1", allowed: false),
            GradeRule("G3", "G2", allowed: false)
        };

        var result = PlanWithTransitions(new[] { g3, g1, g2 }, rules);

        var campaign = Assert.Single(result.Campaigns);
        Assert.Equal(new[] { "G1", "G2", "G3" },
            campaign.GradeSequence.OrderBy(x => x.SequenceNumber).Select(x => x.GradeCode).ToArray());
        var decision = Assert.Single(result.CompositionDecisions!);
        Assert.Equal(new[] { "G1", "G2", "G3" }, decision.Selected.GradeSequence);
        Assert.True(decision.Selected.IsTechnicallyFeasible);
    }

    [Fact]
    public void Forbidden_grade_transition_forces_separate_campaigns_instead_of_failing_later()
    {
        var g1 = Order("PO-G1", 50m, Week1, gradeCode: "G1");
        var g2 = Order("PO-G2", 50m, Week1, gradeCode: "G2");
        var rules = new[]
        {
            GradeRule("G1", "G2", allowed: false),
            GradeRule("G2", "G1", allowed: false)
        };

        var result = PlanWithTransitions(new[] { g1, g2 }, rules);

        Assert.Equal(2, result.Campaigns.Count);
        Assert.All(result.Campaigns, campaign => Assert.Single(campaign.GradeSequence));
        var decision = Assert.Single(result.CompositionDecisions!);
        Assert.True(decision.Selected.IsTechnicallyFeasible);
        var fill = Assert.Single(decision.Considered, x => x.StrategyCode == "FILL_TO_CAPACITY");
        Assert.False(fill.IsTechnicallyFeasible);
        Assert.Contains("grade-transition", fill.TechnicalReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Furnace_heat_envelopes_participate_in_composition_before_campaign_is_fixed()
    {
        // Each order alone is below/inside a dead zone for a physical 50..70 MT EAF envelope, but
        // together 120 MT forms two target 60 MT heats. The campaign optimizer must see that before
        // choosing a split rather than discovering it only after campaign identity exists.
        var first = Order("PO-80", 80m, Week1);
        var second = Order("PO-40", 40m, Week1);
        var eaf = new Resource
        {
            Code = "EAF-1",
            Name = "EAF 1",
            ResourceType = ResourceType.Furnace,
            ProcessUnitType = ProcessUnitType.Eaf,
            MinimumHeatWeightMt = 50m,
            NominalHeatWeightMt = 60m,
            MaximumHeatWeightMt = 70m,
            IsActive = true
        };
        var ccm = new Resource
        {
            Code = "CCM-1",
            Name = "CCM 1",
            ResourceType = ResourceType.Caster,
            ProcessUnitType = ProcessUnitType.Ccm,
            IsActive = true
        };

        var result = new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            ProductionOrders: new[] { first, second },
            Inventory: Array.Empty<InventoryPosition>(),
            Policy: new CampaignPlanningPolicy(60m, 50m, 70m, 120m, 200m),
            Resources: new[] { eaf, ccm }));

        var campaign = Assert.Single(result.Campaigns);
        Assert.Equal(120m, campaign.PlannedQuantityMt);
        Assert.Equal(new[] { 60m, 60m }, campaign.Heats.OrderBy(x => x.SequenceNumber).Select(x => x.PlannedQuantityMt).ToArray());
        var decision = Assert.Single(result.CompositionDecisions!);
        Assert.True(decision.Selected.IsTechnicallyFeasible);
        Assert.Equal(0m, decision.Selected.HeatTargetDeviationMt);
    }

    private static Campaign CampaignFor(CampaignPlanningResult result, ProductionOrder order) =>
        Assert.Single(result.Campaigns, campaign => campaign.Allocations.Any(x => x.ProductionOrderId == order.Id));

    private static string Signature(CampaignPlanningResult result) =>
        string.Join(" | ", result.Campaigns
            .Select(campaign => string.Join(
                ",",
                campaign.Allocations
                    .Select(x => $"{x.ProductionOrder!.ProductionOrderNumber}:{x.PlannedQuantityMt:0.###}")
                    .OrderBy(x => x, StringComparer.Ordinal)))
            .OrderBy(x => x, StringComparer.Ordinal));

    private static CampaignPlanningResult Plan(params ProductionOrder[] orders) => Plan(orders, null);

    private static CampaignPlanningResult Plan(
        IReadOnlyCollection<ProductionOrder> orders,
        CampaignObjectiveWeights? weights) =>
        new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            orders,
            Array.Empty<InventoryPosition>(),
            new CampaignPlanningPolicy(50m, 40m, 55m, 150m, 300m, ObjectiveWeights: weights)));

    private static CampaignPlanningResult PlanWithTransitions(
        IReadOnlyCollection<ProductionOrder> orders,
        IReadOnlyCollection<TransitionRule> transitionRules)
    {
        var grades = orders
            .Select(x => x.GradeCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(code => new SteelGrade
            {
                GradeCode = code,
                Description = code,
                SequenceClassCode = "SEQ-A"
            })
            .ToArray();

        return new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            ProductionOrders: orders,
            Inventory: Array.Empty<InventoryPosition>(),
            Policy: new CampaignPlanningPolicy(50m, 40m, 55m, 150m, 300m),
            SteelGrades: grades,
            TransitionRules: transitionRules));
    }

    private static TransitionRule GradeRule(string from, string to, bool allowed, int penalty = 0) => new()
    {
        Dimension = TransitionDimension.Grade,
        ProcessOperationType = ProcessOperationType.Ccm,
        FromCode = from,
        ToCode = to,
        IsAllowed = allowed,
        Penalty = penalty
    };

    private static ProductionOrder Order(
        string number,
        decimal quantityMt,
        DateTime requiredDate,
        DemandSourceType demandSource = DemandSourceType.MakeToOrder,
        string gradeCode = "G1") => new()
    {
        ProductionOrderNumber = number,
        DemandSource = demandSource,
        MaterialCode = "FG-16",
        GradeCode = gradeCode,
        GradeSequenceClassCode = "SEQ-A",
        FinalCrossSectionCode = "16MM",
        CasterSectionCode = "150X150",
        RouteCode = "SMS-RM",
        PlannedQuantityMt = quantityMt,
        RemainingQuantityMt = quantityMt,
        RequiredDate = requiredDate,
        Priority = 5
    };
}
