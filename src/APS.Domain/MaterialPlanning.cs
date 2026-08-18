namespace APS.Domain;

public enum MaterialReservationStatus
{
    Planned = 1,
    Reserved = 2,
    Consumed = 3,
    Released = 4,
    Cancelled = 5
}

public enum MaterialBalanceEventType
{
    OpeningInventory = 1,
    ExternalReceipt = 2,
    PlannedProductionReceipt = 3,
    ActualProductionReceipt = 4,
    PlannedConsumption = 5,
    ActualConsumption = 6,
    Reservation = 7,
    ReservationRelease = 8,
    QualityHold = 9,
    QualityRelease = 10,
    Rejection = 11,
    Adjustment = 12,
    Dispatch = 13,
    PlannedPurchaseReceipt = 14,
    PlannedTransferReceipt = 15
}

public enum MaterialRequirementStatus
{
    AvailableNow = 1,
    PlannedAvailable = 2,
    SupplyActionRequired = 3,
    Shortfall = 4,
    LateSupply = 5,
    Unsourced = 6
}

public enum MaterialSupplyActionType
{
    Make = 1,
    Buy = 2,
    Transfer = 3,
    Manual = 4,
    Unsourced = 5
}

public enum MaterialRequirementSourceType
{
    ProductionOrder = 1,
    Campaign = 2,
    RollingPlan = 3,
    ProcessOperation = 4,
    StockPolicy = 5
}

/// <summary>
/// Master rule defining which supply paths are permitted for a qualified material requirement.
/// Null material/grade/section selectors act as progressively broader defaults; the most specific
/// matching rule wins. This allows normal integrated-plant MAKE preference while retaining approved
/// BUY/TRANSFER contingency paths without hard-coded plant logic.
/// </summary>
public sealed class MaterialSourcingRule : Entity
{
    public required string RuleCode { get; set; }
    public string? MaterialCode { get; set; }
    public string? MaterialSpecificationCode { get; set; }
    public string? GradeCode { get; set; }
    public string? GradeFamilyCode { get; set; }
    public string? CrossSectionCode { get; set; }
    public SteelProductForm? ProductForm { get; set; }
    public string? DestinationLocationCode { get; set; }

    public bool AllowMake { get; set; } = true;
    public bool AllowBuy { get; set; }
    public bool AllowTransfer { get; set; }
    public bool AllowManualSupply { get; set; } = true;
    public MaterialSupplyActionType PreferredAction { get; set; } = MaterialSupplyActionType.Make;

    public TimeSpan? PurchaseLeadTime { get; set; }
    public TimeSpan? TransferLeadTime { get; set; }
    public string? PreferredSupplierCode { get; set; }
    public string? TransferSourceLocationCode { get; set; }
    public decimal? MinimumBuyQuantityMt { get; set; }
    public decimal? BuyOrderMultipleMt { get; set; }
    public decimal? MinimumTransferQuantityMt { get; set; }
    public int MakePenalty { get; set; }
    public int BuyPenalty { get; set; } = 100;
    public int TransferPenalty { get; set; } = 50;
    public bool IsActive { get; set; } = true;
}

public sealed class MaterialRequirement : Entity
{
    public Guid? PlanVersionId { get; set; }
    public required string RequirementKey { get; set; }
    public MaterialRequirementSourceType SourceType { get; set; }
    public Guid SourceEntityId { get; set; }
    public Guid? ProductionOrderId { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? CampaignHeatId { get; set; }
    public string? MaterialSpecificationCode { get; set; }
    public required string MaterialCode { get; set; }
    public required string GradeCode { get; set; }
    public required string CrossSectionCode { get; set; }
    public SteelProductForm ProductForm { get; set; } = SteelProductForm.Other;
    public string? LocationCode { get; set; }
    public decimal RequiredQuantityMt { get; set; }
    public DateTime RequiredAtUtc { get; set; }
    public int Priority { get; set; }
    public MaterialRequirementStatus Status { get; set; } = MaterialRequirementStatus.SupplyActionRequired;
    public decimal CoveredQuantityMt { get; set; }
    public decimal ShortfallQuantityMt { get; set; }
    public DateTime? ExpectedFullyAvailableAtUtc { get; set; }
    public string? Explanation { get; set; }
}

public sealed class MaterialSupplyRequirement : Entity
{
    public Guid? PlanVersionId { get; set; }
    public Guid MaterialRequirementId { get; set; }
    public Guid? ProductionOrderId { get; set; }
    public string? MaterialSpecificationCode { get; set; }
    public required string MaterialCode { get; set; }
    public required string GradeCode { get; set; }
    public required string CrossSectionCode { get; set; }
    public MaterialSupplyActionType ActionType { get; set; }
    public decimal QuantityMt { get; set; }
    public DateTime RequiredReceiptUtc { get; set; }
    public DateTime? ExpectedReceiptUtc { get; set; }
    public string? SupplyReference { get; set; }
    public string? SupplierCode { get; set; }
    public Guid? UpstreamCampaignId { get; set; }
    public Guid? UpstreamHeatId { get; set; }
    public string? SourceLocationCode { get; set; }
    public string? DestinationLocationCode { get; set; }
    public bool IsFirm { get; set; }
    public string? Explanation { get; set; }
}

public sealed class MaterialSupplyReservation : Entity
{
    public Guid? PlanVersionId { get; set; }
    public Guid ProductionOrderId { get; set; }
    public string? MaterialSpecificationCode { get; set; }
    public required string GradeCode { get; set; }
    public required string CrossSectionCode { get; set; }
    public InventoryStage InventoryStage { get; set; }
    public BilletSupplySourceType? ExternalSourceType { get; set; }
    public string? SupplyReference { get; set; }
    public string? LocationCode { get; set; }
    public decimal QuantityMt { get; set; }
    public DateTime AvailableFromUtc { get; set; }
    public MaterialReservationStatus Status { get; set; } = MaterialReservationStatus.Planned;
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
}

public sealed class MaterialBalanceEvent : Entity
{
    public Guid? PlanVersionId { get; set; }
    public MaterialBalanceEventType EventType { get; set; }
    public required string MaterialPoolKey { get; set; }
    public string? MaterialSpecificationCode { get; set; }
    public required string GradeCode { get; set; }
    public required string CrossSectionCode { get; set; }
    public string? LocationCode { get; set; }
    public decimal QuantityDeltaMt { get; set; }
    public DateTime EffectiveAtUtc { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? ProductionOrderId { get; set; }
    public Guid? CampaignHeatId { get; set; }
    public Guid? MaterialLotId { get; set; }
    public string? SupplyReference { get; set; }
    public string? Explanation { get; set; }
}
