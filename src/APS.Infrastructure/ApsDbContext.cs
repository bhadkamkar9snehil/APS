using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class ApsDbContext(DbContextOptions<ApsDbContext> options) : DbContext(options)
{
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderDemandState> SalesOrderDemandStates => Set<SalesOrderDemandState>();
    public DbSet<SalesOrderFinishedGoodsCoverage> SalesOrderFinishedGoodsCoverage => Set<SalesOrderFinishedGoodsCoverage>();
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<ProductionOrderRequirement> ProductionOrderRequirements => Set<ProductionOrderRequirement>();
    public DbSet<OrderChemistryRequirement> OrderChemistryRequirements => Set<OrderChemistryRequirement>();
    public DbSet<OrderProcessRequirement> OrderProcessRequirements => Set<OrderProcessRequirement>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignAllocation> CampaignAllocations => Set<CampaignAllocation>();
    public DbSet<CampaignGradeSequence> CampaignGradeSequences => Set<CampaignGradeSequence>();
    public DbSet<CampaignHeat> CampaignHeats => Set<CampaignHeat>();
    public DbSet<CampaignHeatAllocation> CampaignHeatAllocations => Set<CampaignHeatAllocation>();
    public DbSet<CastSequence> CastSequences => Set<CastSequence>();
    public DbSet<CastSequenceHeat> CastSequenceHeats => Set<CastSequenceHeat>();
    public DbSet<RollingPlan> RollingPlans => Set<RollingPlan>();
    public DbSet<RollingPlanAllocation> RollingPlanAllocations => Set<RollingPlanAllocation>();

    public DbSet<Plant> Plants => Set<Plant>();
    public DbSet<PlantArea> PlantAreas => Set<PlantArea>();
    public DbSet<ProcessStage> ProcessStages => Set<ProcessStage>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ResourceCapability> ResourceCapabilities => Set<ResourceCapability>();
    public DbSet<ResourceCalendar> ResourceCalendars => Set<ResourceCalendar>();
    public DbSet<PlantFlowLink> PlantFlowLinks => Set<PlantFlowLink>();
    public DbSet<TransitionRule> TransitionRules => Set<TransitionRule>();

    public DbSet<SteelGrade> SteelGrades => Set<SteelGrade>();
    public DbSet<GradeChemistryRequirement> GradeChemistryRequirements => Set<GradeChemistryRequirement>();
    public DbSet<GradeProcessRequirement> GradeProcessRequirements => Set<GradeProcessRequirement>();
    public DbSet<CrossSectionSpecification> CrossSectionSpecifications => Set<CrossSectionSpecification>();
    public DbSet<MaterialSpecification> MaterialSpecifications => Set<MaterialSpecification>();
    public DbSet<PackagingSpecification> PackagingSpecifications => Set<PackagingSpecification>();
    public DbSet<ExternalMaterialSupply> ExternalMaterialSupplies => Set<ExternalMaterialSupply>();
    public DbSet<MaterialSourcingRule> MaterialSourcingRules => Set<MaterialSourcingRule>();
    public DbSet<PlannedPackagingUnit> PlannedPackagingUnits => Set<PlannedPackagingUnit>();

    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderAllocation> WorkOrderAllocations => Set<WorkOrderAllocation>();
    public DbSet<WorkOrderStatusHistory> WorkOrderStatusHistory => Set<WorkOrderStatusHistory>();
    public DbSet<HeatExecutionActual> HeatExecutionActuals => Set<HeatExecutionActual>();
    public DbSet<StrandMaterialActual> StrandMaterialActuals => Set<StrandMaterialActual>();
    public DbSet<MaterialLot> MaterialLots => Set<MaterialLot>();
    public DbSet<LotGenealogy> LotGenealogy => Set<LotGenealogy>();
    public DbSet<MaterialLotAllocation> MaterialLotAllocations => Set<MaterialLotAllocation>();
    public DbSet<PlanVersion> PlanVersions => Set<PlanVersion>();
    public DbSet<PlanVersionState> PlanVersionStates => Set<PlanVersionState>();
    public DbSet<PlanOperationSnapshot> PlanOperationSnapshots => Set<PlanOperationSnapshot>();
    public DbSet<PlanOperationResourceOptionSnapshot> PlanOperationResourceOptionSnapshots => Set<PlanOperationResourceOptionSnapshot>();
    public DbSet<OperationDispatchRevision> OperationDispatchRevisions => Set<OperationDispatchRevision>();
    public DbSet<PlanInventoryAllocationSnapshot> PlanInventoryAllocationSnapshots => Set<PlanInventoryAllocationSnapshot>();
    public DbSet<PlanMaterialUnitSnapshot> PlanMaterialUnitSnapshots => Set<PlanMaterialUnitSnapshot>();
    public DbSet<PlanDemandSnapshot> PlanDemandSnapshots => Set<PlanDemandSnapshot>();
    public DbSet<PlanDemandCoverageSnapshot> PlanDemandCoverageSnapshots => Set<PlanDemandCoverageSnapshot>();
    public DbSet<PlanProductionOrderSnapshot> PlanProductionOrderSnapshots => Set<PlanProductionOrderSnapshot>();
    public DbSet<PlanCampaignSnapshot> PlanCampaignSnapshots => Set<PlanCampaignSnapshot>();
    public DbSet<PlanCampaignAllocationSnapshot> PlanCampaignAllocationSnapshots => Set<PlanCampaignAllocationSnapshot>();
    public DbSet<PlanCampaignGradeSequenceSnapshot> PlanCampaignGradeSequenceSnapshots => Set<PlanCampaignGradeSequenceSnapshot>();
    public DbSet<PlanHeatSnapshot> PlanHeatSnapshots => Set<PlanHeatSnapshot>();
    public DbSet<PlanHeatAllocationSnapshot> PlanHeatAllocationSnapshots => Set<PlanHeatAllocationSnapshot>();
    public DbSet<PlanCastSequenceSnapshot> PlanCastSequenceSnapshots => Set<PlanCastSequenceSnapshot>();
    public DbSet<PlanCastSequenceHeatSnapshot> PlanCastSequenceHeatSnapshots => Set<PlanCastSequenceHeatSnapshot>();
    public DbSet<PlanRollingPlanSnapshot> PlanRollingPlanSnapshots => Set<PlanRollingPlanSnapshot>();
    public DbSet<PlanRollingPlanAllocationSnapshot> PlanRollingPlanAllocationSnapshots => Set<PlanRollingPlanAllocationSnapshot>();
    public DbSet<PlanRouteOperationSnapshot> PlanRouteOperationSnapshots => Set<PlanRouteOperationSnapshot>();
    public DbSet<PlanRouteOperationAllocationSnapshot> PlanRouteOperationAllocationSnapshots => Set<PlanRouteOperationAllocationSnapshot>();
    public DbSet<PlanPackagingUnitSnapshot> PlanPackagingUnitSnapshots => Set<PlanPackagingUnitSnapshot>();
    public DbSet<ScheduledOperation> ScheduledOperations => Set<ScheduledOperation>();
    public DbSet<ManufacturingRoute> ManufacturingRoutes => Set<ManufacturingRoute>();
    public DbSet<ManufacturingRouteOperation> ManufacturingRouteOperations => Set<ManufacturingRouteOperation>();
    public DbSet<RouteResourceCapability> RouteResourceCapabilities => Set<RouteResourceCapability>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SalesOrder>().HasIndex(x => new { x.SalesOrderNumber, x.ItemNumber }).IsUnique();
        modelBuilder.Entity<SalesOrderDemandState>().HasIndex(x => x.SalesOrderId).IsUnique();
        modelBuilder.Entity<ProductionOrder>().HasIndex(x => x.ProductionOrderNumber).IsUnique();
        modelBuilder.Entity<Campaign>().HasIndex(x => x.CampaignNumber).IsUnique();
        modelBuilder.Entity<WorkOrder>().HasIndex(x => x.WorkOrderNumber).IsUnique();
        modelBuilder.Entity<WorkOrder>().HasIndex(x => x.ExternalExecutionId);
        modelBuilder.Entity<MaterialLot>().HasIndex(x => x.LotNumber).IsUnique();
        modelBuilder.Entity<Resource>().HasIndex(x => new { x.PlantId, x.Code }).IsUnique();
        modelBuilder.Entity<PlantArea>().HasIndex(x => new { x.PlantId, x.Code }).IsUnique();
        modelBuilder.Entity<ProcessStage>().HasIndex(x => new { x.PlantId, x.Code }).IsUnique();
        modelBuilder.Entity<SteelGrade>().HasIndex(x => x.GradeCode).IsUnique();
        modelBuilder.Entity<CrossSectionSpecification>().HasIndex(x => x.CrossSectionCode).IsUnique();
        modelBuilder.Entity<MaterialSpecification>().HasIndex(x => x.MaterialSpecificationCode).IsUnique();
        modelBuilder.Entity<PackagingSpecification>().HasIndex(x => x.PackagingCode).IsUnique();
        modelBuilder.Entity<ExternalMaterialSupply>().HasIndex(x => new { x.SourceType, x.SupplyReference });
        modelBuilder.Entity<MaterialSourcingRule>().HasIndex(x => x.RuleCode).IsUnique();
        modelBuilder.Entity<MaterialSourcingRule>().HasIndex(x => new { x.MaterialCode, x.GradeCode, x.CrossSectionCode, x.DestinationLocationCode });

        modelBuilder.Entity<SalesOrderDemandState>()
            .HasOne(x => x.SalesOrder)
            .WithOne()
            .HasForeignKey<SalesOrderDemandState>(x => x.SalesOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SalesOrderDemandState>()
            .HasOne(x => x.ProductionOrder)
            .WithMany()
            .HasForeignKey(x => x.ProductionOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<SalesOrderDemandState>()
            .HasMany(x => x.FinishedGoodsCoverage)
            .WithOne(x => x.SalesOrderDemandState)
            .HasForeignKey(x => x.SalesOrderDemandStateId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProductionOrder>()
            .HasOne(x => x.SalesOrder)
            .WithMany()
            .HasForeignKey(x => x.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProductionOrder>()
            .HasOne(x => x.SteelGrade)
            .WithMany()
            .HasForeignKey(x => x.SteelGradeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProductionOrder>()
            .HasOne(x => x.Requirement)
            .WithOne(x => x.ProductionOrder)
            .HasForeignKey<ProductionOrderRequirement>(x => x.ProductionOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProductionOrderRequirement>()
            .HasMany(x => x.ChemistryOverrides)
            .WithOne()
            .HasForeignKey(x => x.ProductionOrderRequirementId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProductionOrderRequirement>()
            .HasMany(x => x.ProcessOverrides)
            .WithOne()
            .HasForeignKey(x => x.ProductionOrderRequirementId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SteelGrade>()
            .HasMany(x => x.Chemistry)
            .WithOne(x => x.SteelGrade)
            .HasForeignKey(x => x.SteelGradeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SteelGrade>()
            .HasMany(x => x.ProcessRequirements)
            .WithOne(x => x.SteelGrade)
            .HasForeignKey(x => x.SteelGradeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProcessStage>()
            .HasOne<PlantArea>()
            .WithMany()
            .HasForeignKey(x => x.PlantAreaId)
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

        modelBuilder.Entity<CampaignHeatAllocation>()
            .HasOne(x => x.CampaignHeat)
            .WithMany()
            .HasForeignKey(x => x.CampaignHeatId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CampaignHeatAllocation>()
            .HasOne(x => x.ProductionOrder)
            .WithMany()
            .HasForeignKey(x => x.ProductionOrderId)
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

        modelBuilder.Entity<RollingPlanAllocation>()
            .HasOne(x => x.RollingPlan)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => x.RollingPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RollingPlanAllocation>()
            .HasOne(x => x.ProductionOrder)
            .WithMany()
            .HasForeignKey(x => x.ProductionOrderId)
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

        modelBuilder.Entity<WorkOrderStatusHistory>()
            .HasOne(x => x.WorkOrder)
            .WithMany()
            .HasForeignKey(x => x.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StrandMaterialActual>()
            .HasOne(x => x.HeatExecutionActual)
            .WithMany()
            .HasForeignKey(x => x.HeatExecutionActualId)
            .OnDelete(DeleteBehavior.Cascade);

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

        modelBuilder.Entity<PlanVersionState>()
            .HasOne<PlanVersion>()
            .WithMany()
            .HasForeignKey(x => x.PlanVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlanOperationSnapshot>()
            .HasOne<PlanVersion>()
            .WithMany()
            .HasForeignKey(x => x.PlanVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlanOperationResourceOptionSnapshot>()
            .HasOne<PlanVersion>()
            .WithMany()
            .HasForeignKey(x => x.PlanVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OperationDispatchRevision>()
            .HasOne<PlanVersion>()
            .WithMany()
            .HasForeignKey(x => x.PlanVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlanInventoryAllocationSnapshot>()
            .HasOne<PlanVersion>()
            .WithMany()
            .HasForeignKey(x => x.PlanVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlanMaterialUnitSnapshot>()
            .HasOne<PlanVersion>()
            .WithMany()
            .HasForeignKey(x => x.PlanVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlanDemandSnapshot>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlanDemandCoverageSnapshot>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlanProductionOrderSnapshot>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlanCampaignSnapshot>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlanCampaignAllocationSnapshot>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlanCampaignGradeSequenceSnapshot>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlanHeatSnapshot>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlanHeatAllocationSnapshot>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlanCastSequenceSnapshot>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlanCastSequenceHeatSnapshot>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlanRollingPlanSnapshot>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlanRollingPlanAllocationSnapshot>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlanRouteOperationSnapshot>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlanRouteOperationAllocationSnapshot>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PlanPackagingUnitSnapshot>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ManufacturingRouteOperation>()
            .HasOne(x => x.ManufacturingRoute)
            .WithMany(x => x.Operations)
            .HasForeignKey(x => x.ManufacturingRouteId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProductionOrderRequirement>().HasIndex(x => x.ProductionOrderId).IsUnique();
        modelBuilder.Entity<SalesOrderFinishedGoodsCoverage>().HasIndex(x => new { x.SalesOrderDemandStateId, x.MaterialCode, x.GradeCode, x.CrossSectionCode, x.LocationCode });
        modelBuilder.Entity<GradeChemistryRequirement>().HasIndex(x => new { x.SteelGradeId, x.ElementCode }).IsUnique();
        modelBuilder.Entity<GradeProcessRequirement>().HasIndex(x => new { x.SteelGradeId, x.ProcessOperationType }).IsUnique();
        modelBuilder.Entity<OrderChemistryRequirement>().HasIndex(x => new { x.ProductionOrderRequirementId, x.ElementCode }).IsUnique();
        modelBuilder.Entity<OrderProcessRequirement>().HasIndex(x => new { x.ProductionOrderRequirementId, x.ProcessOperationType, x.RequiredResourceId });
        modelBuilder.Entity<CampaignHeatAllocation>().HasIndex(x => new { x.CampaignHeatId, x.ProductionOrderId });
        modelBuilder.Entity<LotGenealogy>().HasIndex(x => new { x.ParentLotId, x.ChildLotId });
        modelBuilder.Entity<MaterialLotAllocation>().HasIndex(x => new { x.MaterialLotId, x.ProductionOrderId });
        modelBuilder.Entity<CampaignGradeSequence>().HasIndex(x => new { x.CampaignId, x.SequenceNumber }).IsUnique();
        modelBuilder.Entity<CampaignHeat>().HasIndex(x => new { x.CampaignId, x.SequenceNumber }).IsUnique();
        modelBuilder.Entity<CastSequenceHeat>().HasIndex(x => new { x.CastSequenceId, x.Position }).IsUnique();
        modelBuilder.Entity<RollingPlanAllocation>().HasIndex(x => new { x.RollingPlanId, x.ProductionOrderId, x.CampaignId });
        modelBuilder.Entity<ResourceCapability>().HasIndex(x => new { x.ResourceId, x.ProcessOperationType });
        modelBuilder.Entity<ResourceCalendar>().HasIndex(x => new { x.ResourceId, x.Start, x.End });
        modelBuilder.Entity<WorkOrderStatusHistory>().HasIndex(x => new { x.WorkOrderId, x.ChangedOnUtc });
        modelBuilder.Entity<WorkOrderStatusHistory>().HasIndex(x => new { x.Source, x.ExternalEventId });
        modelBuilder.Entity<HeatExecutionActual>().HasIndex(x => new { x.PlanVersionId, x.PlanningKey, x.ChangedOnUtc });
        modelBuilder.Entity<HeatExecutionActual>().HasIndex(x => new { x.Source, x.ExternalEventId }).IsUnique();
        modelBuilder.Entity<StrandMaterialActual>().HasIndex(x => new { x.HeatExecutionActualId, x.StrandNumber, x.UnitSequence });
        modelBuilder.Entity<PlanVersionState>().HasIndex(x => x.PlanVersionId).IsUnique();
        modelBuilder.Entity<PlanOperationSnapshot>().HasIndex(x => new { x.PlanVersionId, x.PlanningKey }).IsUnique();
        modelBuilder.Entity<PlanOperationResourceOptionSnapshot>().HasIndex(x => new { x.PlanVersionId, x.PlanningKey, x.ResourceId }).IsUnique();
        modelBuilder.Entity<OperationDispatchRevision>().HasIndex(x => new { x.PlanVersionId, x.PlanningKey, x.ChangedOnUtc });
        modelBuilder.Entity<PlanInventoryAllocationSnapshot>().HasIndex(x => new { x.PlanVersionId, x.ProductionOrderId, x.Stage });
        modelBuilder.Entity<PlanMaterialUnitSnapshot>().HasIndex(x => new { x.PlanVersionId, x.PlanningKey }).IsUnique();
        modelBuilder.Entity<PlanDemandSnapshot>().HasIndex(x => new { x.PlanVersionId, x.SalesOrderId }).IsUnique();
        modelBuilder.Entity<PlanDemandCoverageSnapshot>().HasIndex(x => new { x.PlanVersionId, x.SalesOrderId, x.MaterialCode, x.GradeCode, x.CrossSectionCode, x.LocationCode });
        modelBuilder.Entity<PlanProductionOrderSnapshot>().HasIndex(x => new { x.PlanVersionId, x.ProductionOrderId }).IsUnique();
        modelBuilder.Entity<PlanCampaignSnapshot>().HasIndex(x => new { x.PlanVersionId, x.CampaignId }).IsUnique();
        modelBuilder.Entity<PlanCampaignAllocationSnapshot>().HasIndex(x => new { x.PlanVersionId, x.CampaignId, x.ProductionOrderId });
        modelBuilder.Entity<PlanCampaignGradeSequenceSnapshot>().HasIndex(x => new { x.PlanVersionId, x.CampaignId, x.SequenceNumber }).IsUnique();
        modelBuilder.Entity<PlanHeatSnapshot>().HasIndex(x => new { x.PlanVersionId, x.CampaignHeatId }).IsUnique();
        modelBuilder.Entity<PlanHeatAllocationSnapshot>().HasIndex(x => new { x.PlanVersionId, x.CampaignHeatId, x.ProductionOrderId });
        modelBuilder.Entity<PlanCastSequenceSnapshot>().HasIndex(x => new { x.PlanVersionId, x.CastSequenceId }).IsUnique();
        modelBuilder.Entity<PlanCastSequenceHeatSnapshot>().HasIndex(x => new { x.PlanVersionId, x.CastSequenceId, x.Position }).IsUnique();
        modelBuilder.Entity<PlanRollingPlanSnapshot>().HasIndex(x => new { x.PlanVersionId, x.RollingPlanId }).IsUnique();
        modelBuilder.Entity<PlanRollingPlanAllocationSnapshot>().HasIndex(x => new { x.PlanVersionId, x.RollingPlanId, x.ProductionOrderId });
        modelBuilder.Entity<PlanRouteOperationSnapshot>().HasIndex(x => new { x.PlanVersionId, x.RouteOperationPlanId }).IsUnique();
        modelBuilder.Entity<PlanRouteOperationAllocationSnapshot>().HasIndex(x => new { x.PlanVersionId, x.RouteOperationPlanId, x.ProductionOrderId, x.CampaignId });
        modelBuilder.Entity<PlanPackagingUnitSnapshot>().HasIndex(x => new { x.PlanVersionId, x.PlannedPackagingUnitId }).IsUnique();
        modelBuilder.Entity<ManufacturingRoute>().HasIndex(x => x.RouteCode).IsUnique();
        modelBuilder.Entity<ManufacturingRouteOperation>().HasIndex(x => new { x.RouteCode, x.SequenceNumber }).IsUnique();
        modelBuilder.Entity<RouteResourceCapability>().HasIndex(x => new { x.RouteCode, x.ResourceId, x.ProcessOperationType });

        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetPrecision(18);
            property.SetScale(4);
        }
    }
}
