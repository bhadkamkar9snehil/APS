using System.Data.Common;
using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace APS.Infrastructure.Tests;

public sealed class DemandReconciliationQueryCountTests
{
    [Fact]
    public async Task Reconciliation_query_count_does_not_grow_with_sales_order_batch_size()
    {
        var twoItems = await CountReconciliationQueriesAsync(2);
        var twelveItems = await CountReconciliationQueriesAsync(12);

        Assert.Equal(twoItems, twelveItems);
        Assert.InRange(twoItems, 1, 5);
    }

    private static async Task<int> CountReconciliationQueriesAsync(int itemCount)
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

        var due = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        var inputs = new List<SalesOrderDemandInput>(itemCount);
        for (var index = 0; index < itemCount; index++)
        {
            var number = $"SO-QUERY-{index + 1:D3}";
            var order = new SalesOrder
            {
                SalesOrderNumber = number,
                ItemNumber = "10",
                MaterialCode = "FG-16",
                GradeCode = "G1",
                FinalCrossSectionCode = "16MM",
                OrderQuantityMt = 100m,
                OpenQuantityMt = 90m,
                RequiredDate = due,
                CustomerCode = "CUST",
                CustomerGroupCode = "GROUP",
                ExternalStatus = "OPEN"
            };
            var state = new SalesOrderDemandState
            {
                SalesOrderId = order.Id,
                SalesOrder = order,
                OpenDemandQuantityMt = 90m,
                CustomerRequiredDate = due,
                ProductionRequiredByDate = due,
                Priority = 1,
                Disposition = DemandReconciliationDisposition.Unchanged
            };
            var profile = new SalesOrderRequirementProfile
            {
                SalesOrderId = order.Id,
                SalesOrder = order,
                QualityClassCode = "Q-CERT",
                RequireVd = true
            };
            profile.ChemistryOverrides.Add(new SalesOrderChemistryRequirement
            {
                SalesOrderRequirementProfileId = profile.Id,
                SalesOrderRequirementProfile = profile,
                ElementCode = "C",
                MaximumPct = 0.12m
            });
            profile.ProcessOverrides.Add(new SalesOrderProcessRequirement
            {
                SalesOrderRequirementProfileId = profile.Id,
                SalesOrderRequirementProfile = profile,
                ProcessOperationType = ProcessOperationType.Vd,
                Requirement = RequirementDisposition.Required
            });

            db.SalesOrders.Add(order);
            db.SalesOrderDemandStates.Add(state);
            db.SalesOrderRequirementProfiles.Add(profile);
            inputs.Add(new SalesOrderDemandInput(
                number,
                "10",
                "FG-16",
                "G1",
                "16MM",
                100m,
                80m,
                due,
                CustomerCode: "CUST",
                CustomerGroupCode: "GROUP",
                ExternalStatus: "OPEN",
                Priority: 2,
                Requirement: new SalesOrderRequirementInput(
                    QualityClassCode: "Q-CERT",
                    RequireVd: true,
                    ChemistryOverrides:
                    [
                        new SalesOrderChemistryRequirementInput("C", MaximumPct: 0.12m)
                    ],
                    ProcessOverrides:
                    [
                        new SalesOrderProcessRequirementInput(
                            ProcessOperationType.Vd,
                            RequirementDisposition.Required)
                    ])));
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        counter.Reset();

        var service = new ProductionDemandOrchestrationService(
            db,
            NullLogger<ProductionDemandOrchestrationService>.Instance);
        var result = await service.ReconcileSalesOrdersAsync(inputs);

        Assert.Equal(0, result.Created);
        Assert.Equal(itemCount, result.Updated);
        Assert.Equal(itemCount, result.SalesOrderIds.Count);
        return counter.ReaderCommands;
    }

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
            if (IsSelect(command)) Interlocked.Increment(ref readerCommands);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsSelect(command)) Interlocked.Increment(ref readerCommands);
            return ValueTask.FromResult(result);
        }

        private static bool IsSelect(DbCommand command) =>
            command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);
    }
}
