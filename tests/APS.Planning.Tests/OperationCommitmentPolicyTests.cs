using System.Text.Json;
using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class OperationCommitmentPolicyTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Predecessor_running_commits_successor_only_when_its_policy_requires_it()
    {
        await using var db = CreateDb();
        var planId = Guid.NewGuid();
        var eafResource = Guid.NewGuid();
        var lrf1 = Guid.NewGuid();
        var lrf2 = Guid.NewGuid();

        db.PlanOperationSnapshots.AddRange(
            Operation(planId, "HEAT:H1:EAF", ProcessOperationType.Eaf, eafResource, DateTime.UtcNow.AddMinutes(-5)),
            Operation(
                planId,
                "HEAT:H1:LRF",
                ProcessOperationType.Lrf,
                lrf1,
                DateTime.UtcNow.AddMinutes(40),
                new[] { "HEAT:H1:EAF" },
                new OperationAssignmentPolicy(
                    ProcessOperationType.Lrf,
                    FirmMinutesBeforeStart: 15,
                    CommitMinutesBeforeStart: 5,
                    CommitWhenPredecessorRunning: true),
                new[] { lrf1, lrf2 }));
        await db.SaveChangesAsync();

        var service = new OperationExecutionService(db);
        var before = await service.RefreshCommitmentsAsync(planId, DateTime.UtcNow);
        Assert.Equal(
            OperationAssignmentCommitmentState.Flexible,
            Assert.Single(before, x => x.PlanningKey == "HEAT:H1:LRF").AssignmentCommitmentState);

        await service.ApplyAsync(new OperationExecutionUpdate(
            planId,
            "HEAT:H1:EAF",
            OperationExecutionStatus.Running,
            DateTime.UtcNow,
            ExecutionUpdateSource.Manual,
            eafResource));

        var lrf = await db.PlanOperationSnapshots.SingleAsync(x => x.PlanningKey == "HEAT:H1:LRF");
        Assert.Equal(OperationAssignmentCommitmentState.Committed, lrf.AssignmentCommitmentState);
        Assert.Equal(lrf1, lrf.CommittedResourceId);
        Assert.Null(lrf.ActualResourceId);
    }

    [Fact]
    public async Task Dispatch_acknowledgement_can_commit_rare_eligible_alternate_LRF()
    {
        await using var db = CreateDb();
        var planId = Guid.NewGuid();
        var eafResource = Guid.NewGuid();
        var normalLrf = Guid.NewGuid();
        var rareLrf = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var eaf = Operation(planId, "HEAT:H2:EAF", ProcessOperationType.Eaf, eafResource, now.AddMinutes(-30));
        eaf.ExecutionStatus = OperationExecutionStatus.Completed;
        eaf.AssignmentCommitmentState = OperationAssignmentCommitmentState.Completed;
        eaf.ActualResourceId = eafResource;
        eaf.CommittedResourceId = eafResource;

        db.PlanOperationSnapshots.AddRange(
            eaf,
            Operation(
                planId,
                "HEAT:H2:LRF",
                ProcessOperationType.Lrf,
                normalLrf,
                now.AddMinutes(20),
                new[] { "HEAT:H2:EAF" },
                new OperationAssignmentPolicy(
                    ProcessOperationType.Lrf,
                    FirmMinutesBeforeStart: 60,
                    CommitMinutesBeforeStart: 10,
                    CommitWhenPredecessorCompleted: true,
                    RequireDispatchAcknowledgement: true),
                new[] { normalLrf, rareLrf }));
        await db.SaveChangesAsync();

        var service = new OperationExecutionService(db);
        var refreshed = await service.RefreshCommitmentsAsync(planId, now);
        var firm = Assert.Single(refreshed, x => x.PlanningKey == "HEAT:H2:LRF");
        Assert.Equal(OperationAssignmentCommitmentState.Firm, firm.AssignmentCommitmentState);
        Assert.Null(firm.CommittedResourceId);

        var committed = await service.ApplyAsync(new OperationExecutionUpdate(
            planId,
            "HEAT:H2:LRF",
            OperationExecutionStatus.Ready,
            now.AddMinutes(1),
            ExecutionUpdateSource.Manual,
            ActualResourceId: rareLrf));

        Assert.Equal(OperationAssignmentCommitmentState.Committed, committed.AssignmentCommitmentState);
        Assert.Equal(rareLrf, committed.CommittedResourceId);
        Assert.Equal(normalLrf, committed.PlannedResourceId);
        Assert.Null(committed.ActualResourceId);
    }

    private static PlanOperationSnapshot Operation(
        Guid planId,
        string key,
        ProcessOperationType type,
        Guid resourceId,
        DateTime start,
        IReadOnlyCollection<string>? predecessors = null,
        OperationAssignmentPolicy? policy = null,
        IReadOnlyCollection<Guid>? eligibleResources = null)
    {
        var alternatives = (eligibleResources ?? new[] { resourceId })
            .Select((id, index) => new PlanningOperationResourceAlternative(
                Guid.NewGuid(),
                Guid.NewGuid(),
                key,
                type,
                id,
                30,
                index,
                id == resourceId,
                "TEST_CAPABILITY"))
            .ToArray();

        return new PlanOperationSnapshot
        {
            PlanVersionId = planId,
            PlanningKey = key,
            SourceEntityId = Guid.NewGuid(),
            OperationType = type switch
            {
                ProcessOperationType.Eaf => PlanOperationType.Eaf,
                ProcessOperationType.Lrf => PlanOperationType.Lrf,
                _ => PlanOperationType.Finishing
            },
            ProcessOperationType = type,
            ResourceId = resourceId,
            AssignmentCommitmentState = OperationAssignmentCommitmentState.Flexible,
            EligibleResourceOptionsJson = JsonSerializer.Serialize(alternatives, JsonOptions),
            PredecessorPlanningKeysJson = JsonSerializer.Serialize(predecessors ?? Array.Empty<string>(), JsonOptions),
            AssignmentPolicyJson = policy is null ? null : JsonSerializer.Serialize(policy, JsonOptions),
            ExecutionStatus = OperationExecutionStatus.Planned,
            StartUtc = start,
            EndUtc = start.AddMinutes(30),
            QuantityMt = 50m,
            GradeCode = "G1",
            CrossSectionCode = "150X150"
        };
    }

    private static ApsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-operation-commitment-{Guid.NewGuid():N}")
            .Options;
        return new ApsDbContext(options);
    }
}
