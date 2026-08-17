namespace APS.Domain;

public sealed class PlanOrderRequirementSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public Guid ProductionOrderId { get; set; }
    public string? SalesOrderNumber { get; set; }
    public string? SalesOrderItem { get; set; }
    public string? CustomerCode { get; set; }
    public string? CustomerGroupCode { get; set; }
    public required string MaterialCode { get; set; }
    public required string GradeCode { get; set; }
    public string? GradeFamilyCode { get; set; }
    public string? GradeSequenceClassCode { get; set; }
    public string? CastingClassCode { get; set; }
    public string? QualityClassCode { get; set; }
    public required string RouteCode { get; set; }
    public required string CasterSectionCode { get; set; }
    public required string FinalCrossSectionCode { get; set; }
    public SegregationPolicy SegregationPolicy { get; set; }
    public RequirementDisposition VdRequirement { get; set; }
    public RequirementDisposition ReheatRequirement { get; set; }
    public RequirementDisposition TmtRequirement { get; set; }
    public bool HotChargeAllowed { get; set; }
    public Guid? RequiredResourceId { get; set; }
    public decimal? MinimumSuperheatC { get; set; }
    public decimal? TargetSuperheatC { get; set; }
    public decimal? MaximumSuperheatC { get; set; }
    public decimal? MinimumCastingTemperatureC { get; set; }
    public decimal? TargetCastingTemperatureC { get; set; }
    public decimal? MaximumCastingTemperatureC { get; set; }
    public decimal? CutLengthM { get; set; }
    public decimal? MinimumBundleWeightMt { get; set; }
    public decimal? TargetBundleWeightMt { get; set; }
    public decimal? MaximumBundleWeightMt { get; set; }
    public decimal? MinimumCoilWeightMt { get; set; }
    public decimal? TargetCoilWeightMt { get; set; }
    public decimal? MaximumCoilWeightMt { get; set; }
    public bool? AllowMixedHeatBundle { get; set; }
    public string? MarkingRequirementCode { get; set; }
    public string? InspectionRequirementCode { get; set; }
    public string? RequirementReference { get; set; }
    public string? RequirementFingerprint { get; set; }
    public ICollection<PlanChemistryRequirementSnapshot> Chemistry { get; set; } = new List<PlanChemistryRequirementSnapshot>();
    public ICollection<PlanProcessRequirementSnapshot> ProcessRequirements { get; set; } = new List<PlanProcessRequirementSnapshot>();
}

public sealed class PlanChemistryRequirementSnapshot : Entity
{
    public Guid PlanOrderRequirementSnapshotId { get; set; }
    public required string ElementCode { get; set; }
    public decimal? MinimumPct { get; set; }
    public decimal? TargetPct { get; set; }
    public decimal? MaximumPct { get; set; }
}

public sealed class PlanProcessRequirementSnapshot : Entity
{
    public Guid PlanOrderRequirementSnapshotId { get; set; }
    public ProcessOperationType ProcessOperationType { get; set; }
    public RequirementDisposition Requirement { get; set; }
    public string? CapabilityClassCode { get; set; }
    public Guid? RequiredResourceId { get; set; }
    public int? MinimumProcessMinutes { get; set; }
    public int? MaximumProcessMinutes { get; set; }
    public int? MaximumQueueMinutes { get; set; }
    public decimal? MinimumHeatWeightMt { get; set; }
    public decimal? TargetHeatWeightMt { get; set; }
    public decimal? MaximumHeatWeightMt { get; set; }
    public decimal? ExpectedYieldPct { get; set; }
}
