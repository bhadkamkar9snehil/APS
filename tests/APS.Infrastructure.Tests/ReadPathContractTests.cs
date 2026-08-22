using APS.Domain;
using APS.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Infrastructure.Tests;

public sealed class ReadPathContractTests
{
    [Fact]
    public async Task Master_data_provider_materializes_all_grade_child_collections()
    {
        await using var database = await RelationalDatabase.CreateAsync();
        var grade = new SteelGrade
        {
            GradeCode = "G-SPLIT",
            Description = "Split query contract"
        };
        grade.Chemistry.Add(new GradeChemistryRequirement
        {
            ElementCode = "C",
            MinimumPct = 0.10m,
            MaximumPct = 0.20m
        });
        grade.Chemistry.Add(new GradeChemistryRequirement
        {
            ElementCode = "MN",
            MinimumPct = 0.50m,
            MaximumPct = 0.80m
        });
        grade.ProcessRequirements.Add(new GradeProcessRequirement
        {
            ProcessOperationType = ProcessOperationType.Lrf,
            Requirement = RequirementDisposition.Required
        });
        grade.ProcessRequirements.Add(new GradeProcessRequirement
        {
            ProcessOperationType = ProcessOperationType.Vd,
            Requirement = RequirementDisposition.Optional
        });
        database.Context.SteelGrades.Add(grade);
        await database.Context.SaveChangesAsync();

        var snapshot = await new SqlPlanningMasterDataProvider(database.Context).GetAsync();

        var loaded = Assert.Single(snapshot.EffectiveSteelGrades);
        Assert.Equal(2, loaded.Chemistry.Count);
        Assert.Equal(2, loaded.ProcessRequirements.Count);
        Assert.Contains(loaded.Chemistry, x => x.ElementCode == "C");
        Assert.Contains(loaded.ProcessRequirements, x => x.ProcessOperationType == ProcessOperationType.Vd);
    }

    [Fact]
    public async Task Inventory_snapshot_projection_preserves_reserved_quantity_and_thermal_evidence()
    {
        await using var database = await RelationalDatabase.CreateAsync();
        var productionOrder = new ProductionOrder
        {
            ProductionOrderNumber = "PO-READ-001",
            DemandSource = DemandSourceType.MakeToOrder,
            MaterialCode = "BILLET-G1",
            GradeCode = "G1",
            FinalCrossSectionCode = "100X100",
            CasterSectionCode = "100X100",
            RouteCode = "ROUTE-1",
            PlannedQuantityMt = 10m,
            RemainingQuantityMt = 10m,
            RequiredDate = new DateTime(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc)
        };
        var observedAt = new DateTime(2026, 8, 23, 6, 0, 0, DateTimeKind.Utc);
        var lot = new MaterialLot
        {
            LotNumber = "LOT-READ-001",
            MaterialCode = "BILLET-G1",
            GradeCode = "G1",
            CrossSectionCode = "100X100",
            Stage = InventoryStage.CastIntermediate,
            QuantityMt = 10m,
            Status = MaterialLotStatus.Available,
            LocationCode = "YARD-A",
            ThermalState = ChargeMode.HotBuffered,
            EstimatedTemperatureC = 720m,
            TemperatureObservedOnUtc = observedAt
        };
        database.Context.ProductionOrders.Add(productionOrder);
        database.Context.MaterialLots.Add(lot);
        database.Context.MaterialLotAllocations.Add(new MaterialLotAllocation
        {
            MaterialLotId = lot.Id,
            ProductionOrderId = productionOrder.Id,
            AllocatedQuantityMt = 4m,
            Status = LotAllocationStatus.Reserved
        });
        await database.Context.SaveChangesAsync();

        var positions = await new SqlInventorySnapshotProvider(database.Context).GetInventoryAsync();

        var position = Assert.Single(positions);
        Assert.Equal(10m, position.AvailableQuantityMt);
        Assert.Equal(4m, position.ReservedQuantityMt);
        Assert.Equal(6m, position.ProjectedAvailableQuantityMt);
        Assert.Equal(ChargeMode.HotBuffered, position.ThermalState);
        Assert.Equal(720m, position.EstimatedTemperatureC);
        Assert.Equal(BilletThermalSourceBasis.ActualMeasurement, position.ThermalBasis);
        Assert.Equal(observedAt, position.TemperatureObservedOnUtc);
    }

    private sealed class RelationalDatabase : IAsyncDisposable
    {
        private RelationalDatabase(SqliteConnection connection, ApsDbContext context)
        {
            Connection = connection;
            Context = context;
        }

        public SqliteConnection Connection { get; }
        public ApsDbContext Context { get; }

        public static async Task<RelationalDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApsDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new ApsDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new RelationalDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
