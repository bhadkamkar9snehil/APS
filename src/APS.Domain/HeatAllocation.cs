namespace APS.Domain;

/// <summary>
/// Explicit planning pegging from a planned heat to the Production Orders it serves.
/// This is the authoritative bridge for order-specific metallurgy, customer constraints and heat traceability.
/// </summary>
public sealed class CampaignHeatAllocation : Entity
{
    public Guid CampaignHeatId { get; set; }
    public CampaignHeat? CampaignHeat { get; set; }
    public Guid ProductionOrderId { get; set; }
    public ProductionOrder? ProductionOrder { get; set; }
    public decimal PlannedOutputQuantityMt { get; set; }
    public decimal PlannedInputQuantityMt { get; set; }
}
