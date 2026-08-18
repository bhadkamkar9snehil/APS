using System.Text.Json;
using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Infrastructure.Tests;

public sealed class OperationExecutionFlexibilityTests
{
    [Fact]
    public async Task Ready_can_commit_a_retained_alternate_lrf()
    {
        await using var db = CreateDb();
        var lrf1 = Guid.NewGuid();
        var lrf2 = Guid.NewGuid();
        var operation = Operation("HEAT:H1:LRF", ProcessOperationType.Lrf, lrf1, lrf1, lrf2);
        db.PlanOperationSnapshots.Add(operation);
        await db.SaveChangesAsync();

        var service = new OperationExecutionService(db);
        var result = await service.ApplyAsync(new OperationExecutionUpdate(
            operation.PlanVersionId,
            operation.PlanningKey,
            OperationExecutionStatus.Ready,
            DateTime.UtcNow,
            ExecutionUpdateSource.Manual,
            ActualResourceId: lrf2));

        Assert.Equal(lrf2, result.CommittedResourceId);
        Assert.Equal(OperationAssignmentCommitmentState.Committed, result.AssignmentCommitmentState);
        Assert.False(result.IsOffPlanActualResource);
    }

    [Fact]
    public async Task Ready_rejects_resource_that_was_not_an_eligible_plan_alternative()
    {
        await using var db = CreateDb();
        var lrf1 = Guid.NewGuid();
        var lrf2 = Guid.NewGuid();
        var illegal = Guid.NewGuid();
        var operation = Operation("HEAT:H2:LRF", ProcessOperationType.Lrf, lrf1, lrf1, lrf2);
        db.PlanOperationSnapshots.Add(operation);
        await db.SaveChangesAsync();

        var service = new OperationExecutionService(db);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(new OperationExecutionUpdate(
            operation.PlanVersionId,
            operation.PlanningKey,
            OperationExecutionStatus.Ready,
            DateTime.UtcNow,
            ExecutionUpdateSource.Manual,
            ActualResourceId: illegal)));

        Assert.Contains("not an eligible alternative", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Running_actual_on_unplanned_resource_is_recorded_as_truth_and_flagged()
    {
        await using var db = CreateDb();
        var ccm1 = Guid.NewGuid();
        var ccm2 = Guid.NewGuid();
        var unexpected = Guid.NewGuid();
        var operation = Operation("HEAT:H3:CCM", ProcessOperationType.Ccm, ccm1, ccm1, ccm2);
        db.PlanOperationSnapshots.Add(operation);
        await db.SaveChangesAsync();

        var service = new OperationExecutionService(db);
        var result = await service.ApplyAsync(new OperationExecutionUpdate(
            operation.PlanVersionId,
            operation.PlanningKey,
            OperationExecutionStatus.Running,
            DateTime.UtcNow,
            ExecutionUpdateSource.MesApi,
            ActualResourceId: unexpected,
            ExternalEventId: "evt-off-plan"));

        Assert.Equal(unexpected, result.ActualResourceId);
        Assert.True(result.IsOffPlanActualResource);
        Assert.Equal("ACTUAL_RESOURCE_NOT_IN_PLANNED_ELIGIBLE_SET", result.OffPlanActualReasonCode);
        Assert.Equal(OperationAssignmentCommitmentState.Running, result.AssignmentCommitmentState);
    }

    private static PlanOperationSnapshot Operation(
        string key,
        ProcessOperationType process,
        Guid selected,
        params Guid[] alternatives)
    {
        var planId = Guid.NewGuid();
        var optionRows = alternatives.Select(resourceId => new PlanningOperationResourceAlternative(
            Guid.NewGuid(),
            Guid.NewGuid(),
            key,
            process,
            resourceId,
            45,
            resourceId == selected ? 0 : 100,
            resourceId == selected,
            "GRADE_ROUTE_CAPABILITY")).ToArray();

        return new PlanOperationSnapshot
        {
            PlanVersionId = planId,
            PlanningKey = key,
            SourceEntityId = Guid.NewGuid(),
            OperationType = process switch
            {
                ProcessOperationType.Lrf => PlanOperationType.Lrf,
                ProcessOperationType.Ccm => PlanOperationType.Casting,
                _ => PlanOperationType.Finishing
            },
            ProcessOperationType = process,
            ResourceId = selected,
            EligibleResourceOptionsJson = JsonSerializer.Serialize(optionRows, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            StartUtc = DateTime.UtcNow.AddHours(1),
            EndUtc = DateTime.UtcNow.AddHours(2),
            QuantityMt = 60m,
            GradeCode = "G42",
            CrossSectionCode = "150X150"
        };
    }

    private static ApsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-operation-flex-{Guid.NewGuid():N}")
            .Options;
        return new ApsDbContext(options);
    }
}
