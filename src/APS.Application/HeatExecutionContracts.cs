using APS.Domain;

namespace APS.Application;

public sealed record StrandMaterialActualInput(
    int StrandNumber,
    int UnitSequence,
    string? ExternalLotNumber,
    string MaterialCode,
    string GradeCode,
    string CrossSectionCode,
    decimal QuantityMt,
    DateTime ProducedOnUtc,
    string? LocationCode = null);

public sealed record HeatExecutionUpdate(
    Guid PlanVersionId,
    string PlanningKey,
    HeatExecutionStatus Status,
    DateTime ChangedOnUtc,
    ExecutionUpdateSource Source,
    string? ExternalEventId = null,
    string? ExternalHeatNumber = null,
    string? ExternalCastNumber = null,
    Guid? CasterResourceId = null,
    DateTime? ActualStartUtc = null,
    DateTime? ActualEndUtc = null,
    decimal? ActualQuantityMt = null,
    IReadOnlyCollection<StrandMaterialActualInput>? MaterialOutputs = null,
    string? Comment = null,
    bool IsCorrection = false);

public sealed record HeatExecutionSnapshot(
    Guid HeatExecutionActualId,
    Guid PlanVersionId,
    string PlanningKey,
    HeatExecutionStatus Status,
    string? ExternalHeatNumber,
    string? ExternalCastNumber,
    Guid? CasterResourceId,
    DateTime? ActualStartUtc,
    DateTime? ActualEndUtc,
    decimal ActualQuantityMt,
    DateTime ChangedOnUtc,
    IReadOnlyCollection<StrandMaterialActualInput> MaterialOutputs);

public interface IHeatExecutionService
{
    Task<HeatExecutionSnapshot> ApplyAsync(
        HeatExecutionUpdate update,
        CancellationToken cancellationToken = default);
}
