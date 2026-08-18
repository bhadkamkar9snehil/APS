namespace APS.Domain;

public enum PlanVersionStatus
{
    Draft = 1,
    Feasible = 2,
    Released = 3,
    Superseded = 4,
    Failed = 5
}

public enum PlanTriggerType
{
    Manual = 1,
    ExecutionFeedback = 2,
    InventoryRefresh = 3,
    DemandRefresh = 4,
    MasterDataRefresh = 5
}

public enum PlanOperationType
{
    Casting = 1,
    HotRolling = 2,
    ColdRolling = 3,
    Finishing = 4,
    Eaf = 5,
    Lrf = 6,
    Vd = 7,
    Reheating = 8,
    Tmt = 9,
    Cooling = 10,
    Cutting = 11,
    Bundling = 12,
    Coiling = 13
}

public sealed class PlanVersionState : Entity
{
    public Guid PlanVersionId { get; set; }
    public Guid? ParentPlanVersionId { get; set; }
    public PlanVersionStatus Status { get; set; } = PlanVersionStatus.Draft;
    public PlanTriggerType Trigger { get; set; } = PlanTriggerType.Manual;
    public DateTime ReferenceTimeUtc { get; set; }
    public DateTime HorizonStartUtc { get; set; }
    public DateTime HorizonEndUtc { get; set; }
    public string? SolverStatus { get; set; }
    public long? ObjectiveValue { get; set; }
    public bool IsActive { get; set; }
}

public sealed class PlanOperationSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public required string PlanningKey { get; set; }
    public Guid SourceEntityId { get; set; }
    public PlanOperationType OperationType { get; set; }
    public ProcessOperationType ProcessOperationType { get; set; } = ProcessOperationType.Unknown;
    public Guid ResourceId { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public decimal QuantityMt { get; set; }
    public required string GradeCode { get; set; }
    public required string CrossSectionCode { get; set; }
}

public sealed class PlanInventoryAllocationSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public Guid ProductionOrderId { get; set; }
    public InventoryStage Stage { get; set; }
    public required string MaterialCode { get; set; }
    public required string GradeCode { get; set; }
    public required string CrossSectionCode { get; set; }
    public string? LocationCode { get; set; }
    public decimal QuantityMt { get; set; }
    public int UseCode { get; set; }
}

public sealed class PlanMaterialUnitSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public required string PlanningKey { get; set; }
    public Guid CampaignId { get; set; }
    public Guid CampaignHeatId { get; set; }
    public Guid CastSequenceId { get; set; }
    public Guid CasterResourceId { get; set; }
    public int StrandNumber { get; set; }
    public int UnitSequence { get; set; }
    public required string GradeCode { get; set; }
    public required string CrossSectionCode { get; set; }
    public decimal QuantityMt { get; set; }
    public DateTime? AvailableOnUtc { get; set; }
}
