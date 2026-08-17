namespace APS.Domain;

/// <summary>
/// Grade-specific temperature envelope at one steel process operation. Values are authoritative
/// planning limits supplied by metallurgy/master data; APS does not infer liquidus chemistry itself.
/// </summary>
public sealed class GradeProcessTemperatureRequirement : Entity
{
    public Guid SteelGradeId { get; set; }
    public SteelGrade? SteelGrade { get; set; }
    public ProcessOperationType ProcessOperationType { get; set; }
    public decimal? MinimumEntryTemperatureC { get; set; }
    public decimal? TargetEntryTemperatureC { get; set; }
    public decimal? MaximumEntryTemperatureC { get; set; }
    public decimal? MinimumExitTemperatureC { get; set; }
    public decimal? TargetExitTemperatureC { get; set; }
    public decimal? MaximumExitTemperatureC { get; set; }
    public int? MaximumHoldingMinutesAfterExit { get; set; }
}

/// <summary>
/// Thermal ability of one physical Resource for one process. Used to prove that a selected
/// resource/transfer pair can deliver the next operation inside its required temperature window.
/// </summary>
public sealed class ResourceTemperatureCapability : Entity
{
    public Guid ResourceId { get; set; }
    public ProcessOperationType ProcessOperationType { get; set; }
    public decimal? MinimumAchievableExitTemperatureC { get; set; }
    public decimal? NominalExitTemperatureC { get; set; }
    public decimal? MaximumAchievableExitTemperatureC { get; set; }
    public decimal? MaximumHeatingRateCPerMinute { get; set; }
    public decimal? NominalTemperatureLossCPerMinuteWhileHolding { get; set; }
    public bool CanCorrectTemperature { get; set; }
}
