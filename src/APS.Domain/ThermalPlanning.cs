namespace APS.Domain;

/// <summary>
/// Grade/route/resource-specific thermal capability and requirement envelope for one process operation.
/// Null selectors are defaults; more specific matching profiles override broader profiles.
/// Temperatures are absolute degrees Celsius, never superheat deltas.
/// </summary>
public sealed class ProcessThermalProfile : Entity
{
    public required string ProfileCode { get; set; }
    public ProcessOperationType ProcessOperationType { get; set; }
    public Guid? ResourceId { get; set; }
    public string? RouteCode { get; set; }
    public string? GradeCode { get; set; }
    public string? GradeFamilyCode { get; set; }
    public string? CapabilityClassCode { get; set; }

    public decimal? MinimumEntryTemperatureC { get; set; }
    public decimal? TargetEntryTemperatureC { get; set; }
    public decimal? MaximumEntryTemperatureC { get; set; }
    public decimal? MinimumExitTemperatureC { get; set; }
    public decimal? TargetExitTemperatureC { get; set; }
    public decimal? MaximumExitTemperatureC { get; set; }

    public bool CanAddHeat { get; set; }
    public decimal? MaximumTemperatureCorrectionC { get; set; }
    public decimal? HeatingRateCPerMinute { get; set; }
    public int BelowTargetPenaltyPerDegree { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Immutable evidence explaining the thermal timing rule applied to one physical resource pair.
/// </summary>
public sealed record ThermalTransferConstraint(
    Guid PredecessorTaskId,
    Guid SuccessorTaskId,
    Guid FromResourceId,
    Guid ToResourceId,
    ProcessOperationType FromOperation,
    ProcessOperationType ToOperation,
    decimal? MinimumSourceExitTemperatureC,
    decimal? TargetSourceExitTemperatureC,
    decimal? MaximumSourceExitTemperatureC,
    decimal? MinimumArrivalTemperatureC,
    decimal? TargetArrivalTemperatureC,
    decimal? MaximumArrivalTemperatureC,
    decimal TemperatureLossPerMinuteC,
    int MinimumLagMinutes,
    int? PreferredMaximumLagMinutes,
    int? HardMaximumLagMinutes,
    int ExcessLagPenaltyPerMinute,
    string BasisCode);

public sealed record ThermalPlanningResult(
    IReadOnlyCollection<ThermalTransferConstraint> TransferConstraints,
    IReadOnlyCollection<string> Warnings);
