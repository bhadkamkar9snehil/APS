namespace APS.Domain;

public enum SequenceRuleScope
{
    Default = 1,
    GradeFamily = 2,
    SequenceClass = 3,
    ExactGrade = 4
}

public enum SegregationPolicy
{
    None = 0,
    SameCustomerOnly = 1,
    SameSalesOrderOnly = 2,
    SameHeatOnly = 3,
    DedicatedCampaign = 4
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
    public SteelProcessOperationType OperationType { get; set; }
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

public sealed class GradeSequenceRule : Entity
{
    public SequenceRuleScope Scope { get; set; }
    public SteelProcessOperationType OperationType { get; set; }
    public string? FromGradeFamilyCode { get; set; }
    public string? ToGradeFamilyCode { get; set; }
    public string? FromSequenceClassCode { get; set; }
    public string? ToSequenceClassCode { get; set; }
    public string? FromGradeCode { get; set; }
    public string? ToGradeCode { get; set; }
    public bool IsAllowed { get; set; } = true;
    public bool RequiresSequenceBreak { get; set; }
    public int Penalty { get; set; }
    public TimeSpan TransitionTime { get; set; }
    public string? ReasonCode { get; set; }
}

public sealed class TemperatureProcessProfile : Entity
{
    public string? GradeCode { get; set; }
    public string? GradeFamilyCode { get; set; }
    public SteelProcessOperationType OperationType { get; set; }
    public decimal? MinimumEntryTemperatureC { get; set; }
    public decimal? TargetEntryTemperatureC { get; set; }
    public decimal? MaximumEntryTemperatureC { get; set; }
    public decimal? MinimumExitTemperatureC { get; set; }
    public decimal? TargetExitTemperatureC { get; set; }
    public decimal? MaximumExitTemperatureC { get; set; }
    public decimal? HeatingRateCPerMinute { get; set; }
    public decimal? NominalLossCPerMinute { get; set; }
}

/// <summary>
/// Normalized SAP/customer requirement snapshot attached to the planning Production Order.
/// It can only narrow grade/plant defaults; it does not silently broaden hard process constraints.
/// </summary>
public sealed class ProductionOrderRequirement : Entity
{
    public Guid ProductionOrderId { get; set; }
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
    public SteelProcessOperationType OperationType { get; set; }
    public RequirementDisposition Requirement { get; set; }
    public string? CapabilityClassCode { get; set; }
    public Guid? RequiredResourceId { get; set; }
    public int? MaximumQueueMinutes { get; set; }
}
