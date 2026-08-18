namespace APS.Domain;

public enum DemandReconciliationDisposition
{
    Unchanged = 1,
    ProductionOrderCreated = 2,
    ProductionOrderUpdated = 3,
    FullyCoveredByFinishedGoods = 4,
    ProductionOrderCancelled = 5,
    CommittedProductionOrderProtected = 6,
    PlannerAttentionRequired = 7
}

/// <summary>
/// Current authoritative manufacturing-demand derivation for one Sales Order item.
/// This is demand-orchestration evidence, not a replacement for the time-phased material ledger.
/// </summary>
public sealed class SalesOrderDemandState : Entity
{
    public Guid SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }
    public Guid? ProductionOrderId { get; set; }
    public ProductionOrder? ProductionOrder { get; set; }

    public decimal OpenDemandQuantityMt { get; set; }
    public decimal FinishedGoodsCoveredQuantityMt { get; set; }
    public decimal ManufacturingRequirementQuantityMt { get; set; }

    public DateTime CustomerRequiredDate { get; set; }
    public DateTime? ConfirmedDeliveryDate { get; set; }
    public DateTime ProductionRequiredByDate { get; set; }
    public int Priority { get; set; }

    public DemandReconciliationDisposition Disposition { get; set; }
    public bool PlannerAttentionRequired { get; set; }
    public string? ReasonCode { get; set; }
    public DateTime CalculatedOnUtc { get; set; } = DateTime.UtcNow;

    public ICollection<SalesOrderFinishedGoodsCoverage> FinishedGoodsCoverage { get; set; } =
        new List<SalesOrderFinishedGoodsCoverage>();
}

/// <summary>
/// Evidence of the qualified finished-goods quantity used when deriving an MTO manufacturing requirement.
/// It intentionally records aggregate inventory-position evidence only; #14 owns the future canonical
/// lot/reservation ledger and may replace the backing mechanism without changing the demand contract.
/// </summary>
public sealed class SalesOrderFinishedGoodsCoverage : Entity
{
    public Guid SalesOrderDemandStateId { get; set; }
    public SalesOrderDemandState? SalesOrderDemandState { get; set; }
    public required string MaterialCode { get; set; }
    public required string GradeCode { get; set; }
    public required string CrossSectionCode { get; set; }
    public string? LocationCode { get; set; }
    public DateTime? AvailableFromUtc { get; set; }
    public MaterialQualityStatus QualityStatus { get; set; } = MaterialQualityStatus.Available;
    public decimal QuantityMt { get; set; }
}

/// <summary>
/// Immutable Plan Version evidence of how one SO item was converted into manufacturing demand.
/// </summary>
public sealed class PlanDemandSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid? ProductionOrderId { get; set; }
    public required string SalesOrderNumber { get; set; }
    public required string SalesOrderItemNumber { get; set; }
    public string? CustomerCode { get; set; }
    public string? CustomerGroupCode { get; set; }
    public required string MaterialCode { get; set; }
    public required string GradeCode { get; set; }
    public required string FinalCrossSectionCode { get; set; }
    public decimal OpenDemandQuantityMt { get; set; }
    public decimal FinishedGoodsCoveredQuantityMt { get; set; }
    public decimal ManufacturingRequirementQuantityMt { get; set; }
    public DateTime CustomerRequiredDate { get; set; }
    public DateTime? ConfirmedDeliveryDate { get; set; }
    public DateTime ProductionRequiredByDate { get; set; }
    public int Priority { get; set; }
    public DemandReconciliationDisposition Disposition { get; set; }
    public bool PlannerAttentionRequired { get; set; }
    public string? ReasonCode { get; set; }
}

public sealed class PlanDemandCoverageSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid? ProductionOrderId { get; set; }
    public required string MaterialCode { get; set; }
    public required string GradeCode { get; set; }
    public required string CrossSectionCode { get; set; }
    public string? LocationCode { get; set; }
    public DateTime? AvailableFromUtc { get; set; }
    public MaterialQualityStatus QualityStatus { get; set; }
    public decimal QuantityMt { get; set; }
}
