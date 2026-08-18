using System.Text.Json;
using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using APS.Planning;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class ReplanningActualStateTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Running_actual_overrides_baseline_and_completed_heat_is_removed()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApsDbContext(options);

        var planId = Guid.NewGuid();
        var caster = Guid.NewGuid();
        var mill = Guid.NewGuid();
        var start = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);
        db.PlanVersions.Add(new PlanVersion { Id = planId, VersionNumber = "PLAN-1", CreatedOnUtc = start });
        db.PlanVersionStates.Add(new PlanVersionState
        {
            PlanVersionId = planId,
            Status = PlanVersionStatus.Released,
            Trigger = PlanTriggerType.Manual,
            ReferenceTimeUtc = start,
            HorizonStartUtc = start,
            HorizonEndUtc = start.AddDays(1),
            IsActive = true
        });

        var completedKey = "CAST:DONE";
        var runningKey = "ROLL:RUN";
        db.HeatExecutionActuals.Add(new HeatExecutionActual
        {
            PlanVersionId = planId,
            PlanningKey = completedKey,
            Status = HeatExecutionStatus.Completed,
            CasterResourceId = caster,
            ActualStartUtc = start,
            ActualEndUtc = start.AddMinutes(50),
            ActualQuantityMt = 48m,
            ChangedOnUtc = start.AddMinutes(50),
            Source = ExecutionUpdateSource.Manual
        });

        var wo = new WorkOrder
        {
            WorkOrderNumber = "RM-1",
            WorkOrderType = WorkOrderType.HotRolling,
            ResourceId = mill,
            MaterialCode = "FG-16",
            GradeCode = "G1",
            CrossSectionCode = "16MM",
            PlannedQuantityMt = 50m,
            ActualStart = start.AddHours(2).AddMinutes(15),
            Status = WorkOrderStatus.Running
        };
        db.WorkOrders.Add(wo);
        db.ScheduledOperations.Add(new ScheduledOperation
        {
            PlanVersionId = planId,
            WorkOrderId = wo.Id,
            ResourceId = mill,
            PlanningKey = runningKey,
            Start = start.AddHours(2),
            End = start.AddHours(3)
        });
        db.MaterialLots.Add(new MaterialLot
        {
            LotNumber = "B-1",
            MaterialCode = "BILLET-G1",
            GradeCode = "G1",
            CrossSectionCode = "150X150",
            Stage = InventoryStage.CastIntermediate,
            QuantityMt = 48m,
            Status = MaterialLotStatus.Available
        });
        await db.SaveChangesAsync();

        var baseline = new[]
        {
            new BaselinePlanOperation(completedKey, caster, start, start.AddMinutes(50), FiniteScheduleTaskType.Casting),
            new BaselinePlanOperation(runningKey, mill, start.AddHours(2), start.AddHours(3), FiniteScheduleTaskType.HotRolling)
        };
        var provider = new ReplanningActualStateProvider(db, new SqlInventorySnapshotProvider(db));

        var state = await provider.GetAsync(planId, start.AddHours(2).AddMinutes(20), baseline);

        Assert.Contains(completedKey, state.CompletedPlanningKeys);
        Assert.DoesNotContain(state.BaselineOperations, x => x.PlanningKey == completedKey);
        var running = Assert.Single(state.BaselineOperations, x => x.PlanningKey == runningKey);
        Assert.Equal(wo.ActualStart, running.StartUtc);
        Assert.Contains(runningKey, state.RunningPlanningKeys);
        Assert.Equal(48m, Assert.Single(state.Inventory).ProjectedAvailableQuantityMt);
    }

    [Fact]
    public async Task Released_ccm_receipt_is_future_supply_and_does_not_create_replacement_heat()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApsDbContext(options);

        var now = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var planId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var heatId = Guid.NewGuid();
        var caster = Guid.NewGuid();
        var ccmKey = "HEAT:H100:CCM";
        var receipt = new MaterialBalanceEvent
        {
            PlanVersionId = planId,
            EventType = MaterialBalanceEventType.PlannedProductionReceipt,
            MaterialPoolKey = $"PO:{poId:N}|GRADE:G1|SECTION:150X150",
            MaterialSpecificationCode = "BILLET-G1",
            GradeCode = "G1",
            CrossSectionCode = "150X150",
            QuantityDeltaMt = 100m,
            EffectiveAtUtc = now.AddHours(4),
            ProductionOrderId = poId,
            CampaignHeatId = heatId
        };
        db.PlanVersions.Add(new PlanVersion { Id = planId, VersionNumber = "PLAN-RELEASED", CreatedOnUtc = now });
        db.PlanVersionStates.Add(new PlanVersionState
        {
            PlanVersionId = planId,
            Status = PlanVersionStatus.Released,
            Trigger = PlanTriggerType.Manual,
            ReferenceTimeUtc = now,
            HorizonStartUtc = now,
            HorizonEndUtc = now.AddDays(2),
            IsActive = true,
            MaterialLedgerJson = JsonSerializer.Serialize(new[] { receipt }, JsonOptions)
        });
        db.PlanOperationSnapshots.Add(new PlanOperationSnapshot
        {
            PlanVersionId = planId,
            PlanningKey = ccmKey,
            SourceEntityId = heatId,
            OperationType = PlanOperationType.Casting,
            ProcessOperationType = ProcessOperationType.Ccm,
            ResourceId = caster,
            CommittedResourceId = caster,
            AssignmentCommitmentState = OperationAssignmentCommitmentState.Committed,
            ExecutionStatus = OperationExecutionStatus.Ready,
            StartUtc = now.AddHours(3),
            EndUtc = now.AddHours(4),
            QuantityMt = 100m,
            GradeCode = "G1",
            CrossSectionCode = "150X150"
        });
        await db.SaveChangesAsync();

        var provider = new ReplanningActualStateProvider(db, new SqlInventorySnapshotProvider(db));
        var state = await provider.GetAsync(
            planId,
            now.AddHours(1),
            new[] { new BaselinePlanOperation(ccmKey, caster, now.AddHours(3), now.AddHours(4), FiniteScheduleTaskType.Casting) });

        var future = Assert.Single(state.EffectiveCommittedFutureSupplies);
        Assert.Equal(poId, future.ProductionOrderId);
        Assert.Equal(heatId, future.CampaignHeatId);
        Assert.Equal(100m, future.QuantityMt);

        var po = Order(poId, "PO-REPLAN", 100m, now.AddHours(12));
        var campaign = new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            new[] { po },
            state.Inventory,
            Policy(),
            CommittedMaterialSupplies: state.EffectiveCommittedFutureSupplies));

        Assert.Equal(0m, campaign.FreshSteelRequirementsMt[po.Id]);
        Assert.Empty(campaign.Campaigns.SelectMany(x => x.Heats));
        Assert.Equal(100m, Assert.Single(campaign.InventoryAllocations, x => x.Use == PlanningInventoryUse.CommittedInternalProductionFeed).QuantityMt);
    }

    [Fact]
    public async Task Partially_produced_ccm_carries_only_remaining_future_quantity()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApsDbContext(options);

        var now = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var planId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var heatId = Guid.NewGuid();
        var caster = Guid.NewGuid();
        var ccmKey = "HEAT:H200:CCM";
        var receipt = new MaterialBalanceEvent
        {
            PlanVersionId = planId,
            EventType = MaterialBalanceEventType.PlannedProductionReceipt,
            MaterialPoolKey = $"PO:{poId:N}|GRADE:G1|SECTION:150X150",
            MaterialSpecificationCode = "BILLET-G1",
            GradeCode = "G1",
            CrossSectionCode = "150X150",
            QuantityDeltaMt = 100m,
            EffectiveAtUtc = now.AddHours(4),
            ProductionOrderId = poId,
            CampaignHeatId = heatId
        };
        db.PlanVersions.Add(new PlanVersion { Id = planId, VersionNumber = "PLAN-PARTIAL", CreatedOnUtc = now });
        db.PlanVersionStates.Add(new PlanVersionState
        {
            PlanVersionId = planId,
            Status = PlanVersionStatus.Released,
            Trigger = PlanTriggerType.Manual,
            ReferenceTimeUtc = now,
            HorizonStartUtc = now,
            HorizonEndUtc = now.AddDays(2),
            IsActive = true,
            MaterialLedgerJson = JsonSerializer.Serialize(new[] { receipt }, JsonOptions)
        });
        db.PlanOperationSnapshots.Add(new PlanOperationSnapshot
        {
            PlanVersionId = planId,
            PlanningKey = ccmKey,
            SourceEntityId = heatId,
            OperationType = PlanOperationType.Casting,
            ProcessOperationType = ProcessOperationType.Ccm,
            ResourceId = caster,
            CommittedResourceId = caster,
            ActualResourceId = caster,
            AssignmentCommitmentState = OperationAssignmentCommitmentState.Running,
            ExecutionStatus = OperationExecutionStatus.Running,
            ActualStartUtc = now.AddHours(3),
            ActualQuantityMt = 40m,
            StartUtc = now.AddHours(3),
            EndUtc = now.AddHours(4),
            QuantityMt = 100m,
            GradeCode = "G1",
            CrossSectionCode = "150X150"
        });
        db.MaterialLots.Add(new MaterialLot
        {
            LotNumber = "H200-ACTUAL-40",
            MaterialCode = "BILLET-G1",
            GradeCode = "G1",
            CrossSectionCode = "150X150",
            Stage = InventoryStage.CastIntermediate,
            QuantityMt = 40m,
            Status = MaterialLotStatus.Available
        });
        await db.SaveChangesAsync();

        var provider = new ReplanningActualStateProvider(db, new SqlInventorySnapshotProvider(db));
        var state = await provider.GetAsync(
            planId,
            now.AddHours(3).AddMinutes(20),
            new[] { new BaselinePlanOperation(ccmKey, caster, now.AddHours(3), now.AddHours(4), FiniteScheduleTaskType.Casting) });

        Assert.Equal(40m, Assert.Single(state.Inventory).ProjectedAvailableQuantityMt);
        Assert.Equal(60m, Assert.Single(state.EffectiveCommittedFutureSupplies).QuantityMt);

        var po = Order(poId, "PO-PARTIAL", 100m, now.AddHours(12));
        var campaign = new CampaignPlanningService().FormCampaigns(new CampaignPlanningRequest(
            new[] { po },
            state.Inventory,
            Policy(),
            CommittedMaterialSupplies: state.EffectiveCommittedFutureSupplies));

        Assert.Equal(0m, campaign.FreshSteelRequirementsMt[po.Id]);
        Assert.Empty(campaign.Campaigns.SelectMany(x => x.Heats));
        Assert.Equal(40m, campaign.IntermediateInventoryAllocatedMt[po.Id]);
        Assert.Equal(60m, Assert.Single(campaign.InventoryAllocations, x => x.Use == PlanningInventoryUse.CommittedInternalProductionFeed).QuantityMt);
    }

    private static CampaignPlanningPolicy Policy() => new(
        NominalHeatSizeMt: 60m,
        MinimumHeatSizeMt: 50m,
        MaximumHeatSizeMt: 70m,
        TargetCampaignQuantityMt: 500m,
        MaximumCampaignQuantityMt: 1000m);

    private static ProductionOrder Order(Guid id, string number, decimal quantity, DateTime due) => new()
    {
        Id = id,
        ProductionOrderNumber = number,
        DemandSource = DemandSourceType.MakeToOrder,
        MaterialCode = "FG-G1",
        GradeCode = "G1",
        FinalCrossSectionCode = "16MM",
        CasterSectionCode = "150X150",
        RouteCode = "STEEL",
        PlannedQuantityMt = quantity,
        RemainingQuantityMt = quantity,
        RequiredDate = due,
        Priority = 10,
        Status = ProductionOrderStatus.Planned
    };
}
