using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class PlanningWorkbenchQueryTests
{
    [Fact]
    public async Task Aggregates_schedule_demand_campaign_material_and_exceptions_for_one_plan()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApsDbContext(options);

        var planId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var productionOrderId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var heatId = Guid.NewGuid();
        var start = new DateTime(2026, 8, 21, 8, 0, 0, DateTimeKind.Utc);

        db.PlanVersions.Add(new PlanVersion
        {
            Id = planId,
            VersionNumber = "PLAN-20260821-01",
            CreatedOnUtc = start
        });
        db.PlanVersionStates.Add(new PlanVersionState
        {
            PlanVersionId = planId,
            Status = PlanVersionStatus.Feasible,
            Trigger = PlanTriggerType.Manual,
            ReferenceTimeUtc = start,
            HorizonStartUtc = start,
            HorizonEndUtc = start.AddDays(7),
            SolverStatus = "Optimal",
            IsActive = true
        });
        db.Resources.Add(new Resource
        {
            Id = resourceId,
            Code = "EAF1-A",
            Name = "Electric Arc Furnace 1",
            ResourceType = ResourceType.Furnace,
            ProcessUnitType = ProcessUnitType.Eaf,
            OperatingState = ResourceOperatingState.Available
        });
        db.PlanProductionOrderSnapshots.Add(new PlanProductionOrderSnapshot
        {
            PlanVersionId = planId,
            ProductionOrderId = productionOrderId,
            ProductionOrderNumber = "MTO-SO-1001-10",
            DemandSource = DemandSourceType.MakeToOrder,
            SalesOrderNumber = "SO-1001",
            SalesOrderItemNumber = "10",
            MaterialCode = "BAR-SAE1008-12",
            GradeCode = "SAE1008",
            FinalCrossSectionCode = "RND-12",
            CasterSectionCode = "BLT-150SQ",
            RouteCode = "STD-BAR",
            PlannedQuantityMt = 75m,
            RemainingQuantityMt = 75m,
            RequiredDate = start.AddDays(1),
            Status = ProductionOrderStatus.Planned,
            FreshSteelRequirementMt = 70m
        });
        db.PlanCampaignSnapshots.Add(new PlanCampaignSnapshot
        {
            PlanVersionId = planId,
            CampaignId = campaignId,
            CampaignNumber = "CMP-00001",
            GradeSequenceClassCode = "LOW-C",
            CasterSectionCode = "BLT-150SQ",
            RouteCode = "STD-BAR",
            PlannedQuantityMt = 70m,
            FreshSteelRequirementMt = 70m,
            RequiredDate = start.AddDays(1),
            Status = CampaignStatus.Planned
        });
        db.PlanCampaignAllocationSnapshots.Add(new PlanCampaignAllocationSnapshot
        {
            PlanVersionId = planId,
            CampaignId = campaignId,
            ProductionOrderId = productionOrderId,
            PlannedQuantityMt = 70m,
            FreshSteelQuantityMt = 70m
        });
        db.PlanHeatSnapshots.Add(new PlanHeatSnapshot
        {
            PlanVersionId = planId,
            CampaignHeatId = heatId,
            CampaignId = campaignId,
            SequenceNumber = 1,
            GradeCode = "SAE1008",
            PlannedQuantityMt = 70m
        });
        db.PlanOperationSnapshots.Add(new PlanOperationSnapshot
        {
            PlanVersionId = planId,
            PlanningKey = "HEAT:CMP-00001:H01:EAF",
            SourceEntityId = heatId,
            OperationType = PlanOperationType.Eaf,
            ProcessOperationType = ProcessOperationType.Eaf,
            ResourceId = resourceId,
            StartUtc = start,
            EndUtc = start.AddHours(1),
            QuantityMt = 70m,
            GradeCode = "SAE1008",
            CrossSectionCode = "BLT-150SQ"
        });
        db.PlanOperationResourceOptionSnapshots.Add(new PlanOperationResourceOptionSnapshot
        {
            PlanVersionId = planId,
            PlanningKey = "HEAT:CMP-00001:H01:EAF",
            SourceEntityId = heatId,
            ProcessOperationType = ProcessOperationType.Eaf,
            ResourceId = resourceId,
            DurationMinutes = 60,
            WasSelected = true,
            EligibilityBasisCode = "ROUTE_GRADE_CAPABILITY"
        });
        await db.SaveChangesAsync();

        var service = new PlannerWorkspaceQueryService(db, new PlanVersionRepository(db));
        var result = await service.GetPlanningWorkbenchAsync(planId);

        Assert.NotNull(result);
        Assert.Equal("PLAN-20260821-01", result.Plan.VersionNumber);
        Assert.Single(result.Schedule.ResourceLanes);
        Assert.Single(result.Demand.Rows);
        Assert.Single(result.Campaigns.Campaigns);
        Assert.Single(result.OperationDetails);
        Assert.Single(result.OperationDetails.Single().ResourceOptions);
        Assert.Empty(result.Material.Pools);
        Assert.Equal(1, result.Queue.TotalDemand);
        Assert.Equal(0, result.Queue.UnscheduledDemand);
        Assert.Contains(result.Exceptions, x => x.Kind == PlanningWorkbenchExceptionKind.UncoveredDemand);
    }
}
