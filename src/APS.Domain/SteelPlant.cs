namespace APS.Domain;

public enum SteelProcessUnitType
{
    Unknown = 0,
    Eaf = 1,
    Lrf = 2,
    Vd = 3,
    Ccm = 4,
    ReheatingFurnace = 5,
    HotRollingMill = 6,
    ColdRollingMill = 7,
    TmtWaterBox = 8,
    CoolingBed = 9,
    Shear = 10,
    BundlingLine = 11,
    Coiler = 12,
    FinishingLine = 13,
    MaterialBuffer = 14
}

public enum SteelProcessOperationType
{
    Eaf = 1,
    Lrf = 2,
    Vd = 3,
    Ccm = 4,
    Reheat = 5,
    HotRoll = 6,
    ColdRoll = 7,
    Tmt = 8,
    Cool = 9,
    Cut = 10,
    Bundle = 11,
    Coil = 12,
    Finish = 13
}

public enum RequirementDisposition
{
    Forbidden = 0,
    Optional = 1,
    Required = 2
}

public enum ResourceOperatingState
{
    Available = 1,
    PlannedMaintenance = 2,
    Breakdown = 3,
    CapacityDerated = 4,
    QualityRestricted = 5,
    Disabled = 6
}

public enum ChargeMode
{
    HotDirect = 1,
    HotBuffered = 2,
    ColdCharge = 3
}

public sealed class PlantArea : Entity
{
    public Guid PlantId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int SequenceNumber { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Steel-specific physical-equipment semantics layered over the existing Resource record.
/// One profile exists per independently scheduled physical Resource.
/// </summary>
public sealed class SteelResourceProfile : Entity
{
    public Guid ResourceId { get; set; }
    public Guid? PlantAreaId { get; set; }
    public SteelProcessUnitType ProcessUnitType { get; set; }
    public ResourceOperatingState OperatingState { get; set; } = ResourceOperatingState.Available;
    public decimal CapacityFactorPct { get; set; } = 100m;

    // Heat/tap capability; populated for EAF/LRF/VD where relevant.
    public decimal? MinimumHeatWeightMt { get; set; }
    public decimal? NominalHeatWeightMt { get; set; }
    public decimal? MaximumHeatWeightMt { get; set; }
    public decimal? LadleCapacityMt { get; set; }

    // Generic rate/capacity attributes.
    public decimal? NominalThroughputMtPerHour { get; set; }
    public decimal? WorkingCapacityMt { get; set; }
    public int? MinimumResidenceMinutes { get; set; }
    public int? NominalResidenceMinutes { get; set; }
    public int? MaximumResidenceMinutes { get; set; }

    // Caster-specific attributes.
    public int? StrandCount { get; set; }
    public int? MaximumHeatsPerSequence { get; set; }
    public int? MaximumHeatsPerTundish { get; set; }
    public decimal? MinimumCastingSpeedMPerMin { get; set; }
    public decimal? NominalCastingSpeedMPerMin { get; set; }
    public decimal? MaximumCastingSpeedMPerMin { get; set; }
    public decimal? ExpectedYieldPct { get; set; }

    // Reheating/hot-charge semantics.
    public bool SupportsHotCharge { get; set; }
    public bool SupportsColdCharge { get; set; }
    public decimal? TargetDischargeTemperatureC { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class SteelResourceCapability : Entity
{
    public Guid ResourceId { get; set; }
    public SteelProcessOperationType OperationType { get; set; }
    public string? CapabilityClassCode { get; set; }
    public string? GradeCode { get; set; }
    public string? GradeFamilyCode { get; set; }
    public string? CastingClassCode { get; set; }
    public string? MaterialSpecificationCode { get; set; }
    public string? InputCrossSectionCode { get; set; }
    public string? OutputCrossSectionCode { get; set; }
    public string? ProductFamilyCode { get; set; }
    public decimal? MinimumQuantityMt { get; set; }
    public decimal? MaximumQuantityMt { get; set; }
    public decimal? ThroughputMtPerHour { get; set; }
    public int? FixedDurationMinutes { get; set; }
    public int AssignmentPenalty { get; set; }
    public bool IsPreferred { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class SteelFlowLink : Entity
{
    public Guid FromResourceId { get; set; }
    public Guid ToResourceId { get; set; }
    public SteelProcessOperationType FromOperationType { get; set; }
    public SteelProcessOperationType ToOperationType { get; set; }
    public FlowCouplingType CouplingType { get; set; }
    public TimeSpan MinimumTransferTime { get; set; }
    public TimeSpan? MaximumTransferTime { get; set; }
    public bool SupportsHotTransfer { get; set; }
    public bool IsInventoryDecouplingPoint { get; set; }
    public decimal? NominalTemperatureLossCPerMinute { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class SteelRouteTemplate : Entity
{
    public required string RouteCode { get; set; }
    public required string Name { get; set; }
    public string? MaterialFamilyCode { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<SteelRouteStep> Steps { get; set; } = new List<SteelRouteStep>();
}

public sealed class SteelRouteStep : Entity
{
    public Guid SteelRouteTemplateId { get; set; }
    public SteelRouteTemplate? SteelRouteTemplate { get; set; }
    public int SequenceNumber { get; set; }
    public SteelProcessOperationType OperationType { get; set; }
    public RequirementDisposition Requirement { get; set; } = RequirementDisposition.Required;
    public string? CapabilityClassCode { get; set; }
    public string? InputMaterialSpecificationCode { get; set; }
    public string? OutputMaterialSpecificationCode { get; set; }
    public TimeSpan MinimumQueueTime { get; set; }
    public TimeSpan? MaximumQueueTime { get; set; }
    public bool IsInventoryDecouplingPoint { get; set; }
    public decimal YieldPct { get; set; } = 100m;
}

public sealed class PlanningScenario : Entity
{
    public required string ScenarioCode { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public bool IsBaseline { get; set; }
    public ICollection<ResourceScenarioOverride> ResourceOverrides { get; set; } = new List<ResourceScenarioOverride>();
}

public sealed class ResourceScenarioOverride : Entity
{
    public Guid PlanningScenarioId { get; set; }
    public Guid ResourceId { get; set; }
    public ResourceOperatingState OperatingState { get; set; }
    public decimal? CapacityFactorPct { get; set; }
    public DateTime? EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public string? Reason { get; set; }
}
