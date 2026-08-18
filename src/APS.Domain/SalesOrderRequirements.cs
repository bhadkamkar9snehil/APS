namespace APS.Domain;

/// <summary>
/// Normalized SAP/customer manufacturing requirements attached to the Sales Order item before an MTO PO exists.
/// The derived ProductionOrderRequirement is copied from this profile when manufacturing is required.
/// </summary>
public sealed class SalesOrderRequirementProfile : Entity
{
    public Guid SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }
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

    /// <summary>
    /// Stable qualification fingerprint used only when customer/order-specific FG compatibility must be proven.
    /// It deliberately excludes PO identity so equivalent demand/stock specifications can match.
    /// </summary>
    public string? QualificationFingerprint { get; set; }

    public ICollection<SalesOrderChemistryRequirement> ChemistryOverrides { get; set; } =
        new List<SalesOrderChemistryRequirement>();
    public ICollection<SalesOrderProcessRequirement> ProcessOverrides { get; set; } =
        new List<SalesOrderProcessRequirement>();
}

public sealed class SalesOrderChemistryRequirement : Entity
{
    public Guid SalesOrderRequirementProfileId { get; set; }
    public SalesOrderRequirementProfile? SalesOrderRequirementProfile { get; set; }
    public required string ElementCode { get; set; }
    public decimal? MinimumPct { get; set; }
    public decimal? TargetPct { get; set; }
    public decimal? MaximumPct { get; set; }
}

public sealed class SalesOrderProcessRequirement : Entity
{
    public Guid SalesOrderRequirementProfileId { get; set; }
    public SalesOrderRequirementProfile? SalesOrderRequirementProfile { get; set; }
    public ProcessOperationType ProcessOperationType { get; set; }
    public RequirementDisposition Requirement { get; set; }
    public string? CapabilityClassCode { get; set; }
    public Guid? RequiredResourceId { get; set; }
    public int? MaximumQueueMinutes { get; set; }
}
