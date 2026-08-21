using APS.Domain;

namespace APS.Application;

/// <summary>
/// Immutable Plan Version evidence for one billet-feed path into rolling. This records the effective
/// rule and scheduled consequence; it is not a second thermal master or a thermodynamic simulation.
/// </summary>
public sealed record BilletThermalDecision(
    Guid RollingPlanId,
    Guid HotRollTaskId,
    Guid? SourceTaskId,
    string RouteCode,
    string GradeCode,
    string CrossSectionCode,
    BilletThermalSourceBasis SourceBasis,
    decimal? SourceTemperatureC,
    DateTime? SourceTemperatureAtUtc,
    decimal? MinimumRollingEntryTemperatureC,
    decimal? PredictedOrActualRollingEntryTemperatureC,
    int? TransferWaitMinutes,
    decimal? TemperatureLossCPerMinute,
    int? MaximumHotHoldMinutes,
    BilletThermalOutcome Outcome,
    string ReasonCode,
    string WaitTransferBasis,
    bool ReheatRequiredByThermalState,
    bool ReheatRequiredByPolicy,
    IReadOnlyCollection<string> RejectedHotPaths,
    IReadOnlyCollection<string> Warnings);
