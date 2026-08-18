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
/// Using kilograms keeps CP-SAT reservoir arithmetic integral while retaining tonne-level domain values elsewhere.
/// </summary>
public sealed record ScheduledMaterialEvent(
    string MaterialPoolKey,
    long QuantityDeltaKg,
    ScheduledMaterialEventTiming Timing,
    Guid? TaskId = null,
    DateTime? FixedTimeUtc = null,
    string? Explanation = null);

public sealed record MaterialPlanningResult(
    IReadOnlyCollection<MaterialSupplyReservation> Reservations,
    IReadOnlyCollection<ScheduledMaterialEvent> ScheduleEvents,
    IReadOnlyCollection<MaterialBalanceEvent> LedgerEvents,
    IReadOnlyCollection<PlanningIssue> Issues,
    IReadOnlyCollection<MaterialRequirement>? Requirements = null,
    IReadOnlyCollection<MaterialSupplyRequirement>? SupplyRequirements = null);
