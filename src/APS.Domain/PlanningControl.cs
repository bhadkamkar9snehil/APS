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
    MasterDataRefresh = 5,
    OperationalRedispatch = 6,
    SupplyPlanRefresh = 7
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

/// <summary>
/// Physical-resource assignment deliberately hardens later than the production identity.
/// A planned assignment is an optimizer preference until the operation is explicitly firmed/committed.
/// </summary>
public enum OperationAssignmentCommitmentState
{
    Flexible = 1,
    Firm = 2,
    Committed = 3,
    Running = 4,
    Completed = 5
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

    /// <summary>Resource selected by the optimizer for this Plan Version.</summary>
    public Guid ResourceId { get; set; }

    /// <summary>Explicit dispatch commitment, if operations has firmed a physical resource after planning.</summary>
    public Guid? CommittedResourceId { get; set; }

    /// <summary>Actual physical resource from execution. Once running/completed this is historical truth.</summary>
    public Guid? ActualResourceId { get; set; }

    public OperationAssignmentCommitmentState AssignmentCommitmentState { get; set; } = OperationAssignmentCommitmentState.Flexible;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public decimal QuantityMt { get; set; }
    public required string GradeCode { get; set; }
    public required string CrossSectionCode { get; set; }
}

/// <summary>
/// Immutable evidence of every physical resource that remained feasible for an operation when the plan was solved.
/// This is retained even when an alternative is rarely used operationally.
/// </summary>
public sealed class PlanOperationResourceOptionSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public required string PlanningKey { get; set; }
    public Guid SourceEntityId { get; set; }
    public ProcessOperationType ProcessOperationType { get; set; }
    public Guid ResourceId { get; set; }
    public int DurationMinutes { get; set; }
    public int AssignmentPenalty { get; set; }
    public bool WasSelected { get; set; }
    public string? EligibilityBasisCode { get; set; }
    public DateTime CapturedOnUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Audit row for a post-plan physical-resource reassignment. The revised assignment should normally
/// be materialized through a child plan/replan so all route, thermal, queue, material and sequencing
/// constraints are revalidated by the planning kernel.
/// </summary>
public sealed class OperationDispatchRevision : Entity
{
    public Guid PlanVersionId { get; set; }
    public required string PlanningKey { get; set; }
    public Guid PreviousResourceId { get; set; }
    public Guid RevisedResourceId { get; set; }
    public DateTime ChangedOnUtc { get; set; } = DateTime.UtcNow;
    public required string ReasonCode { get; set; }
    public string? Comment { get; set; }
    public ExecutionUpdateSource Source { get; set; } = ExecutionUpdateSource.Manual;
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