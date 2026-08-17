namespace APS.Domain;

public sealed class ManufacturingRoute : Entity
{
    public required string RouteCode { get; set; }
    public required string Name { get; set; }
    public string? MaterialFamilyCode { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<ManufacturingRouteOperation> Operations { get; set; } = new List<ManufacturingRouteOperation>();
}

public sealed class ManufacturingRouteOperation : Entity
{
    public Guid ManufacturingRouteId { get; set; }
    public ManufacturingRoute? ManufacturingRoute { get; set; }
    public required string RouteCode { get; set; }
    public int SequenceNumber { get; set; }
    public ProcessOperationType ProcessOperationType { get; set; }
    public WorkOrderType ReleaseWorkOrderType { get; set; }
    public RequirementDisposition Requirement { get; set; } = RequirementDisposition.Required;
    public string? CapabilityClassCode { get; set; }
    public string? InputMaterialSpecificationCode { get; set; }
    public string? OutputMaterialSpecificationCode { get; set; }
    public string? InputCrossSectionCode { get; set; }
    public string? OutputCrossSectionCode { get; set; }
    public bool IsInventoryDecouplingPoint { get; set; }
    public bool RequiresHotMaterial { get; set; }
    public ChargeMode? RequiredChargeMode { get; set; }
    public TimeSpan MinimumQueueTime { get; set; }
    public TimeSpan? MaximumQueueTime { get; set; }
    public decimal YieldPct { get; set; } = 100m;
}

public sealed class RouteResourceCapability : Entity
{
    public Guid ResourceId { get; set; }
    public required string RouteCode { get; set; }
    public ProcessOperationType ProcessOperationType { get; set; }
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
}

public sealed class RouteOperationPlan : Entity
{
    public required string RouteCode { get; set; }
    public Guid UpstreamPlanId { get; set; }
    public ProcessOperationType ProcessOperationType { get; set; }
    public WorkOrderType ReleaseWorkOrderType { get; set; }
    public int SequenceNumber { get; set; }
    public Guid? ResourceId { get; set; }
    public required string GradeCode { get; set; }
    public string? InputMaterialSpecificationCode { get; set; }
    public string? OutputMaterialSpecificationCode { get; set; }
    public required string InputCrossSectionCode { get; set; }
    public required string OutputCrossSectionCode { get; set; }
    public decimal PlannedQuantityMt { get; set; }
    public TimeSpan MinimumQueueTime { get; set; }
    public TimeSpan? MaximumQueueTime { get; set; }
    public bool IsInventoryDecouplingPoint { get; set; }
    public ICollection<RouteOperationPlanAllocation> Allocations { get; set; } = new List<RouteOperationPlanAllocation>();
}

public sealed class RouteOperationPlanAllocation : Entity
{
    public Guid RouteOperationPlanId { get; set; }
    public RouteOperationPlan? RouteOperationPlan { get; set; }
    public Guid CampaignId { get; set; }
    public Guid ProductionOrderId { get; set; }
    public ProductionOrder? ProductionOrder { get; set; }
    public decimal PlannedQuantityMt { get; set; }
}
