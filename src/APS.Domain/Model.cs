namespace APS.Domain;

public enum DemandSourceType { MakeToOrder = 1, MakeToStock = 2 }
public enum ProductionOrderStatus { Planned = 1, Firmed = 2, Released = 3, Completed = 4, Cancelled = 5 }
public enum CampaignStatus { Draft = 1, Planned = 2, Firmed = 3, Released = 4, Completed = 5, Cancelled = 6 }
public enum WorkOrderStatus { Planned = 1, Released = 2, Ready = 3, Running = 4, Held = 5, Completed = 6, Cancelled = 7 }
public enum WorkOrderType { Steelmaking = 1, Casting = 2, HotRolling = 3, ColdRolling = 4, Finishing = 5 }
public enum ResourceType { Generic = 0, Furnace = 1, Refining = 2, Caster = 3, RollingMill = 4, FinishingLine = 5, Buffer = 6 }
public enum ProcessUnitType { Unknown = 0, Eaf = 1, Lrf = 2, Vd = 3, Ccm = 4, ReheatingFurnace = 5, HotRollingMill = 6, ColdRollingMill = 7, TmtWaterBox = 8, CoolingBed = 9, Shear = 10, BundlingLine = 11, Coiler = 12, FinishingLine = 13, MaterialBuffer = 14 }
public enum ProcessOperationType { Unknown = 0, Eaf = 1, Lrf = 2, Vd = 3, Ccm = 4, Reheat = 5, HotRoll = 6, ColdRoll = 7, Tmt = 8, Cool = 9, Cut = 10, Bundle = 11, Coil = 12, Finish = 13 }
public enum RequirementDisposition { Forbidden = 0, Optional = 1, Required = 2 }
public enum ResourceOperatingState { Available = 1, PlannedMaintenance = 2, Breakdown = 3, CapacityDerated = 4, QualityRestricted = 5, Disabled = 6 }
public enum MaterialLotStatus { Available = 1, Reserved = 2, Consumed = 3, Held = 4, Scrapped = 5 }
public enum FlowCouplingType { Direct = 1, HotTransfer = 2, Buffered = 3, InventoryDecoupled = 4 }
public enum TransitionDimension { Grade = 1, CrossSection = 2, ProductFamily = 3 }
public enum TransitionRuleScope { Default = 1, GradeFamily = 2, SequenceClass = 3, ExactCode = 4 }
public enum LotAllocationStatus { Planned = 1, Reserved = 2, ConsumedForOrder = 3, Delivered = 4, Cancelled = 5 }
public enum InventoryStage { FinishedGoods = 1, CastIntermediate = 2, OtherIntermediate = 3, RawMaterial = 4, InTransit = 5 }
public enum SegregationPolicy { None = 0, SameCustomerOnly = 1, SameSalesOrderOnly = 2, SameHeatOnly = 3, DedicatedCampaign = 4 }
public enum SteelMaterialStage { LiquidSteel = 1, CastIntermediate = 2, RolledIntermediate = 3, FinishedGoods = 4 }
public enum SteelProductForm { LiquidSteel = 1, Billet = 2, Bloom = 3, Slab = 4, Bar = 5, Rod = 6, Coil = 7, Section = 8, Bundle = 9, Other = 10 }
public enum CrossSectionShape { Unknown = 0, Square = 1, Rectangle = 2, Round = 3, Flat = 4, ISection = 5, Channel = 6, Angle = 7, Custom = 8 }
public enum PackagingUnitType { Bundle = 1, Coil = 2, Piece = 3 }
public enum BilletSupplySourceType { InternalCastPlanned = 1, InternalCastActual = 2, ExistingInventory = 3, ExternalPurchased = 4, InTransit = 5, ManualPlannerSupply = 6 }
public enum MaterialQualityStatus { Available = 1, QualityHold = 2, Blocked = 3, Rejected = 4, Released = 5 }
public enum ChargeMode { HotDirect = 1, HotBuffered = 2, ColdCharge = 3 }

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
    public string? CustomerGroupCode { get; set; }
    public string? ExternalStatus { get; set; }
}

public sealed class ProductionOrder : Entity
{
    public required string ProductionOrderNumber { get; set; }
    public DemandSourceType DemandSource { get; set; }
    public required string MaterialCode { get; set; }
    public required string GradeCode { get; set; }
    public Guid? SteelGradeId { get; set; }
    public SteelGrade? SteelGrade { get; set; }
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
    public ProductionOrderRequirement? Requirement { get; set; }

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
    public decimal PlannedQuantityMt { get; set; }
    public decimal FreshSteelRequirementMt { get; set; }
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
    public decimal PlannedQuantityMt { get; set; }
    public decimal ExistingIntermediateInventoryMt { get; set; }
    public decimal FreshSteelQuantityMt { get; set; }
}

public sealed class CampaignGradeSequence : Entity
{
    public Guid CampaignId { get; set; }
    public Campaign? Campaign { get; set; }
    public int SequenceNumber { get; set; }
    public required string GradeCode { get; set; }
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
    public decimal? MinimumFeasibleQuantityMt { get; set; }
    public decimal? TargetQuantityMt { get; set; }
    public decimal? MaximumFeasibleQuantityMt { get; set; }
    public Guid? PreferredSteelmakingResourceId { get; set; }
    public Guid? PreferredCasterResourceId { get; set; }
}

public sealed class CastSequence : Entity
{
    public Guid? CampaignId { get; set; }
    public Guid CasterResourceId { get; set; }
    public int SequenceNumber { get; set; }
    public required string CasterSectionCode { get; set; }
    public required string RouteCode { get; set; }
    public int? TundishNumber { get; set; }
    public DateTime? PlannedStart { get; set; }
    public DateTime? PlannedEnd { get; set; }
    public ICollection<CastSequenceHeat> Heats { get; set; } = new List<CastSequenceHeat>();
}

public sealed class CastSequenceHeat : Entity
{
    public Guid CastSequenceId { get; set; }
    public CastSequence? CastSequence { get; set; }
    public Guid CampaignHeatId { get; set; }
    public CampaignHeat CampaignHeat { get; set; } = null!;
    public int Position { get; set; }
}

public sealed class RollingPlan : Entity
{
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

public sealed class PlantArea : Entity
{
    public Guid PlantId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int SequenceNumber { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ProcessStage : Entity
{
    public Guid PlantId { get; set; }
    public Guid? PlantAreaId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public ProcessOperationType ProcessOperationType { get; set; }
    public int SequenceNumber { get; set; }
}

public sealed class Resource : Entity
{
    public Guid PlantId { get; set; }
    public Guid ProcessStageId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public ResourceType ResourceType { get; set; }
    public ProcessUnitType ProcessUnitType { get; set; }
    public ResourceOperatingState OperatingState { get; set; } = ResourceOperatingState.Available;
    public decimal CapacityFactorPct { get; set; } = 100m;

    // Heat/tap/capacity properties belong to the physical unit that owns them.
    public decimal? MinimumHeatWeightMt { get; set; }
    public decimal? NominalHeatWeightMt { get; set; }
    public decimal? MaximumHeatWeightMt { get; set; }
    public decimal? LadleCapacityMt { get; set; }
    public decimal? WorkingCapacityMt { get; set; }
    public decimal? NominalThroughputMtPerHour { get; set; }
    public int? MinimumResidenceMinutes { get; set; }
    public int? NominalResidenceMinutes { get; set; }
    public int? MaximumResidenceMinutes { get; set; }

    // CCM properties.
    public int? StrandCount { get; set; }
    public int? MaximumHeatsPerSequence { get; set; }
    public int? MaximumHeatsPerTundish { get; set; }
    public decimal? MinimumCastingSpeedMPerMin { get; set; }
    public decimal? NominalCastingSpeedMPerMin { get; set; }
    public decimal? MaximumCastingSpeedMPerMin { get; set; }
    public decimal? ExpectedYieldPct { get; set; }

    // Billet heating/thermal properties.
    public bool SupportsHotCharge { get; set; }
    public bool SupportsColdCharge { get; set; }
    public decimal? TargetDischargeTemperatureC { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ResourceCapability : Entity
{
    public Guid ResourceId { get; set; }
    public ProcessOperationType? ProcessOperationType { get; set; }
    public string? CapabilityClassCode { get; set; }
    public string? GradeCode { get; set; }
    public string? GradeFamilyCode { get; set; }
    public string? CastingClassCode { get; set; }
    public string? MaterialSpecificationCode { get; set; }
    public string? InputCrossSectionCode { get; set; }
    public string? OutputCrossSectionCode { get; set; }
    public string? RouteCode { get; set; }
    public string? ProductFamilyCode { get; set; }
    public decimal? MinimumQuantityMt { get; set; }
    public decimal? MaximumQuantityMt { get; set; }
    public decimal? ThroughputMtPerHour { get; set; }
    public int? FixedDurationMinutes { get; set; }
    public int AssignmentPenalty { get; set; }
    public bool IsPreferred { get; set; }
}

public sealed class ResourceCalendar : Entity
{
    public Guid ResourceId { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsAvailable { get; set; }
    public decimal? CapacityFactorPct { get; set; }
    public string? ReasonCode { get; set; }
}

public sealed class PlantFlowLink : Entity
{
    public Guid FromResourceId { get; set; }
    public Guid ToResourceId { get; set; }
    public ProcessOperationType? FromProcessOperationType { get; set; }
    public ProcessOperationType? ToProcessOperationType { get; set; }
    public FlowCouplingType CouplingType { get; set; }
    public TimeSpan MinimumTransferTime { get; set; }
    public TimeSpan? MaximumTransferTime { get; set; }
    public bool SupportsHotTransfer { get; set; }
    public bool IsInventoryDecouplingPoint { get; set; }
    public decimal? NominalTemperatureLossCPerMinute { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class TransitionRule : Entity
{
    public Guid? ResourceId { get; set; }
    public ResourceType? ResourceType { get; set; }
    public ProcessUnitType? ProcessUnitType { get; set; }
    public ProcessOperationType? ProcessOperationType { get; set; }
    public TransitionRuleScope Scope { get; set; } = TransitionRuleScope.ExactCode;
    public TransitionDimension Dimension { get; set; }
    public required string FromCode { get; set; }
    public required string ToCode { get; set; }
    public bool IsAllowed { get; set; } = true;
    public bool RequiresSequenceBreak { get; set; }
    public int Penalty { get; set; }
    public TimeSpan TransitionTime { get; set; }
    public string? ReasonCode { get; set; }
}

public sealed class SteelGrade : Entity
{
    public required string GradeCode { get; set; }
    public required string Description { get; set; }
    public string? GradeFamilyCode { get; set; }
    public string? SequenceClassCode { get; set; }
    public string? CastingClassCode { get; set; }
    public string? QualityClassCode { get; set; }
    public string? DefaultCasterSectionCode { get; set; }
    public string? DefaultRouteCode { get; set; }
    public decimal? LiquidusTemperatureC { get; set; }
    public decimal? MinimumSuperheatC { get; set; }
    public decimal? TargetSuperheatC { get; set; }
    public decimal? MaximumSuperheatC { get; set; }
    public decimal? MinimumCastingTemperatureC { get; set; }
    public decimal? TargetCastingTemperatureC { get; set; }
    public decimal? MaximumCastingTemperatureC { get; set; }
    public bool HotChargeEligible { get; set; } = true;
    public bool ColdChargeEligible { get; set; } = true;
    public bool TmtApplicable { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<GradeChemistryRequirement> Chemistry { get; set; } = new List<GradeChemistryRequirement>();
    public ICollection<GradeProcessRequirement> ProcessRequirements { get; set; } = new List<GradeProcessRequirement>();
}

public sealed class GradeChemistryRequirement : Entity
{
    public Guid SteelGradeId { get; set; }
    public SteelGrade? SteelGrade { get; set; }
    public required string ElementCode { get; set; }
    public decimal? MinimumPct { get; set; }
    public decimal? TargetPct { get; set; }
    public decimal? MaximumPct { get; set; }
}

public sealed class GradeProcessRequirement : Entity
{
    public Guid SteelGradeId { get; set; }
    public SteelGrade? SteelGrade { get; set; }
    public ProcessOperationType ProcessOperationType { get; set; }
    public RequirementDisposition Requirement { get; set; }
    public string? CapabilityClassCode { get; set; }
    public int? MinimumProcessMinutes { get; set; }
    public int? MaximumProcessMinutes { get; set; }
    public int? MaximumQueueMinutesAfterOperation { get; set; }
    public decimal? MinimumHeatWeightMt { get; set; }
    public decimal? TargetHeatWeightMt { get; set; }
    public decimal? MaximumHeatWeightMt { get; set; }
    public decimal? ExpectedYieldPct { get; set; }
}

public sealed class ProductionOrderRequirement : Entity
{
    public Guid ProductionOrderId { get; set; }
    public ProductionOrder? ProductionOrder { get; set; }
    public string? CustomerCode { get; set; }
    public string? CustomerGroupCode { get; set; }
    public string? RequirementReference { get; set; }
    public string? QualityClassCode { get; set; }
    public SegregationPolicy SegregationPolicy { get; set; }
    public bool? RequireVd { get; set; }
    public bool? ForbidVd { get; set; }
    public bool? RequireReheating { get; set; }
    public bool? ForbidHotCharge { get; set; }
    public bool? RequireTmt { get; set; }
    public string? RequiredRouteCode { get; set; }
    public Guid? RequiredResourceId { get; set; }
    public string? RequiredResourceGroupCode { get; set; }
    public decimal? MinimumSuperheatC { get; set; }
    public decimal? TargetSuperheatC { get; set; }
    public decimal? MaximumSuperheatC { get; set; }
    public decimal? MinimumCastingTemperatureC { get; set; }
    public decimal? MaximumCastingTemperatureC { get; set; }
    public decimal? CutLengthM { get; set; }
    public decimal? TargetBundleWeightMt { get; set; }
    public decimal? MinimumBundleWeightMt { get; set; }
    public decimal? MaximumBundleWeightMt { get; set; }
    public decimal? TargetCoilWeightMt { get; set; }
    public decimal? MinimumCoilWeightMt { get; set; }
    public decimal? MaximumCoilWeightMt { get; set; }
    public bool? AllowMixedHeatBundle { get; set; }
    public string? MarkingRequirementCode { get; set; }
    public string? InspectionRequirementCode { get; set; }
    public ICollection<OrderChemistryRequirement> ChemistryOverrides { get; set; } = new List<OrderChemistryRequirement>();
    public ICollection<OrderProcessRequirement> ProcessOverrides { get; set; } = new List<OrderProcessRequirement>();
}

public sealed class OrderChemistryRequirement : Entity
{
    public Guid ProductionOrderRequirementId { get; set; }
    public required string ElementCode { get; set; }
    public decimal? MinimumPct { get; set; }
    public decimal? TargetPct { get; set; }
    public decimal? MaximumPct { get; set; }
}

public sealed class OrderProcessRequirement : Entity
{
    public Guid ProductionOrderRequirementId { get; set; }
    public ProcessOperationType ProcessOperationType { get; set; }
    public RequirementDisposition Requirement { get; set; }
    public string? CapabilityClassCode { get; set; }
    public Guid? RequiredResourceId { get; set; }
    public int? MaximumQueueMinutes { get; set; }
}

public sealed class CrossSectionSpecification : Entity
{
    public required string CrossSectionCode { get; set; }
    public CrossSectionShape Shape { get; set; }
    public decimal? WidthMm { get; set; }
    public decimal? HeightMm { get; set; }
    public decimal? ThicknessMm { get; set; }
    public decimal? DiameterMm { get; set; }
    public string? SectionFamilyCode { get; set; }
    public string? CasterFormatClassCode { get; set; }
    public string? RollingFamilyCode { get; set; }
    public decimal? TheoreticalKgPerM { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class MaterialSpecification : Entity
{
    public required string MaterialSpecificationCode { get; set; }
    public string? SapMaterialCode { get; set; }
    public required string Name { get; set; }
    public SteelMaterialStage Stage { get; set; }
    public SteelProductForm ProductForm { get; set; }
    public string? GradeCode { get; set; }
    public string? GradeFamilyCode { get; set; }
    public string? CrossSectionCode { get; set; }
    public string? ProductFamilyCode { get; set; }
    public string? RouteFamilyCode { get; set; }
    public decimal? StandardCutLengthM { get; set; }
    public decimal? UnitWeightKg { get; set; }
    public decimal? ExpectedYieldPct { get; set; }
    public bool TmtApplicable { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class PackagingSpecification : Entity
{
    public required string PackagingCode { get; set; }
    public required string MaterialSpecificationCode { get; set; }
    public PackagingUnitType PackagingUnitType { get; set; }
    public decimal? StandardCutLengthM { get; set; }
    public decimal? TargetUnitWeightMt { get; set; }
    public decimal? MinimumUnitWeightMt { get; set; }
    public decimal? MaximumUnitWeightMt { get; set; }
    public int? TargetPiecesPerUnit { get; set; }
    public bool AllowMixedHeats { get; set; }
    public bool AllowMixedLots { get; set; }
    public bool AllowRemainderUnit { get; set; } = true;
    public string? MarkingRequirementCode { get; set; }
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
    public string? MaterialSpecificationCode { get; set; }
    public required string GradeCode { get; set; }
    public required string CrossSectionCode { get; set; }
    public SteelProductForm ProductForm { get; set; } = SteelProductForm.Other;
    public InventoryStage Stage { get; set; } = InventoryStage.OtherIntermediate;
    public MaterialQualityStatus QualityStatus { get; set; } = MaterialQualityStatus.Available;
    public BilletSupplySourceType? SupplySourceType { get; set; }
    public string? SupplierCode { get; set; }
    public string? CertificateReference { get; set; }
    public decimal QuantityMt { get; set; }
    public MaterialLotStatus Status { get; set; } = MaterialLotStatus.Available;
    public string? LocationCode { get; set; }
    public Guid? ProducedByWorkOrderId { get; set; }
    public string? HeatNumber { get; set; }
    public string? CastNumber { get; set; }
    public int? StrandNumber { get; set; }
    public DateTime ProducedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AvailableFromUtc { get; set; }
    public ChargeMode? ThermalState { get; set; }
    public decimal? EstimatedTemperatureC { get; set; }
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

public sealed class ExternalMaterialSupply : Entity
{
    public BilletSupplySourceType SourceType { get; set; }
    public required string SupplyReference { get; set; }
    public string? SupplierCode { get; set; }
    public string? CertificateReference { get; set; }
    public string? MaterialSpecificationCode { get; set; }
    public required string GradeCode { get; set; }
    public required string CrossSectionCode { get; set; }
    public decimal QuantityMt { get; set; }
    public decimal ReservedQuantityMt { get; set; }
    public DateTime AvailableFromUtc { get; set; }
    public string? LocationCode { get; set; }
    public MaterialQualityStatus QualityStatus { get; set; } = MaterialQualityStatus.Available;
    public ChargeMode? ThermalState { get; set; }
    public decimal? EstimatedTemperatureC { get; set; }
    public bool IsFirm { get; set; } = true;
    public int UsagePenalty { get; set; }
}

public sealed class PlannedPackagingUnit : Entity
{
    public Guid ProductionOrderId { get; set; }
    public Guid? WorkOrderId { get; set; }
    public PackagingUnitType PackagingUnitType { get; set; }
    public int SequenceNumber { get; set; }
    public decimal PlannedWeightMt { get; set; }
    public int? PlannedPieceCount { get; set; }
    public decimal? CutLengthM { get; set; }
    public string? PackagingCode { get; set; }
    public string? PlannedIdentifier { get; set; }
}

public sealed class InventoryPosition
{
    public required string MaterialCode { get; init; }
    public required string GradeCode { get; init; }
    public required string CrossSectionCode { get; init; }
    public InventoryStage Stage { get; init; } = InventoryStage.FinishedGoods;
    public string? LocationCode { get; init; }
    public DateTime? AvailableFromUtc { get; init; }
    public MaterialQualityStatus QualityStatus { get; init; } = MaterialQualityStatus.Available;
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
    public ProcessOperationType? ProcessOperationType { get; set; }
    public string? PlanningKey { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsFrozen { get; set; }
}
