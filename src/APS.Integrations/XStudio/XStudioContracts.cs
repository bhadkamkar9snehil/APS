using APS.Domain;

namespace APS.Integrations.XStudio;

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
