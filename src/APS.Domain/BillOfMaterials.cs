namespace APS.Domain;

public enum BomStatus
{
    Draft = 1,
    Active = 2,
    Inactive = 3,
    Obsolete = 4
}

public enum BomFlowType
{
    Input = 1,
    Byproduct = 2,
    CoProduct = 3,
    Waste = 4
}

/// <summary>
/// Versioned/effective manufacturing BOM header. Levels are deliberately not stored: depth is derived recursively.
/// Selector fields are optional restrictions; a populated selector must match the demand context.
/// Higher SelectionPriority wins before selector specificity and version/effective-date tie-breakers.
/// </summary>
public sealed class BillOfMaterial : Entity
{
    public required string BomCode { get; set; }
    public int VersionNumber { get; set; } = 1;
    public BomStatus Status { get; set; } = BomStatus.Active;
    public DateTime EffectiveFromUtc { get; set; } = DateTime.MinValue;
    public DateTime? EffectiveToUtc { get; set; }

    public string? OutputMaterialSpecificationCode { get; set; }
    public required string OutputMaterialCode { get; set; }
    public decimal OutputQuantity { get; set; } = 1m;
    public string OutputUom { get; set; } = "MT";

    public Guid? PlantId { get; set; }
    public string? RouteCode { get; set; }
    public string? GradeCode { get; set; }
    public string? GradeFamilyCode { get; set; }
    public string? ProductFamilyCode { get; set; }
    public int SelectionPriority { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<BillOfMaterialComponent> Components { get; set; } = new List<BillOfMaterialComponent>();
}

/// <summary>
/// One BOM flow relative to the header output. Input flows are recursively required.
/// Byproduct/co-product/waste flows are projected as auditable outputs and are not recursively consumed by this BOM.
/// YieldPct takes precedence over ScrapPct/LossPct when populated.
/// </summary>
public sealed class BillOfMaterialComponent : Entity
{
    public Guid BillOfMaterialId { get; set; }
    public BillOfMaterial? BillOfMaterial { get; set; }
    public int SequenceNumber { get; set; }

    public string? ComponentMaterialSpecificationCode { get; set; }
    public required string ComponentMaterialCode { get; set; }
    public string? ComponentGradeCode { get; set; }
    public string? ComponentCrossSectionCode { get; set; }
    public BomFlowType FlowType { get; set; } = BomFlowType.Input;
    public decimal QuantityPerOutput { get; set; }
    public string Uom { get; set; } = "MT";

    public decimal? YieldPct { get; set; }
    public decimal? ScrapPct { get; set; }
    public decimal? LossPct { get; set; }
    public int RequiredAtOffsetMinutes { get; set; }

    public string? LocationCode { get; set; }
    public string? QualityClassCode { get; set; }
}
