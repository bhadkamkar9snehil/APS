using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class HeatExecutionService(ApsDbContext db) : IHeatExecutionService
{
    public async Task<HeatExecutionSnapshot> ApplyAsync(
        HeatExecutionUpdate update,
        CancellationToken cancellationToken = default)
    {
        Validate(update);

        if (!string.IsNullOrWhiteSpace(update.ExternalEventId))
        {
            var duplicate = await db.HeatExecutionActuals
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Source == update.Source && x.ExternalEventId == update.ExternalEventId,
                    cancellationToken);
            if (duplicate is not null) return await SnapshotAsync(duplicate, cancellationToken);
        }

        var planned = await db.PlanOperationSnapshots
            .SingleOrDefaultAsync(x =>
                x.PlanVersionId == update.PlanVersionId &&
                x.PlanningKey == update.PlanningKey &&
                x.OperationType == PlanOperationType.Casting,
                cancellationToken)
            ?? throw new KeyNotFoundException("No casting operation matches the supplied plan version and planning key.");

        var previous = await db.HeatExecutionActuals
            .AsNoTracking()
            .Where(x => x.PlanVersionId == update.PlanVersionId && x.PlanningKey == update.PlanningKey)
            .OrderByDescending(x => x.ChangedOnUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var outputs = update.MaterialOutputs ?? Array.Empty<StrandMaterialActualInput>();
        var outputQuantity = outputs.Sum(x => x.QuantityMt);
        var actualQuantity = update.ActualQuantityMt ?? (outputs.Count > 0 ? outputQuantity : previous?.ActualQuantityMt ?? 0m);
        var actualResource = update.CasterResourceId ?? previous?.CasterResourceId ?? planned.ActualResourceId ?? planned.CommittedResourceId ?? planned.ResourceId;

        var actual = new HeatExecutionActual
        {
            PlanVersionId = update.PlanVersionId,
            PlanningKey = update.PlanningKey,
            ExternalHeatNumber = update.ExternalHeatNumber ?? previous?.ExternalHeatNumber,
            ExternalCastNumber = update.ExternalCastNumber ?? previous?.ExternalCastNumber,
            CasterResourceId = actualResource,
            Status = update.Status,
            ActualStartUtc = update.ActualStartUtc ?? previous?.ActualStartUtc ??
                             (update.Status == HeatExecutionStatus.Running ? update.ChangedOnUtc : null),
            ActualEndUtc = update.ActualEndUtc ??
                           (update.Status == HeatExecutionStatus.Completed ? update.ChangedOnUtc : previous?.ActualEndUtc),
            ActualQuantityMt = actualQuantity,
            ChangedOnUtc = update.ChangedOnUtc,
            Source = update.Source,
            ExternalEventId = update.ExternalEventId,
            Comment = update.Comment
        };

        if (actual.ActualStartUtc.HasValue && actual.ActualEndUtc.HasValue && actual.ActualEndUtc < actual.ActualStartUtc)
            throw new InvalidOperationException("Heat actual end cannot be before actual start.");

        // Casting-specific material evidence is specialized, but operation status/resource/commitment
        // semantics are not. Reuse the canonical operation execution path so heat events cannot drift
        // from generic operation history, off-plan-resource handling, or downstream commitment rules.
        await new OperationExecutionService(db).ApplyCoreAsync(
            new OperationExecutionUpdate(
                update.PlanVersionId,
                update.PlanningKey,
                ToOperationStatus(update.Status),
                update.ChangedOnUtc,
                update.Source,
                actualResource,
                actual.ActualStartUtc,
                actual.ActualEndUtc,
                actual.ActualQuantityMt,
                update.ExternalEventId,
                update.Comment,
                update.IsCorrection),
            saveChanges: false,
            cancellationToken);

        db.HeatExecutionActuals.Add(actual);
        foreach (var output in outputs)
            await StageMaterialOutputAsync(actual, output, update.PlanningKey, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Snapshot(actual, outputs);
    }

    private async Task StageMaterialOutputAsync(
        HeatExecutionActual actual,
        StrandMaterialActualInput output,
        string planningKey,
        CancellationToken cancellationToken)
    {
        db.StrandMaterialActuals.Add(new StrandMaterialActual
        {
            HeatExecutionActualId = actual.Id,
            HeatExecutionActual = actual,
            StrandNumber = output.StrandNumber,
            UnitSequence = output.UnitSequence,
            ExternalLotNumber = output.ExternalLotNumber,
            MaterialCode = output.MaterialCode,
            GradeCode = output.GradeCode,
            CrossSectionCode = output.CrossSectionCode,
            QuantityMt = output.QuantityMt,
            ProducedOnUtc = output.ProducedOnUtc,
            LocationCode = output.LocationCode,
            ThermalState = output.ThermalState,
            MeasuredTemperatureC = output.MeasuredTemperatureC,
            TemperatureObservedOnUtc = output.TemperatureObservedOnUtc
        });

        var lotNumber = output.ExternalLotNumber ?? $"{planningKey}:S{output.StrandNumber:00}:U{output.UnitSequence:000}";
        if (await db.MaterialLots.AsNoTracking().AnyAsync(x => x.LotNumber == lotNumber, cancellationToken)) return;

        db.MaterialLots.Add(new MaterialLot
        {
            LotNumber = lotNumber,
            MaterialCode = output.MaterialCode,
            GradeCode = output.GradeCode,
            CrossSectionCode = output.CrossSectionCode,
            Stage = InventoryStage.CastIntermediate,
            QuantityMt = output.QuantityMt,
            Status = MaterialLotStatus.Available,
            LocationCode = output.LocationCode,
            HeatNumber = actual.ExternalHeatNumber,
            CastNumber = actual.ExternalCastNumber,
            StrandNumber = output.StrandNumber,
            ProducedOnUtc = output.ProducedOnUtc,
            ThermalState = output.ThermalState,
            EstimatedTemperatureC = output.MeasuredTemperatureC,
            TemperatureObservedOnUtc = output.TemperatureObservedOnUtc
        });
    }

    private async Task<HeatExecutionSnapshot> SnapshotAsync(HeatExecutionActual actual, CancellationToken cancellationToken)
    {
        var outputs = await db.StrandMaterialActuals
            .AsNoTracking()
            .Where(x => x.HeatExecutionActualId == actual.Id)
            .OrderBy(x => x.StrandNumber)
            .ThenBy(x => x.UnitSequence)
            .Select(x => new StrandMaterialActualInput(
                x.StrandNumber,
                x.UnitSequence,
                x.ExternalLotNumber,
                x.MaterialCode,
                x.GradeCode,
                x.CrossSectionCode,
                x.QuantityMt,
                x.ProducedOnUtc,
                x.LocationCode,
                x.ThermalState,
                x.MeasuredTemperatureC,
                x.TemperatureObservedOnUtc))
            .ToListAsync(cancellationToken);
        return Snapshot(actual, outputs);
    }

    private static HeatExecutionSnapshot Snapshot(HeatExecutionActual actual, IReadOnlyCollection<StrandMaterialActualInput> outputs) => new(
        actual.Id,
        actual.PlanVersionId,
        actual.PlanningKey,
        actual.Status,
        actual.ExternalHeatNumber,
        actual.ExternalCastNumber,
        actual.CasterResourceId,
        actual.ActualStartUtc,
        actual.ActualEndUtc,
        actual.ActualQuantityMt,
        actual.ChangedOnUtc,
        outputs);

    private static void Validate(HeatExecutionUpdate update)
    {
        if (string.IsNullOrWhiteSpace(update.PlanningKey))
            throw new ArgumentException("PlanningKey is required.", nameof(update));
        if (update.Source != ExecutionUpdateSource.Manual && string.IsNullOrWhiteSpace(update.ExternalEventId))
            throw new ArgumentException("ExternalEventId is required for MES heat execution updates.", nameof(update));
        if (update.ActualQuantityMt < 0m)
            throw new ArgumentOutOfRangeException(nameof(update.ActualQuantityMt));
        if (update.ActualStartUtc.HasValue && update.ActualEndUtc.HasValue && update.ActualEndUtc < update.ActualStartUtc)
            throw new ArgumentException("ActualEndUtc cannot be before ActualStartUtc.", nameof(update));

        foreach (var output in update.MaterialOutputs ?? Array.Empty<StrandMaterialActualInput>())
        {
            if (output.StrandNumber <= 0) throw new ArgumentOutOfRangeException(nameof(output.StrandNumber));
            if (output.UnitSequence <= 0) throw new ArgumentOutOfRangeException(nameof(output.UnitSequence));
            if (string.IsNullOrWhiteSpace(output.MaterialCode)) throw new ArgumentException("MaterialCode is required for strand output.");
            if (output.QuantityMt < 0m) throw new ArgumentOutOfRangeException(nameof(output.QuantityMt));
        }
    }

    private static OperationExecutionStatus ToOperationStatus(HeatExecutionStatus status) => status switch
    {
        HeatExecutionStatus.Planned => OperationExecutionStatus.Planned,
        HeatExecutionStatus.Ready => OperationExecutionStatus.Ready,
        HeatExecutionStatus.Running => OperationExecutionStatus.Running,
        HeatExecutionStatus.Held => OperationExecutionStatus.Held,
        HeatExecutionStatus.Completed => OperationExecutionStatus.Completed,
        HeatExecutionStatus.Cancelled => OperationExecutionStatus.Cancelled,
        _ => OperationExecutionStatus.Planned
    };
}
