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
    Unsourced = 6,
    Covered = 7,
    InternalProductionRequired = 8,
    NotManufacturableHere = 9,
    ProjectedOutput = 10,
    CycleBlocked = 11
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
    StockPolicy = 5,
    BomComponent = 6
}

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
    public bool AllowManualSupply { get; set; }
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
    public Guid? ParentRequirementId { get; set; }
    public string? RequirementPath { get; set; }
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

    /// <summary>
    /// Generic quantity/UOM facts used by recursive BOM planning. These fields are authoritative for non-MT flows.
    /// Legacy *Mt properties remain populated when MaterialUom is MT so existing steel-route consumers stay compatible.
    /// </summary>
    public string MaterialUom { get; set; } = "MT";
    public decimal GrossQuantity { get; set; }
    public decimal CoveredQuantity { get; set; }
    public decimal OpeningInventoryCoveredQuantity { get; set; }
    public decimal KnownIncomingCoveredQuantity { get; set; }
    public decimal CommittedProductionCoveredQuantity { get; set; }
    public decimal PlannedProductionCoveredQuantity { get; set; }
    public decimal ActualProductionCoveredQuantity { get; set; }
    public decimal LateSupplyQuantity { get; set; }
    public decimal NetRequirementQuantity { get; set; }
    public decimal InternalProductionQuantity { get; set; }
    public decimal ShortfallQuantity { get; set; }
    public decimal ProducedQuantity { get; set; }
    public BomFlowType FlowType { get; set; } = BomFlowType.Input;

    public Guid? SelectedBomId { get; set; }
    public string? SelectedBomCode { get; set; }
    public int? SelectedBomVersion { get; set; }
    public decimal? EffectiveYieldPct { get; set; }
    public decimal? EffectiveScrapPct { get; set; }
    public bool IsInternallyManufacturable { get; set; }
    public string? TimingBasisCode { get; set; }
    public string? QualificationCode { get; set; }

    public decimal RequiredQuantityMt { get; set; }

    /// <summary>Actual planned material-consumption time after finite scheduling.</summary>
    public DateTime RequiredAtUtc { get; set; }

    /// <summary>
    /// Latest service-feasible need time from backward propagation through the planned process chain.
    /// Supply after this timestamp is explicitly classified LateSupply even if APS can delay the operation.
    /// </summary>
    public DateTime? TargetRequiredAtUtc { get; set; }

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

    /// <summary>Quantity actually required/reserved by the originating material requirement.</summary>
    public decimal QuantityMt { get; set; }

    /// <summary>Commercial order/transfer quantity after MOQ/order-multiple rules.</summary>
    public decimal PlannedOrderQuantityMt { get; set; }

    /// <summary>PlannedOrderQuantityMt - QuantityMt; projected future inventory, not silently reserved to this PO.</summary>
    public decimal ExcessQuantityMt { get; set; }

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
