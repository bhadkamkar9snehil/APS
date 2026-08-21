using APS.Domain;

namespace APS.Application;

/// <summary>
/// A future receipt that is no longer a speculative sourcing choice: its upstream operation has been
/// released/committed/running in the baseline plan. It stays pegged to the originating PO so replanning
/// cannot silently steal in-process steel for another order.
/// </summary>
public sealed record CommittedMaterialSupply(
    Guid BaselinePlanVersionId,
    Guid ProductionOrderId,
    Guid? CampaignHeatId,
    string SupplyReference,
    BilletSupplySourceType SourceType,
    string? MaterialSpecificationCode,
    string GradeCode,
    string CrossSectionCode,
    decimal QuantityMt,
    DateTime AvailableFromUtc,
    string? LocationCode = null,
    ChargeMode? ThermalState = null,
    decimal? EstimatedTemperatureC = null,
    BilletThermalSourceBasis? ThermalBasis = null,
    DateTime? TemperatureObservedOnUtc = null);

public sealed record ReplanningActualState(
    IReadOnlyCollection<BaselinePlanOperation> BaselineOperations,
    IReadOnlyCollection<InventoryPosition> Inventory,
    IReadOnlyCollection<string> CompletedPlanningKeys,
    IReadOnlyCollection<string> RunningPlanningKeys,
    IReadOnlyCollection<CommittedMaterialSupply>? CommittedFutureSupplies = null)
{
    public IReadOnlyCollection<CommittedMaterialSupply> EffectiveCommittedFutureSupplies =>
        CommittedFutureSupplies ?? Array.Empty<CommittedMaterialSupply>();
}

public interface IReplanningActualStateProvider
{
    Task<ReplanningActualState> GetAsync(
        Guid baselinePlanVersionId,
        DateTime referenceTimeUtc,
        IReadOnlyCollection<BaselinePlanOperation> baselineOperations,
        CancellationToken cancellationToken = default);
}
