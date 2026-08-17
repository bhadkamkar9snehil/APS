using APS.Application;
using APS.Domain;

namespace APS.Integrations.XStudio;

public sealed class XStudioIntegrationOptions
{
    public const string SectionName = "Integrations:XStudio";
    public string? BaseUrl { get; init; }
    public string? DataConnectionString { get; init; }
    public bool EnableApiPush { get; init; }
    public bool EnableSqlReconciliation { get; init; } = true;
}

// APS owns this canonical contract. XStudio-specific table/SP/API details are mapped here,
// never inside APS.Planning. Implementations may use configured API calls, controlled
// business stored procedures, or read-only SQL reconciliation depending on deployment.
public interface IXStudioExecutionMapper
{
    ExecutionActual MapExecutionActual(XStudioExecutionRecord record);
}

public sealed record XStudioExecutionRecord(
    string WorkOrderId,
    string Status,
    DateTime? ActualStart,
    DateTime? ActualEnd,
    decimal ProducedQuantityMt,
    string? MaterialCode,
    string? GradeCode,
    string? CrossSectionCode,
    DateTime ChangedOnUtc);

public sealed class XStudioExecutionMapper : IXStudioExecutionMapper
{
    public ExecutionActual MapExecutionActual(XStudioExecutionRecord record) => new(
        record.WorkOrderId,
        ParseStatus(record.Status),
        record.ActualStart,
        record.ActualEnd,
        record.ProducedQuantityMt,
        record.MaterialCode,
        record.GradeCode,
        record.CrossSectionCode,
        record.ChangedOnUtc);

    private static WorkOrderStatus ParseStatus(string status) => status.Trim().ToUpperInvariant() switch
    {
        "RELEASED" => WorkOrderStatus.Released,
        "READY" => WorkOrderStatus.Ready,
        "RUNNING" or "STARTED" => WorkOrderStatus.Running,
        "HELD" or "HOLD" => WorkOrderStatus.Held,
        "COMPLETED" or "COMPLETE" => WorkOrderStatus.Completed,
        "CANCELLED" or "CANCELED" => WorkOrderStatus.Cancelled,
        _ => WorkOrderStatus.Planned
    };
}

public interface IXStudioPlanReleaseMapper
{
    XStudioPlanReleaseEnvelope Map(
        PlanRelease release,
        IReadOnlyCollection<CastSequence> castSequences,
        IReadOnlyCollection<Resource> resources);
}

public sealed record XStudioPlanReleaseEnvelope(
    Guid PlanVersionId,
    DateTime ReleasedOnUtc,
    IReadOnlyCollection<XStudioWorkOrderPlan> WorkOrders,
    IReadOnlyCollection<XStudioCastSequencePlan> CastSequences);

public sealed record XStudioWorkOrderPlan(
    string ApsWorkOrderNumber,
    string WorkOrderType,
    string MaterialCode,
    string GradeCode,
    string CrossSectionCode,
    decimal PlannedQuantityMt,
    DateTime? PlannedStart,
    DateTime? PlannedEnd,
    string? ResourceCode,
    IReadOnlyCollection<XStudioWorkOrderAllocation> Allocations);

public sealed record XStudioWorkOrderAllocation(
    string ProductionOrderNumber,
    string? SalesOrderNumber,
    string? SalesOrderItem,
    DemandSourceType DemandSource,
    decimal PlannedQuantityMt);

public sealed record XStudioCastSequencePlan(
    Guid ApsCastSequenceId,
    string CasterResourceCode,
    int SequenceNumber,
    string CasterSectionCode,
    string RouteCode,
    DateTime? PlannedStart,
    DateTime? PlannedEnd,
    IReadOnlyCollection<XStudioCastHeatPlan> Heats);

public sealed record XStudioCastHeatPlan(
    Guid ApsCampaignHeatId,
    int Position,
    string GradeCode,
    decimal PlannedQuantityMt,
    Guid CampaignId);
