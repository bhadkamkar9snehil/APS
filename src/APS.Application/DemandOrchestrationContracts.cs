using APS.Domain;
using FluentValidation;

namespace APS.Application;

public sealed record SalesOrderDemandInput(
    string SalesOrderNumber,
    string ItemNumber,
    string MaterialCode,
    string GradeCode,
    string FinalCrossSectionCode,
    decimal OrderQuantityMt,
    decimal OpenQuantityMt,
    DateTime CustomerRequiredDate,
    DateTime? ConfirmedDeliveryDate = null,
    string? CustomerCode = null,
    string? CustomerGroupCode = null,
    string? ExternalStatus = null,
    int Priority = 0);

public sealed record SalesOrderReconciliationResult(
    int Created,
    int Updated,
    int Unchanged,
    int CancelledOrClosed,
    IReadOnlyCollection<Guid> SalesOrderIds);

public sealed record DemandServiceDatePolicy(
    int QualityLeadMinutes = 0,
    int PackingLeadMinutes = 0,
    int DispatchLeadMinutes = 0)
{
    public int TotalLeadMinutes =>
        Math.Max(0, QualityLeadMinutes) + Math.Max(0, PackingLeadMinutes) + Math.Max(0, DispatchLeadMinutes);

    public DateTime ProductionRequiredBy(DateTime customerOrConfirmedDate) =>
        customerOrConfirmedDate.AddMinutes(-TotalLeadMinutes);
}

public sealed record PlanningDemandSelection(
    IReadOnlyCollection<Guid>? SalesOrderIds = null,
    DateTime? RequiredThroughUtc = null,
    bool IncludeMakeToStock = true,
    DemandServiceDatePolicy? ServiceDatePolicy = null);

public sealed record DemandCoverageEvidence(
    string MaterialCode,
    string GradeCode,
    string CrossSectionCode,
    string? LocationCode,
    DateTime? AvailableFromUtc,
    MaterialQualityStatus QualityStatus,
    decimal QuantityMt);

public sealed record DemandOrchestrationItem(
    Guid SalesOrderId,
    string SalesOrderNumber,
    string SalesOrderItemNumber,
    string MaterialCode,
    string GradeCode,
    string FinalCrossSectionCode,
    string? CustomerCode,
    string? CustomerGroupCode,
    Guid? ProductionOrderId,
    string? ProductionOrderNumber,
    decimal OpenDemandQuantityMt,
    decimal FinishedGoodsCoveredQuantityMt,
    decimal ManufacturingRequirementQuantityMt,
    DateTime CustomerRequiredDate,
    DateTime? ConfirmedDeliveryDate,
    DateTime ProductionRequiredByDate,
    int Priority,
    DemandReconciliationDisposition Disposition,
    bool PlannerAttentionRequired,
    string? ReasonCode,
    IReadOnlyCollection<DemandCoverageEvidence> FinishedGoodsCoverage);

public sealed record DemandOrchestrationResult(
    IReadOnlyCollection<ProductionOrder> ProductionOrders,
    IReadOnlyCollection<DemandOrchestrationItem> MakeToOrderDemand,
    IReadOnlyCollection<ProductionOrder> MakeToStockProductionOrders,
    IReadOnlyCollection<PlanningIssue> Issues);

public interface IProductionDemandOrchestrationService
{
    Task<SalesOrderReconciliationResult> ReconcileSalesOrdersAsync(
        IReadOnlyCollection<SalesOrderDemandInput> salesOrders,
        CancellationToken cancellationToken = default);

    Task<DemandOrchestrationResult> PrepareAsync(
        PlanningDemandSelection selection,
        IReadOnlyCollection<InventoryPosition> inventory,
        PlanningMasterDataSnapshot masters,
        DateTime referenceTimeUtc,
        DateTime horizonEndUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DemandOrchestrationItem>> GetCurrentMtoDemandAsync(
        CancellationToken cancellationToken = default);
}

public sealed class SalesOrderDemandInputValidator : AbstractValidator<SalesOrderDemandInput>
{
    public SalesOrderDemandInputValidator()
    {
        RuleFor(x => x.SalesOrderNumber).NotEmpty();
        RuleFor(x => x.ItemNumber).NotEmpty();
        RuleFor(x => x.MaterialCode).NotEmpty();
        RuleFor(x => x.GradeCode).NotEmpty();
        RuleFor(x => x.FinalCrossSectionCode).NotEmpty();
        RuleFor(x => x.OrderQuantityMt).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.OpenQuantityMt).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.OpenQuantityMt).LessThanOrEqualTo(x => x.OrderQuantityMt)
            .When(x => x.OrderQuantityMt > 0m);
        RuleFor(x => x.CustomerRequiredDate).NotEmpty();
        RuleFor(x => x.Priority).GreaterThanOrEqualTo(0);
    }
}

public sealed class PlanningDemandSelectionValidator : AbstractValidator<PlanningDemandSelection>
{
    public PlanningDemandSelectionValidator()
    {
        RuleForEach(x => x.SalesOrderIds).NotEmpty()
            .When(x => x.SalesOrderIds is { Count: > 0 });
        RuleFor(x => x.ServiceDatePolicy!.QualityLeadMinutes).GreaterThanOrEqualTo(0)
            .When(x => x.ServiceDatePolicy is not null);
        RuleFor(x => x.ServiceDatePolicy!.PackingLeadMinutes).GreaterThanOrEqualTo(0)
            .When(x => x.ServiceDatePolicy is not null);
        RuleFor(x => x.ServiceDatePolicy!.DispatchLeadMinutes).GreaterThanOrEqualTo(0)
            .When(x => x.ServiceDatePolicy is not null);
    }
}
