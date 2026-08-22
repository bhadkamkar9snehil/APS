using APS.Domain;
using APS.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Infrastructure.Tests;

public sealed class RelationalPersistenceContractTests
{
    [Fact]
    public async Task Sqlite_can_create_the_complete_APS_model()
    {
        await using var database = await SqliteDatabase.CreateAsync();

        Assert.True(await database.Context.Database.CanConnectAsync());
    }

    [Fact]
    public async Task Business_key_uniqueness_is_enforced_by_the_relational_provider()
    {
        await using var database = await SqliteDatabase.CreateAsync();

        database.Context.SteelGrades.AddRange(
            new SteelGrade { GradeCode = "G42", Description = "First definition" },
            new SteelGrade { GradeCode = "G42", Description = "Duplicate definition" });

        await Assert.ThrowsAsync<DbUpdateException>(() => database.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_a_BOM_cascades_to_its_components()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var bom = new BillOfMaterial
        {
            BomCode = "BOM-BILLET-G42",
            OutputMaterialCode = "BILLET-G42",
            OutputQuantity = 1m,
            Components =
            {
                new BillOfMaterialComponent
                {
                    SequenceNumber = 10,
                    ComponentMaterialCode = "SCRAP-MIX",
                    QuantityPerOutput = 1.08m
                }
            }
        };

        database.Context.BillsOfMaterial.Add(bom);
        await database.Context.SaveChangesAsync();
        var componentId = bom.Components.Single().Id;

        database.Context.BillsOfMaterial.Remove(bom);
        await database.Context.SaveChangesAsync();

        Assert.Null(await database.Context.BillOfMaterialComponents.FindAsync(componentId));
    }

    private sealed class SqliteDatabase : IAsyncDisposable
    {
        private SqliteDatabase(SqliteConnection connection, ApsDbContext context)
        {
            Connection = connection;
            Context = context;
        }

        public SqliteConnection Connection { get; }
        public ApsDbContext Context { get; }

        public static async Task<SqliteDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApsDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new ApsDbContext(options);
            await context.Database.EnsureCreatedAsync();

            return new SqliteDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
