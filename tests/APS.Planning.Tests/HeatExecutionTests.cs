using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using APS.Planning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class HeatExecutionTests
{
    [Fact]
    public async Task Completed_heat_creates_inventory_that_reduces_next_fresh_steel_requirement()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApsDbContext(options);

        var planVersionId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        db.PlanVersions.Add(new PlanVersion
        {
            Id = planVersionId,
            VersionNumber = "PLAN-TEST",
            CreatedOnUtc = DateTime.UtcNow
        });
        db.PlanVersionStates.Add(new PlanVersionState
        {
            PlanVersionId = planVersionId,
            Status = PlanVersionStatus.Released,
            Trigger = PlanTriggerType.Manual,
            ReferenceTimeUtc = DateTime.UtcNow,
            HorizonStartUtc = DateTime.UtcNow,
            HorizonEndUtc = DateTime.UtcNow.AddDays(1),
            IsActive = true
        });
        db.PlanOperationSnapshots.Add(new PlanOperationSnapshot
        {
            PlanVersionId = planVersionId,
            PlanningKey = "CAST:ABC123",
            SourceEntityId = Guid.NewGuid(),
            OperationType = PlanOperationType.Casting,
            ResourceId = resourceId,
            StartUtc = DateTime.UtcNow,
            EndUtc = DateTime.UtcNow.AddHours(1),
            QuantityMt = 50m,
            GradeCode = "G1",
            CrossSectionCode = "150X150"
        });
        await db.SaveChangesAsync();

        var service = new HeatExecutionService(db);
        var start = new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);
        await service.ApplyAsync(new HeatExecutionUpdate(
            planVersionId,
            "CAST:ABC123",
            HeatExecutionStatus.Running,
            start,
            ExecutionUpdateSource.MesApi,
            "HEAT-EVT-1",
            "H-1001",
            "CAST-77"));

        var outputs = new[]
        {
            new StrandMaterialActualInput(1, 1, "BILLET-001", "BILLET-G1", "G1", "150X150", 24m, start.AddMinutes(55), "YARD-A", ChargeMode.HotBuffered, 1060m, start.AddMinutes(55)),
            new StrandMaterialActualInput(2, 1, "BILLET-002", "BILLET-G1", "G1", "150X150", 25m, start.AddMinutes(55), "YARD-A", ChargeMode.HotBuffered, 1060m, start.AddMinutes(55))
        };
        var completed = new HeatExecutionUpdate(
            planVersionId,
            "CAST:ABC123",
            HeatExecutionStatus.Completed,
            start.AddMinutes(55),
            ExecutionUpdateSource.MesApi,
            "HEAT-EVT-2",
            "H-1001",
            "CAST-77",
            resourceId,
            start,
            start.AddMinutes(55),
            49m,
            outputs);

        var first = await service.ApplyAsync(completed);
        var retry = await service.ApplyAsync(completed);

        Assert.Equal(first.HeatExecutionActualId, retry.HeatExecutionActualId);
        Assert.Equal(49m, first.ActualQuantityMt);
        Assert.Equal(2, await db.MaterialLots.CountAsync());
        Assert.All(await db.MaterialLots.ToListAsync(), lot =>
        {
            Assert.Equal(MaterialLotStatus.Available, lot.Status);
            Assert.Equal(InventoryStage.CastIntermediate, lot.Stage);
            Assert.Equal(ChargeMode.HotBuffered, lot.ThermalState);
            Assert.Equal(1060m, lot.EstimatedTemperatureC);
            Assert.Equal(start.AddMinutes(55), lot.TemperatureObservedOnUtc);
        });
        Assert.Equal(2, await db.StrandMaterialActuals.CountAsync());
        Assert.Equal(2, await db.HeatExecutionActuals.CountAsync());

        var inventory = await new SqlInventorySnapshotProvider(db).GetInventoryAsync();
        var billet = Assert.Single(inventory);
        Assert.Equal(49m, billet.ProjectedAvailableQuantityMt);
        Assert.Equal(InventoryStage.CastIntermediate, billet.Stage);
        Assert.Equal(BilletThermalSourceBasis.ActualMeasurement, billet.ThermalBasis);
        Assert.Equal(1060m, billet.EstimatedTemperatureC);

        var po = new ProductionOrder
        {
            ProductionOrderNumber = "PO-NEXT",
            DemandSource = DemandSourceType.MakeToOrder,
            MaterialCode = "FG-16",
            GradeCode = "G1",
            GradeSequenceClassCode = "SEQ-A",
            FinalCrossSectionCode = "16MM",
            CasterSectionCode = "150X150",
            RouteCode = "SMS-RM",
            PlannedQuantityMt = 100m,
            RemainingQuantityMt = 100m,
            RequiredDate = new DateTime(2026, 8, 22)
        };
        var campaignPlan = new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            new[] { po },
            inventory,
            new CampaignPlanningPolicy(50m, 25m, 55m, 250m, 300m)));

        Assert.Equal(100m, campaignPlan.RollingRequirementsMt[po.Id]);
        Assert.Equal(49m, campaignPlan.IntermediateInventoryAllocatedMt[po.Id]);
        Assert.Equal(51m, campaignPlan.FreshSteelRequirementsMt[po.Id]);
        var allocation = Assert.Single(campaignPlan.InventoryAllocations, x => x.Use == PlanningInventoryUse.IntermediateFeed);
        Assert.Equal(BilletThermalSourceBasis.ActualMeasurement, allocation.ThermalBasis);
        Assert.Equal(1060m, allocation.EstimatedTemperatureC);
    }
}
