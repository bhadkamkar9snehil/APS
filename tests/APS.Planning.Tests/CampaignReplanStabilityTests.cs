using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class CampaignReplanStabilityTests
{
    private static readonly DateTime Due = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Replan_prefers_baseline_campaign_membership_when_service_and_feasibility_are_equal()
    {
        var first = Order("PO-A", "G1", 100m);
        var second = Order("PO-B", "G1", 100m);
        var oldCampaignA = Guid.NewGuid();
        var oldCampaignB = Guid.NewGuid();
        var policy = Policy() with
        {
            BaselineCampaignAllocations = new[]
            {
                new BaselineCampaignAllocation(oldCampaignA, first.Id, 100m),
                new BaselineCampaignAllocation(oldCampaignB, second.Id, 100m)
            }
        };

        var result = Plan(policy, first, second);

        Assert.Equal(2, result.Campaigns.Count);
        var decision = Assert.Single(result.CompositionDecisions!);
        Assert.Equal("BASELINE_STABILITY", decision.Selected.StrategyCode);
        Assert.Equal(0m, decision.Selected.CampaignStabilityChangedMt);

        var merged = Assert.Single(decision.Considered, x => x.StrategyCode == "FILL_TO_CAPACITY");
        Assert.Equal(1, merged.CampaignCount);
        Assert.Equal(100m, merged.CampaignStabilityChangedMt);
        Assert.True(merged.TotalCost > decision.Selected.TotalCost);
    }

    [Fact]
    public void Hard_grade_transition_prohibition_overrides_baseline_stability()
    {
        var first = Order("PO-G1", "G1", 100m);
        var second = Order("PO-G2", "G2", 100m);
        var oldCampaign = Guid.NewGuid();
        var policy = Policy() with
        {
            BaselineCampaignAllocations = new[]
            {
                new BaselineCampaignAllocation(oldCampaign, first.Id, 100m),
                new BaselineCampaignAllocation(oldCampaign, second.Id, 100m)
            }
        };
        var forbiddenTransitions = new[]
        {
            new TransitionRule
            {
                Dimension = TransitionDimension.Grade,
                FromCode = "G1",
                ToCode = "G2",
                IsAllowed = false,
                ProcessOperationType = ProcessOperationType.Ccm
            },
            new TransitionRule
            {
                Dimension = TransitionDimension.Grade,
                FromCode = "G2",
                ToCode = "G1",
                IsAllowed = false,
                ProcessOperationType = ProcessOperationType.Ccm
            }
        };

        var result = new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            new[] { first, second },
            Array.Empty<InventoryPosition>(),
            policy,
            TransitionRules: forbiddenTransitions));

        Assert.Equal(2, result.Campaigns.Count);
        var decision = Assert.Single(result.CompositionDecisions!);
        Assert.True(decision.Selected.IsTechnicallyFeasible);
        Assert.Equal(100m, decision.Selected.CampaignStabilityChangedMt);
        var baseline = Assert.Single(decision.Considered, x => x.StrategyCode == "BASELINE_STABILITY");
        Assert.False(baseline.IsTechnicallyFeasible);
        Assert.Contains("grade-transition", baseline.TechnicalReason!.ToLowerInvariant());
    }

    [Fact]
    public void New_demand_is_not_counted_as_campaign_instability()
    {
        var existing = Order("PO-EXISTING", "G1", 100m);
        var newDemand = Order("PO-NEW", "G1", 100m);
        var policy = Policy() with
        {
            BaselineCampaignAllocations = new[]
            {
                new BaselineCampaignAllocation(Guid.NewGuid(), existing.Id, 100m)
            }
        };

        var result = Plan(policy, existing, newDemand);

        var selected = Assert.Single(result.CompositionDecisions!).Selected;
        Assert.Equal(0m, selected.CampaignStabilityChangedMt);
    }

    [Fact]
    public async Task Plan_version_repository_reads_historical_campaign_membership_for_replan()
    {
        await using var db = new ApsDbContext(new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-campaign-baseline-{Guid.NewGuid():N}")
            .Options);
        var planVersionId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var productionOrderId = Guid.NewGuid();
        db.PlanCampaignAllocationSnapshots.Add(new PlanCampaignAllocationSnapshot
        {
            PlanVersionId = planVersionId,
            CampaignId = campaignId,
            ProductionOrderId = productionOrderId,
            PlannedQuantityMt = 87.5m,
            ExistingIntermediateInventoryMt = 20m,
            FreshSteelQuantityMt = 67.5m
        });
        await db.SaveChangesAsync();

        var rows = await new PlanVersionRepository(db).GetBaselineCampaignAllocationsAsync(planVersionId);

        var row = Assert.Single(rows);
        Assert.Equal(campaignId, row.CampaignId);
        Assert.Equal(productionOrderId, row.ProductionOrderId);
        Assert.Equal(87.5m, row.PlannedQuantityMt);
    }

    private static CampaignPlanningResult Plan(CampaignPlanningPolicy policy, params ProductionOrder[] orders) =>
        new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            orders,
            Array.Empty<InventoryPosition>(),
            policy));

    private static CampaignPlanningPolicy Policy() =>
        new(50m, 40m, 55m, 50m, 300m);

    private static ProductionOrder Order(string number, string grade, decimal quantityMt) => new()
    {
        ProductionOrderNumber = number,
        DemandSource = DemandSourceType.MakeToOrder,
        MaterialCode = "FG-16",
        GradeCode = grade,
        GradeSequenceClassCode = "SEQ-A",
        FinalCrossSectionCode = "16MM",
        CasterSectionCode = "150X150",
        RouteCode = "SMS-RM",
        PlannedQuantityMt = quantityMt,
        RemainingQuantityMt = quantityMt,
        RequiredDate = Due,
        Priority = 5
    };
}
