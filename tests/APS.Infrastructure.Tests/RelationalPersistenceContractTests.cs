using APS.Domain;
using APS.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Infrastructure.Tests;

public sealed class RelationalPersistenceContractTests
{
    private static readonly DateTime ReferenceTime = new(2026, 8, 22, 6, 0, 0, DateTimeKind.Utc);

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

    [Fact]
    public async Task Plan_version_round_trip_preserves_explainability_route_and_resource_flexibility_truth()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        var planVersionId = Guid.Parse("10000000-0000-0000-0000-000000000042");
        var sourceEntityId = Guid.Parse("20000000-0000-0000-0000-000000000042");
        var selectedResourceId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var alternateResourceId = Guid.Parse("30000000-0000-0000-0000-000000000002");
        const string planningKey = "HEAT-2042:LRF";
        const string assumptions = "{\"scenario\":\"BASELINE\",\"resourceMode\":\"Disjunctive\"}";
        const string routeDecisions = "[{\"operation\":\"VD\",\"decision\":\"Skipped\",\"reason\":\"GradeNotRequired\"}]";
        const string eligibleResources = "[{\"resourceCode\":\"LRF-01\"},{\"resourceCode\":\"LRF-02\"}]";

        database.Context.PlanVersionStates.Add(new PlanVersionState
        {
            PlanVersionId = planVersionId,
            Status = PlanVersionStatus.Feasible,
            Trigger = PlanTriggerType.Manual,
            ReferenceTimeUtc = ReferenceTime,
            HorizonStartUtc = ReferenceTime,
            HorizonEndUtc = ReferenceTime.AddDays(30),
            SolverStatus = "Feasible",
            ObjectiveValue = 42_000,
            IsActive = true,
            MaterialRequirementsJson = "[{\"material\":\"BILLET-G42\",\"quantityMt\":90}]",
            MaterialSupplyRequirementsJson = "[{\"material\":\"SCRAP-MIX\",\"shortfallMt\":12}]",
            PlanningAssumptionsJson = assumptions,
            RouteOperationDecisionsJson = routeDecisions
        });
        database.Context.PlanOperationSnapshots.Add(new PlanOperationSnapshot
        {
            PlanVersionId = planVersionId,
            PlanningKey = planningKey,
            SourceEntityId = sourceEntityId,
            OperationType = PlanOperationType.Lrf,
            ProcessOperationType = ProcessOperationType.Lrf,
            RouteCode = "SMS-G42",
            RouteSequenceNumber = 20,
            ResourceId = selectedResourceId,
            AssignmentCommitmentState = OperationAssignmentCommitmentState.Flexible,
            EligibleResourceOptionsJson = eligibleResources,
            PredecessorPlanningKeysJson = "[\"HEAT-2042:EAF\"]",
            AssignmentPolicyJson = "{\"basis\":\"QualifiedAlternate\"}",
            ExecutionStatus = OperationExecutionStatus.Planned,
            StartUtc = ReferenceTime.AddHours(2),
            EndUtc = ReferenceTime.AddHours(2).AddMinutes(55),
            QuantityMt = 90m,
            GradeCode = "G42",
            CrossSectionCode = "BLT-150"
        });
        database.Context.PlanOperationResourceOptionSnapshots.AddRange(
            new PlanOperationResourceOptionSnapshot
            {
                PlanVersionId = planVersionId,
                PlanningKey = planningKey,
                SourceEntityId = sourceEntityId,
                ProcessOperationType = ProcessOperationType.Lrf,
                ResourceId = selectedResourceId,
                DurationMinutes = 55,
                AssignmentPenalty = 0,
                WasSelected = true,
                EligibilityBasisCode = "ROUTE",
                CapturedOnUtc = ReferenceTime
            },
            new PlanOperationResourceOptionSnapshot
            {
                PlanVersionId = planVersionId,
                PlanningKey = planningKey,
                SourceEntityId = sourceEntityId,
                ProcessOperationType = ProcessOperationType.Lrf,
                ResourceId = alternateResourceId,
                DurationMinutes = 55,
                AssignmentPenalty = 5,
                WasSelected = false,
                EligibilityBasisCode = "ROUTE",
                CapturedOnUtc = ReferenceTime
            });

        await database.Context.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();

        var state = await database.Context.PlanVersionStates.AsNoTracking()
            .SingleAsync(x => x.PlanVersionId == planVersionId);
        var operation = await database.Context.PlanOperationSnapshots.AsNoTracking()
            .SingleAsync(x => x.PlanVersionId == planVersionId && x.PlanningKey == planningKey);
        var options = await database.Context.PlanOperationResourceOptionSnapshots.AsNoTracking()
            .Where(x => x.PlanVersionId == planVersionId && x.PlanningKey == planningKey)
            .OrderBy(x => x.AssignmentPenalty)
            .ToArrayAsync();

        Assert.Equal(assumptions, state.PlanningAssumptionsJson);
        Assert.Equal(routeDecisions, state.RouteOperationDecisionsJson);
        Assert.Equal("SMS-G42", operation.RouteCode);
        Assert.Equal(20, operation.RouteSequenceNumber);
        Assert.Equal(OperationAssignmentCommitmentState.Flexible, operation.AssignmentCommitmentState);
        Assert.Equal(eligibleResources, operation.EligibleResourceOptionsJson);
        Assert.Equal([selectedResourceId, alternateResourceId], options.Select(x => x.ResourceId));
        Assert.True(options[0].WasSelected);
        Assert.False(options[1].WasSelected);
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
