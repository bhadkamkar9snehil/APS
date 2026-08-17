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
    Dispatch = 13
}

/// <summary>
/// A plan's explicit claim on one qualified material supply. The same supply quantity cannot be
/// committed twice inside an authoritative plan, and released-plan reservations can be carried
/// forward into replanning.
/// </summary>
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

/// <summary>
/// Auditable time-phased material movement. Positive quantity is receipt; negative quantity is
/// consumption/withdrawal. Task-linked events use the scheduled task start/end as their effective time.
/// </summary>
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
