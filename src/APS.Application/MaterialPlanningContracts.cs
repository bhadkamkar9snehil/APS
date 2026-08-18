using APS.Domain;

namespace APS.Application;

public enum ScheduledMaterialEventTiming
{
    FixedTime = 1,
    TaskStart = 2,
    TaskEnd = 3
}

/// <summary>
/// Solver-level material event. QuantityDeltaKg is positive for receipts and negative for consumption.
/// Qualification metadata is retained so the same event stream can become the auditable Plan Version ledger.
/// </summary>
public sealed record ScheduledMaterialEvent(
    string MaterialPoolKey,
    long QuantityDeltaKg,
    ScheduledMaterialEventTiming Timing,
    Guid? TaskId = null,
    DateTime? FixedTimeUtc = null,
    string? Explanation = null,
    Guid? ProductionOrderId = null,
    string? MaterialCode = null,
    string? MaterialSpecificationCode = null,
    string? GradeCode = null,
    string? CrossSectionCode = null,
    string? LocationCode = null,
    string? SupplyReference = null,
    Guid? CampaignHeatId = null,
    MaterialBalanceEventType? LedgerEventType = null);

public sealed record MaterialPlanningResult(
    IReadOnlyCollection<MaterialSupplyReservation> Reservations,
    IReadOnlyCollection<ScheduledMaterialEvent> ScheduleEvents,
    IReadOnlyCollection<MaterialBalanceEvent> LedgerEvents,
    IReadOnlyCollection<PlanningIssue> Issues,
    IReadOnlyCollection<MaterialRequirement>? Requirements = null,
    IReadOnlyCollection<MaterialSupplyRequirement>? SupplyRequirements = null);
