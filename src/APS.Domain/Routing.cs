namespace APS.Domain;

public sealed class ManufacturingRoute : Entity
{
    public required string RouteCode { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<ManufacturingRouteOperation> Operations { get; set; } = new List<ManufacturingRouteOperation>();
}

public sealed class ManufacturingRouteOperation : Entity
{
    public Guid ManufacturingRouteId { get; set; }
    public ManufacturingRoute? ManufacturingRoute { get; set; }
    public required string RouteCode { get; set; }
    public int SequenceNumber { get; set; }
    public WorkOrderType OperationType { get; set; }
    public string? InputCrossSectionCode { get; set; }
    public string? OutputCrossSectionCode { get; set; }
    public bool IsOptional { get; set; }
    public bool IsInventoryDecouplingPoint { get; set; }
    public TimeSpan MinimumQueueTime { get; set; }
    public TimeSpan? MaximumQueueTime { get; set; }
    public decimal YieldPct { get; set; } = 100m;
}

public sealed class RouteOperationPlan : Entity
{
    public required string RouteCode { get; set; }
    public Guid UpstreamPlanId { get; set; }
    public WorkOrderType OperationType { get; set; }
    public int SequenceNumber { get; set; }
    public Guid? ResourceId { get; set; }
    public required string GradeCode { get; set; }
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
