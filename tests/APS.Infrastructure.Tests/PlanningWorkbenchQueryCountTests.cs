using System.Data.Common;
using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace APS.Infrastructure.Tests;

public sealed class PlanningWorkbenchQueryCountTests
{
    [Fact]
    public async Task Bulk_move_validation_query_count_does_not_grow_with_selection_size()
    {
        var twoMoves = await CountValidationQueriesAsync(2);
        var twelveMoves = await CountValidationQueriesAsync(12);

        Assert.Equal(twoMoves, twelveMoves);
        Assert.InRange(twoMoves, 1, 5);
    }

    private static async Task<int> CountValidationQueriesAsync(int moveCount)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var counter = new QueryCounter();
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(counter)
            .Options;
        await using var db = new ApsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var planId = Guid.NewGuid();
        var source = Resource("EAF-SOURCE");
        var target = Resource("EAF-TARGET");
        var reference = new DateTime(2026, 8, 22, 0, 0, 0, DateTimeKind.Utc);

        db.PlanVersions.Add(new PlanVersion
        {
            Id = planId,
            VersionNumber = $"PLAN-QUERY-{moveCount}",
            CreatedOnUtc = reference
        });
        db.PlanVersionStates.Add(new PlanVersionState
        {
            PlanVersionId = planId,
            Status = PlanVersionStatus.Feasible,
            ReferenceTimeUtc = reference,
            HorizonStartUtc = reference,
            HorizonEndUtc = reference.AddDays(10),
            IsActive = true
        });
        db.Resources.AddRange(source, target);

        var moves = new List<PlanningBulkMoveItem>(moveCount);
        for (var index = 0; index < moveCount; index++)
        {
            var start = reference.AddHours(4 + index * 2);
            var operation = new PlanOperationSnapshot
            {
                PlanVersionId = planId,
                PlanningKey = $"HEAT:CMP-QUERY:H{index + 1:D2}:EAF",
                SourceEntityId = Guid.NewGuid(),
                OperationType = PlanOperationType.Eaf,
                ProcessOperationType = ProcessOperationType.Eaf,
                ResourceId = source.Id,
                StartUtc = start,
                EndUtc = start.AddHours(1),
                QuantityMt = 70m,
                GradeCode = "SAE1008",
                CrossSectionCode = "BLT-150SQ"
            };
            db.PlanOperationSnapshots.Add(operation);
            db.PlanOperationResourceOptionSnapshots.Add(new PlanOperationResourceOptionSnapshot
            {
                PlanVersionId = planId,
                PlanningKey = operation.PlanningKey,
                SourceEntityId = operation.SourceEntityId,
                ProcessOperationType = operation.ProcessOperationType,
                ResourceId = target.Id,
                DurationMinutes = 60,
                WasSelected = false,
                EligibilityBasisCode = "ROUTE_GRADE_CAPABILITY"
            });
            moves.Add(new PlanningBulkMoveItem(
                operation.PlanningKey,
                target.Id,
                reference.AddDays(2).AddHours(index * 2)));
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        counter.Reset();

        var service = new PlanningWorkbenchCommandService(db, new ThrowingLifecycle());
        var impact = await service.ValidateBulkMoveAsync(new PlanningBulkMoveProposal(
            planId,
            moves,
            "QUERY_COUNT_TEST",
            TimeFencePolicy: new PlanningTimeFencePolicy(FrozenMinutes: 0)));

        Assert.True(impact.CanApply);
        Assert.Equal(moveCount, impact.Items.Count);
        return counter.ReaderCommands;
    }

    private static Resource Resource(string code) => new()
    {
        PlantId = Guid.NewGuid(),
        ProcessStageId = Guid.NewGuid(),
        Code = code,
        Name = code,
        ResourceType = ResourceType.Furnace,
        ProcessUnitType = ProcessUnitType.Eaf,
        OperatingState = ResourceOperatingState.Available,
        SchedulingMode = ResourceSchedulingMode.Disjunctive
    };

    private sealed class QueryCounter : DbCommandInterceptor
    {
        private int readerCommands;
        public int ReaderCommands => Volatile.Read(ref readerCommands);

        public void Reset() => Interlocked.Exchange(ref readerCommands, 0);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref readerCommands);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref readerCommands);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowingLifecycle : IPlanningLifecycleService
    {
        public Task<PersistedPlanningRunResult> CalculateAsync(
            PlanningCalculationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PersistedPlanningRunResult> ReplanAsync(
            Guid baselinePlanVersionId,
            PlanningRecalculationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
