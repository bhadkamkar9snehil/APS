using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class ApsDbContext(DbContextOptions<ApsDbContext> options) : DbContext(options)
{
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignAllocation> CampaignAllocations => Set<CampaignAllocation>();
    public DbSet<CampaignHeat> CampaignHeats => Set<CampaignHeat>();
    public DbSet<Plant> Plants => Set<Plant>();
    public DbSet<ProcessStage> ProcessStages => Set<ProcessStage>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ResourceCapability> ResourceCapabilities => Set<ResourceCapability>();
    public DbSet<PlantFlowLink> PlantFlowLinks => Set<PlantFlowLink>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderAllocation> WorkOrderAllocations => Set<WorkOrderAllocation>();
    public DbSet<MaterialLot> MaterialLots => Set<MaterialLot>();
    public DbSet<LotGenealogy> LotGenealogy => Set<LotGenealogy>();
    public DbSet<PlanVersion> PlanVersions => Set<PlanVersion>();
    public DbSet<ScheduledOperation> ScheduledOperations => Set<ScheduledOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SalesOrder>().HasIndex(x => new { x.SalesOrderNumber, x.ItemNumber }).IsUnique();
        modelBuilder.Entity<ProductionOrder>().HasIndex(x => x.ProductionOrderNumber).IsUnique();
        modelBuilder.Entity<Campaign>().HasIndex(x => x.CampaignNumber).IsUnique();
        modelBuilder.Entity<WorkOrder>().HasIndex(x => x.WorkOrderNumber).IsUnique();
        modelBuilder.Entity<MaterialLot>().HasIndex(x => x.LotNumber).IsUnique();
        modelBuilder.Entity<Resource>().HasIndex(x => new { x.PlantId, x.Code }).IsUnique();

        modelBuilder.Entity<ProductionOrder>()
            .HasOne(x => x.SalesOrder)
            .WithMany()
            .HasForeignKey(x => x.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CampaignAllocation>()
            .HasOne(x => x.Campaign)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CampaignAllocation>()
            .HasOne(x => x.ProductionOrder)
            .WithMany()
            .HasForeignKey(x => x.ProductionOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CampaignHeat>()
            .HasOne(x => x.Campaign)
            .WithMany(x => x.Heats)
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorkOrderAllocation>()
            .HasOne(x => x.WorkOrder)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorkOrderAllocation>()
            .HasOne(x => x.ProductionOrder)
            .WithMany()
            .HasForeignKey(x => x.ProductionOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LotGenealogy>().HasIndex(x => new { x.ParentLotId, x.ChildLotId });
        modelBuilder.Entity<CampaignHeat>().HasIndex(x => new { x.CampaignId, x.SequenceNumber }).IsUnique();

        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetPrecision(18);
            property.SetScale(4);
        }
    }
}
