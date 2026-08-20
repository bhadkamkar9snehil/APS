using APS.Application;
using APS.Domain;
using Xunit;

namespace APS.Planning.Tests;

/// <summary>
/// GitHub #15: compatibility answers "can these coexist?", not "should they coexist in this campaign
/// now?". Filling every campaign to its maximum in due-date order answered the second question by
/// accident - a far-future order joined a campaign because tonnage was left over, was produced weeks
/// early, and the campaign's service date collapsed to the earliest order in it.
/// </summary>
public sealed class CampaignCompositionTests
{
    private static readonly DateTime Week1 = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Far_future_order_is_not_pulled_weeks_early_just_because_capacity_remains()
    {
        // Both are compatible and together fit inside one campaign, so sort-and-fill merged them and
        // produced the October order in September.
        var due = Order("PO-DUE-NOW", 100m, Week1);
        var distant = Order("PO-SIX-WEEKS-OUT", 100m, Week1.AddDays(42));

        var result = Plan(due, distant);

        Assert.Equal(2, result.Campaigns.Count);
        var nearCampaign = CampaignFor(result, due);
        var farCampaign = CampaignFor(result, distant);
        Assert.NotEqual(nearCampaign.Id, farCampaign.Id);
        // Each campaign now carries its own service date rather than both collapsing to the earliest.
        Assert.Equal(Week1, nearCampaign.RequiredDate);
        Assert.Equal(Week1.AddDays(42), farCampaign.RequiredDate);
    }

    [Fact]
    public void Two_near_due_orders_are_still_grouped_because_splitting_them_buys_nothing()
    {
        // A day apart: separating them would cost a whole extra campaign and leave two part-filled
        // heats, for one day of avoided early production. Efficiency should win here.
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

        // Selection is by objective score, so permuting the input cannot change the answer. Under
        // sort-and-fill the packing sequence itself was the decision.
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

        // A campaign may expose an earliest date for summary, but the allocations must still be
        // traceable to their own Production Orders and quantities.
        var allocations = campaign.Allocations.ToDictionary(x => x.ProductionOrder!.ProductionOrderNumber);
        Assert.Equal(100m, allocations["PO-MON"].PlannedQuantityMt);
        Assert.Equal(100m, allocations["PO-TUE"].PlannedQuantityMt);
        Assert.Equal(Week1, allocations["PO-MON"].ProductionOrder!.RequiredDate);
        Assert.Equal(Week1.AddDays(1), allocations["PO-TUE"].ProductionOrder!.RequiredDate);
    }

    [Fact]
    public void Make_to_stock_may_fill_an_uneconomic_residual_heat()
    {
        // 90 MT on a 50 MT nominal heat leaves 10 MT of a second heat unfilled. A 10 MT replenishment
        // order due a week later fills exactly that gap.
        var mto = Order("PO-MTO", 90m, Week1);
        var mts = Order("PO-MTS", 10m, Week1.AddDays(7), DemandSourceType.MakeToStock);

        var result = Plan(mto, mts);

        // Replenishment stock carries no customer promise, so pulling it forward is cheap relative to
        // casting a near-empty heat - unlike the far-future customer order above.
        var campaign = Assert.Single(result.Campaigns);
        Assert.Equal(100m, campaign.PlannedQuantityMt);
        Assert.Equal(2, campaign.Heats.Count);
    }

    [Fact]
    public void Campaign_quantity_in_the_heat_envelope_dead_band_does_not_crash_the_plan()
    {
        // 70 MT against a 40/55 min/max heat envelope needs more than one heat but cannot fill two.
        // Math.Clamp was handed crossed bounds and threw, taking the whole plan down with an
        // arithmetic message rather than producing a campaign.
        var result = Plan(Order("PO-DEAD-BAND", 70m, Week1));

        var campaign = Assert.Single(result.Campaigns);
        Assert.Equal(70m, campaign.PlannedQuantityMt);
        // The furnace maximum is physical and the minimum is economic, so it runs two heats with one
        // under-filled rather than one heat above what the furnace holds.
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
        // A single opaque score is not an explanation; each component has to be readable on its own.
        Assert.Equal(2, decision.Selected.CampaignCount);
        Assert.Equal(0m, decision.Selected.ServiceRiskMtDays);
        Assert.Equal(0m, decision.Selected.EarlyProductionMtDays);
        Assert.True(decision.Considered.Count > 1);

        // The rejected fill-to-capacity alternative is the one that used to be taken unconditionally,
        // and its early-production cost is exactly why it lost.
        var fillToCapacity = Assert.Single(decision.Considered, x => x.StrategyCode == "FILL_TO_CAPACITY");
        Assert.Equal(1, fillToCapacity.CampaignCount);
        Assert.True(
            fillToCapacity.EarlyProductionMtDays > decision.Selected.EarlyProductionMtDays,
            "The single-campaign alternative should be penalised for producing the distant order early.");
        Assert.True(fillToCapacity.TotalCost > decision.Selected.TotalCost);
    }

    [Fact]
    public void Weighting_early_production_at_zero_restores_the_old_fill_to_capacity_behaviour()
    {
        var due = Order("PO-DUE-NOW", 100m, Week1);
        var distant = Order("PO-SIX-WEEKS-OUT", 100m, Week1.AddDays(42));

        // The lever is real and inspectable: a plant that genuinely does not care about early
        // production gets the packed campaign back, and it is visible in the weights why.
        var result = Plan(
            new[] { due, distant },
            CampaignObjectiveWeights.Default with { EarlyProductionPerMtDay = 0m });

        Assert.Single(result.Campaigns);
    }

    private static Campaign CampaignFor(CampaignPlanningResult result, ProductionOrder order) =>
        Assert.Single(result.Campaigns, campaign => campaign.Allocations.Any(x => x.ProductionOrderId == order.Id));

    /// <summary>Campaign composition reduced to what it groups, independent of campaign numbering.</summary>
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

    private static ProductionOrder Order(
        string number,
        decimal quantityMt,
        DateTime requiredDate,
        DemandSourceType demandSource = DemandSourceType.MakeToOrder) => new()
    {
        ProductionOrderNumber = number,
        DemandSource = demandSource,
        MaterialCode = "FG-16",
        GradeCode = "G1",
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
