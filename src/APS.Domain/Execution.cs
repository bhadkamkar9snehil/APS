namespace APS.Domain;

public enum ExecutionUpdateSource
{
    Manual = 1,
    MesApi = 2,
    MesSqlReconciliation = 3
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
