using APS.Domain;

namespace APS.Application;

public sealed record WorkOrderDemandRefView(
    Guid ProductionOrderId,
    string ProductionOrderNumber,
    string? SalesOrderNumber,
    string? SalesOrderItemNumber,
    DemandSourceType DemandSource);

public sealed record WorkOrderOperationView(
    Guid OperationSnapshotId,
    string PlanningKey,
    ProcessOperationType ProcessOperationType,
    string ResourceCode,
    DateTime PlannedStartUtc,
    DateTime PlannedEndUtc,
    decimal QuantityMt,
    string GradeCode,
    string CrossSectionCode);

public sealed record WorkOrderView(
    Guid WorkOrderId,
    string WorkOrderNumber,
    WorkOrderType WorkOrderType,
    WorkOrderStatus Status,
    string? ExternalExecutionId,
    DateTime? FirstPlannedStartUtc,
    DateTime? LastPlannedEndUtc,
    decimal PlannedQuantityMt,
    IReadOnlyCollection<WorkOrderDemandRefView> DemandReferences,
    IReadOnlyCollection<WorkOrderOperationView> Operations);

public sealed record WorkOrdersWorkspaceView(
    PlanContextView Plan,
    int WorkOrderCount,
    int ReleasedCount,
    int RunningCount,
    int HeldCount,
    int CompletedCount,
    IReadOnlyCollection<WorkOrderView> WorkOrders);
