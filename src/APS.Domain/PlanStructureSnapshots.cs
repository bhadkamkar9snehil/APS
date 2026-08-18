namespace APS.Domain;

/// <summary>
/// Immutable commercial requirement snapshot retained with a Plan Version.
/// This prevents later SO/PO/master changes from rewriting what the planner actually planned.
/// </summary>
public sealed class PlanProductionOrderSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public Guid ProductionOrderId { get; set; }
    public required string ProductionOrderNumber { get; set; }
    public DemandSourceType DemandSource { get; set; }
    public Guid? SalesOrderId { get; set; }
    public string? SalesOrderNumber { get; set; }
    public string? SalesOrderItemNumber { get; set; }
    public string? CustomerCode { get; set; }
    public string? CustomerGroupCode { get; set; }
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
    public ProductionOrderStatus Status { get; set; }
    public decimal? TargetStockMt { get; set; }
    public decimal? ProjectedAvailableStockMt { get; set; }
    public string? StockPolicyCode { get; set; }
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
    public decimal? MinimumSuperheatC { get; set; }
    public decimal? TargetSuperheatC { get; set; }
    public decimal? MaximumSuperheatC { get; set; }
    public decimal? MinimumCastingTemperatureC { get; set; }
    public decimal? MaximumCastingTemperatureC { get; set; }
    public decimal? CutLengthM { get; set; }
    public decimal? TargetBundleWeightMt { get; set; }
    public decimal? TargetCoilWeightMt { get; set; }
    public string? MarkingRequirementCode { get; set; }
    public string? InspectionRequirementCode { get; set; }
}

public sealed class PlanCampaignSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public Guid CampaignId { get; set; }
    public required string CampaignNumber { get; set; }
    public required string GradeSequenceClassCode { get; set; }
    public required string CasterSectionCode { get; set; }
    public required string RouteCode { get; set; }
    public decimal PlannedQuantityMt { get; set; }
    public decimal FreshSteelRequirementMt { get; set; }
    public decimal ExistingIntermediateInventoryMt { get; set; }
    public DateTime RequiredDate { get; set; }
    public CampaignStatus Status { get; set; }
}

public sealed class PlanCampaignAllocationSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid ProductionOrderId { get; set; }
    public decimal PlannedQuantityMt { get; set; }
    public decimal ExistingIntermediateInventoryMt { get; set; }
    public decimal FreshSteelQuantityMt { get; set; }
}

public sealed class PlanCampaignGradeSequenceSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public Guid CampaignId { get; set; }
    public int SequenceNumber { get; set; }
    public required string GradeCode { get; set; }
    public decimal PlannedQuantityMt { get; set; }
}

public sealed class PlanHeatSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public Guid CampaignHeatId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid CampaignGradeSequenceId { get; set; }
    public int SequenceNumber { get; set; }
    public required string GradeCode { get; set; }
    public decimal PlannedQuantityMt { get; set; }
    public decimal? MinimumFeasibleQuantityMt { get; set; }
    public decimal? TargetQuantityMt { get; set; }
    public decimal? MaximumFeasibleQuantityMt { get; set; }
    public Guid? PreferredSteelmakingResourceId { get; set; }
    public Guid? PreferredCasterResourceId { get; set; }
}

public sealed class PlanHeatAllocationSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public Guid CampaignHeatId { get; set; }
    public Guid ProductionOrderId { get; set; }
    public decimal PlannedOutputQuantityMt { get; set; }
    public decimal PlannedInputQuantityMt { get; set; }
}

public sealed class PlanCastSequenceSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public Guid CastSequenceId { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid CasterResourceId { get; set; }
    public int SequenceNumber { get; set; }
    public required string CasterSectionCode { get; set; }
    public required string RouteCode { get; set; }
    public int? TundishNumber { get; set; }
    public DateTime? PlannedStart { get; set; }
    public DateTime? PlannedEnd { get; set; }
}

public sealed class PlanCastSequenceHeatSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public Guid CastSequenceId { get; set; }
    public Guid CampaignHeatId { get; set; }
    public int Position { get; set; }
}

public sealed class PlanRollingPlanSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public Guid RollingPlanId { get; set; }
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
}

public sealed class PlanRollingPlanAllocationSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public Guid RollingPlanId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid ProductionOrderId { get; set; }
    public decimal PlannedQuantityMt { get; set; }
    public decimal ExistingIntermediateInventoryMt { get; set; }
    public decimal FreshSteelQuantityMt { get; set; }
}

public sealed class PlanPackagingUnitSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public Guid PlannedPackagingUnitId { get; set; }
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
