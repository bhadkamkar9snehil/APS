using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class ReplanningActualStateTests
{
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
}
