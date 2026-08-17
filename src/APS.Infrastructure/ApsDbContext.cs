using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class ApsDbContext(DbContextOptions<ApsDbContext> options) : DbContext(options)
{
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignAllocation> CampaignAllocations => Set<CampaignAllocation>();
    public DbSet<CampaignGradeSequence> CampaignGradeSequences => Set<CampaignGradeSequence>();
    public DbSet<CampaignHeat> CampaignHeats => Set<CampaignHeat>();
    public DbSet<CastSequence> CastSequences => Set<CastSequence>();
    public DbSet<CastSequenceHeat> CastSequenceHeats => Set<CastSequenceHeat>();
    public DbSet<RollingPlan> RollingPlans => Set<RollingPlan>();
    public DbSet<Plant> Plants => Set<Plant>();
    public DbSet<ProcessStage> ProcessStages => Set<ProcessStage>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ResourceCapability> ResourceCapabilities => Set<ResourceCapability>();
    public DbSet<ResourceCalendar> ResourceCalendars => Set<ResourceCalendar>();
    public DbSet<PlantFlowLink> PlantFlowLinks => Set<PlantFlowLink>();
    public DbSet<TransitionRule> TransitionRules => Set<TransitionRule>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderAllocation> WorkOrderAllocations => Set<WorkOrderAllocation>();
    public DbSet<MaterialLot> MaterialLots => Set<MaterialLot>();
    public DbSet<LotGenealogy> LotGenealogy => Set<LotGenealogy>();
    public DbSet<MaterialLotAllocation> MaterialLotAllocations => Set<MaterialLotAllocation>();
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

        modelBuilder.Entity<CampaignGradeSequence>()
            .HasOne(x => x.Campaign)
            .WithMany(x => x.GradeSequence)
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CampaignHeat>()
            .HasOne(x => x.Campaign)
            .WithMany(x => x.Heats)
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CampaignHeat>()
            .HasOne(x => x.CampaignGradeSequence)
            .WithMany()
            .HasForeignKey(x => x.CampaignGradeSequenceId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CastSequenceHeat>()
            .HasOne(x => x.CastSequence)
            .WithMany(x => x.Heats)
            .HasForeignKey(x => x.CastSequenceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CastSequenceHeat>()
            .HasOne(x => x.CampaignHeat)
            .WithMany()
            .HasForeignKey(x => x.CampaignHeatId)
            .OnDelete(DeleteBehavior.Restrict);

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

        modelBuilder.Entity<MaterialLotAllocation>()
            .HasOne<MaterialLot>()
            .WithMany()
            .HasForeignKey(x => x.MaterialLotId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MaterialLotAllocation>()
            .HasOne<ProductionOrder>()
            .WithMany()
            .HasForeignKey(x => x.ProductionOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LotGenealogy>().HasIndex(x => new { x.ParentLotId, x.ChildLotId });
        modelBuilder.Entity<MaterialLotAllocation>().HasIndex(x => new { x.MaterialLotId, x.ProductionOrderId });
        modelBuilder.Entity<CampaignGradeSequence>().HasIndex(x => new { x.CampaignId, x.SequenceNumber }).IsUnique();
        modelBuilder.Entity<CampaignHeat>().HasIndex(x => new { x.CampaignId, x.SequenceNumber }).IsUnique();
        modelBuilder.Entity<CastSequenceHeat>().HasIndex(x => new { x.CastSequenceId, x.Position }).IsUnique();
        modelBuilder.Entity<ResourceCalendar>().HasIndex(x => new { x.ResourceId, x.Start, x.End });

        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetPrecision(18);
            property.SetScale(4);
        }
    }
}
