namespace APS.Domain;

public enum ExecutionUpdateSource
{
    Manual = 1,
    MesApi = 2,
    MesSqlReconciliation = 3
}

public enum HeatExecutionStatus
{
    Planned = 1,
    Ready = 2,
    Running = 3,
    Held = 4,
    Completed = 5,
    Cancelled = 6
}

public sealed class WorkOrderStatusHistory : Entity
{
    public Guid WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }
    public WorkOrderStatus PreviousStatus { get; set; }
    public WorkOrderStatus NewStatus { get; set; }
    public DateTime ChangedOnUtc { get; set; }
    public ExecutionUpdateSource Source { get; set; }
    public string? ExternalEventId { get; set; }
    public string? Comment { get; set; }
}

public sealed class HeatExecutionActual : Entity
{
    public Guid PlanVersionId { get; set; }
    public required string PlanningKey { get; set; }
    public string? ExternalHeatNumber { get; set; }
    public string? ExternalCastNumber { get; set; }
    public Guid? CasterResourceId { get; set; }
    public HeatExecutionStatus Status { get; set; } = HeatExecutionStatus.Planned;
    public DateTime? ActualStartUtc { get; set; }
    public DateTime? ActualEndUtc { get; set; }
    public decimal ActualQuantityMt { get; set; }
    public DateTime ChangedOnUtc { get; set; }
    public ExecutionUpdateSource Source { get; set; }
    public string? ExternalEventId { get; set; }
    public string? Comment { get; set; }
}

public sealed class StrandMaterialActual : Entity
{
    public Guid HeatExecutionActualId { get; set; }
    public HeatExecutionActual? HeatExecutionActual { get; set; }
    public int StrandNumber { get; set; }
    public int UnitSequence { get; set; }
    public string? ExternalLotNumber { get; set; }
    public required string MaterialCode { get; set; }
    public required string GradeCode { get; set; }
    public required string CrossSectionCode { get; set; }
    public decimal QuantityMt { get; set; }
    public DateTime ProducedOnUtc { get; set; }
    public string? LocationCode { get; set; }
}
