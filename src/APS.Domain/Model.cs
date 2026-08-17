namespace APS.Domain;

public enum DemandSourceType { MakeToOrder = 1, MakeToStock = 2 }
public enum ProductionOrderStatus { Planned = 1, Firmed = 2, Released = 3, Completed = 4, Cancelled = 5 }
public enum CampaignStatus { Draft = 1, Planned = 2, Firmed = 3, Released = 4, Completed = 5, Cancelled = 6 }
public enum WorkOrderStatus { Planned = 1, Released = 2, Ready = 3, Running = 4, Held = 5, Completed = 6, Cancelled = 7 }
public enum WorkOrderType { Steelmaking = 1, Casting = 2, HotRolling = 3, ColdRolling = 4, Finishing = 5 }
public enum ResourceType { Generic = 0, Furnace = 1, Refining = 2, Caster = 3, RollingMill = 4, FinishingLine = 5, Buffer = 6 }
public enum MaterialLotStatus { Available = 1, Reserved = 2, Consumed = 3, Held = 4, Scrapped = 5 }
public enum FlowCouplingType { Direct = 1, HotTransfer = 2, Buffered = 3, InventoryDecoupled = 4 }
public enum TransitionDimension { Grade = 1, CrossSection = 2, ProductFamily = 3 }
public enum LotAllocationStatus { Planned = 1, Reserved = 2, ConsumedForOrder = 3, Delivered = 4, Cancelled = 5 }
public enum InventoryStage { FinishedGoods = 1, CastIntermediate = 2, OtherIntermediate = 3, RawMaterial = 4 }

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

public sealed class SalesOrder : Entity
{
    public required string SalesOrderNumber { get; set; }
    public required string ItemNumber { get; set; }
    public required string MaterialCode { get; set; }
    public required string GradeCode { get; set; }
    public required string FinalCrossSectionCode { get; set; }
    public decimal OrderQuantityMt { get; set; }
    public decimal OpenQuantityMt { get; set; }
    public DateTime RequiredDate { get; set; }
    public string? CustomerCode { get; set; }
    public string? ExternalStatus { get; set; }
}

public sealed class ProductionOrder : Entity
{
    public required string ProductionOrderNumber { get; set; }
    public DemandSourceType DemandSource { get; set; }
    public required string MaterialCode { get; set; }
    public required string GradeCode { get; set; }
    public string? GradeFamilyCode { get; set; }
    public string? GradeSequenceClassCode { get; set; }
    public required string FinalCrossSectionCode { get; set; }
    public required string CasterSectionCode { get; set; }
    public required string RouteCode { get; set; }
    public string? ProductFamilyCode { get; set; }
    public decimal PlannedQuantityMt { get; set; }
    public decimal RemainingQuantityMt { get; set; }
    public DateTime RequiredDate { get; set; }
    public int Priority { get; set; }
    public ProductionOrderStatus Status { get; set; } = ProductionOrderStatus.Planned;

    public Guid? SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }

    // Populated for APS-generated MTS Production Orders.
    public decimal? TargetStockMt { get; set; }
    public decimal? ProjectedAvailableStockMt { get; set; }
    public string? StockPolicyCode { get; set; }
}

public sealed class Campaign : Entity
{
    public required string CampaignNumber { get; set; }
    public required string GradeSequenceClassCode { get; set; }
    public required string CasterSectionCode { get; set; }
    public required string RouteCode { get; set; }

    // Quantity still requiring rolling after finished-goods inventory netting.
    public decimal PlannedQuantityMt { get; set; }

    // Portion that requires fresh steelmaking/casting after intermediate inventory netting.
    public decimal FreshSteelRequirementMt { get; set; }

    // Existing compatible billet/slab/intermediate inventory assigned to this campaign.
    public decimal ExistingIntermediateInventoryMt { get; set; }

    public DateTime RequiredDate { get; set; }
    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;

    public ICollection<CampaignAllocation> Allocations { get; set; } = new List<CampaignAllocation>();
    public ICollection<CampaignGradeSequence> GradeSequence { get; set; } = new List<CampaignGradeSequence>();
    public ICollection<CampaignHeat> Heats { get; set; } = new List<CampaignHeat>();
}

public sealed class CampaignAllocation : Entity
{
    public Guid CampaignId { get; set; }
    public Campaign? Campaign { get; set; }
    public Guid ProductionOrderId { get; set; }
    public ProductionOrder? ProductionOrder { get; set; }

    // Total quantity still requiring production operations after FG inventory netting.
    public decimal PlannedQuantityMt { get; set; }

    // Portion supplied by compatible intermediate inventory and therefore not requiring SMS/casting.
    public decimal ExistingIntermediateInventoryMt { get; set; }

    // Portion requiring fresh steelmaking/casting.
    public decimal FreshSteelQuantityMt { get; set; }
}

public sealed class CampaignGradeSequence : Entity
{
    public Guid CampaignId { get; set; }
    public Campaign? Campaign { get; set; }
    public int SequenceNumber { get; set; }
    public required string GradeCode { get; set; }

    // Fresh steel quantity only; rolling-only quantities covered from intermediate stock do not create heats.
    public decimal PlannedQuantityMt { get; set; }
}

public sealed class CampaignHeat : Entity
{
    public Guid CampaignId { get; set; }
    public Campaign? Campaign { get; set; }
    public Guid CampaignGradeSequenceId { get; set; }
    public CampaignGradeSequence? CampaignGradeSequence { get; set; }
    public int SequenceNumber { get; set; }
    public required string GradeCode { get; set; }
    public decimal PlannedQuantityMt { get; set; }
    public Guid? PreferredCasterResourceId { get; set; }
}

public sealed class CastSequence : Entity
{
    // Null when a caster sequence spans heats from more than one campaign.
    public Guid? CampaignId { get; set; }
    public Guid CasterResourceId { get; set; }
    public int SequenceNumber { get; set; }
    public required string CasterSectionCode { get; set; }
    public required string RouteCode { get; set; }
    public DateTime? PlannedStart { get; set; }
    public DateTime? PlannedEnd { get; set; }
    public ICollection<CastSequenceHeat> Heats { get; set; } = new List<CastSequenceHeat>();
}

public sealed class CastSequenceHeat : Entity
{
    public Guid CastSequenceId { get; set; }
    public CastSequence? CastSequence { get; set; }
    public Guid CampaignHeatId { get; set; }
    public CampaignHeat? CampaignHeat { get; set; }
    public int Position { get; set; }
}

public sealed class RollingPlan : Entity
{
    // Populated only when the plan is sourced from a single campaign/PO; allocations remain authoritative.
    public Guid? CampaignId { get; set; }
    public Guid? ProductionOrderId { get; set; }
    public Guid? RollingMillResourceId { get; set; }
    public int SequenceNumber { get; set; }
    public required string GradeCode { get; set; }
    public required string InputCrossSectionCode { get; set; }
    public required string OutputCrossSectionCode { get; set; }
    public required string RouteCode { get; set; }
    public decimal PlannedQuantityMt { get; set; }
    public decimal ExistingIntermediateInventoryMt { get; set; }
    public decimal FreshSteelQuantityMt { get; set; }
    public ICollection<RollingPlanAllocation> Allocations { get; set; } = new List<RollingPlanAllocation>();
}

public sealed class RollingPlanAllocation : Entity
{
    public Guid RollingPlanId { get; set; }
    public RollingPlan? RollingPlan { get; set; }
    public Guid CampaignId { get; set; }
    public Guid ProductionOrderId { get; set; }
    public ProductionOrder? ProductionOrder { get; set; }
    public decimal PlannedQuantityMt { get; set; }
    public decimal ExistingIntermediateInventoryMt { get; set; }
    public decimal FreshSteelQuantityMt { get; set; }
}

public sealed class Plant : Entity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
}

public sealed class ProcessStage : Entity
{
    public Guid PlantId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int SequenceNumber { get; set; }
}

public sealed class Resource : Entity
{
    public Guid PlantId { get; set; }
    public Guid ProcessStageId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public ResourceType ResourceType { get; set; }
    public int? StrandCount { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ResourceCapability : Entity
{
    public Guid ResourceId { get; set; }
    public string? GradeCode { get; set; }
    public string? GradeFamilyCode { get; set; }
    public string? InputCrossSectionCode { get; set; }
    public string? OutputCrossSectionCode { get; set; }
    public string? RouteCode { get; set; }
    public string? ProductFamilyCode { get; set; }
    public decimal? ThroughputMtPerHour { get; set; }
}

public sealed class ResourceCalendar : Entity
{
    public Guid ResourceId { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsAvailable { get; set; }
    public string? ReasonCode { get; set; }
}

public sealed class PlantFlowLink : Entity
{
    public Guid FromResourceId { get; set; }
    public Guid ToResourceId { get; set; }
    public FlowCouplingType CouplingType { get; set; }
    public TimeSpan MinimumTransferTime { get; set; }
    public TimeSpan? MaximumTransferTime { get; set; }
    public bool SupportsHotTransfer { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class TransitionRule : Entity
{
    public Guid? ResourceId { get; set; }
    public ResourceType? ResourceType { get; set; }
    public TransitionDimension Dimension { get; set; }
    public required string FromCode { get; set; }
    public required string ToCode { get; set; }
    public bool IsAllowed { get; set; } = true;
    public int Penalty { get; set; }
    public TimeSpan TransitionTime { get; set; }
}

public sealed class WorkOrder : Entity
{
    public required string WorkOrderNumber { get; set; }
    public WorkOrderType WorkOrderType { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? ResourceId { get; set; }
    public required string MaterialCode { get; set; }
    public required string GradeCode { get; set; }
    public required string CrossSectionCode { get; set; }
    public decimal PlannedQuantityMt { get; set; }
    public decimal ActualQuantityMt { get; set; }
    public DateTime? PlannedStart { get; set; }
    public DateTime? PlannedEnd { get; set; }
    public DateTime? ActualStart { get; set; }
    public DateTime? ActualEnd { get; set; }
    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Planned;
    public string? ExternalExecutionId { get; set; }
    public ICollection<WorkOrderAllocation> Allocations { get; set; } = new List<WorkOrderAllocation>();
}

public sealed class WorkOrderAllocation : Entity
{
    public Guid WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }
    public Guid ProductionOrderId { get; set; }
    public ProductionOrder? ProductionOrder { get; set; }
    public decimal PlannedQuantityMt { get; set; }
}

public sealed class MaterialLot : Entity
{
    public required string LotNumber { get; set; }
    public required string MaterialCode { get; set; }
    public required string GradeCode { get; set; }
    public required string CrossSectionCode { get; set; }
    public decimal QuantityMt { get; set; }
    public MaterialLotStatus Status { get; set; } = MaterialLotStatus.Available;
    public string? LocationCode { get; set; }
    public Guid? ProducedByWorkOrderId { get; set; }
    public string? HeatNumber { get; set; }
    public string? CastNumber { get; set; }
    public int? StrandNumber { get; set; }
    public DateTime ProducedOnUtc { get; set; } = DateTime.UtcNow;
}

public sealed class LotGenealogy : Entity
{
    public Guid ParentLotId { get; set; }
    public Guid ChildLotId { get; set; }
    public decimal QuantityMt { get; set; }
    public Guid? TransformationWorkOrderId { get; set; }
}

public sealed class MaterialLotAllocation : Entity
{
    public Guid MaterialLotId { get; set; }
    public Guid ProductionOrderId { get; set; }
    public decimal AllocatedQuantityMt { get; set; }
    public LotAllocationStatus Status { get; set; } = LotAllocationStatus.Planned;
}

public sealed class InventoryPosition
{
    public required string MaterialCode { get; init; }
    public required string GradeCode { get; init; }
    public required string CrossSectionCode { get; init; }
    public InventoryStage Stage { get; init; } = InventoryStage.FinishedGoods;
    public string? LocationCode { get; init; }
    public decimal AvailableQuantityMt { get; init; }
    public decimal ReservedQuantityMt { get; init; }
    public decimal ConfirmedIncomingQuantityMt { get; init; }
    public decimal AllocatedOutgoingQuantityMt { get; init; }

    public decimal ProjectedAvailableQuantityMt =>
        AvailableQuantityMt - ReservedQuantityMt + ConfirmedIncomingQuantityMt - AllocatedOutgoingQuantityMt;
}

public sealed class PlanVersion : Entity
{
    public required string VersionNumber { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }
    public bool IsReleased { get; set; }
}

public sealed class ScheduledOperation : Entity
{
    public Guid PlanVersionId { get; set; }
    public Guid WorkOrderId { get; set; }
    public Guid ResourceId { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsFrozen { get; set; }
}
