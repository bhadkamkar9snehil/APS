namespace APS.Domain;

public enum PlanVersionStatus
{
    Draft = 1,
    Feasible = 2,
    Released = 3,
    Superseded = 4,
    Failed = 5,
    Approved = 6
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

public enum OperationAssignmentCommitmentState
{
    Flexible = 1,
    Firm = 2,
    Committed = 3,
    Running = 4,
    Completed = 5
}

public enum OperationExecutionStatus
{
    Planned = 1,
    Ready = 2,
    Running = 3,
    Held = 4,
    Completed = 5,
    Cancelled = 6
}

public sealed record OperationExecutionEventSnapshot(
    OperationExecutionStatus PreviousStatus,
    OperationExecutionStatus NewStatus,
    Guid? ResourceId,
    DateTime ChangedOnUtc,
    ExecutionUpdateSource Source,
    string? ExternalEventId,
    string? Comment);

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
    public string? MaterialRequirementsJson { get; set; }
    public string? MaterialSupplyRequirementsJson { get; set; }
    public string? MaterialReservationsJson { get; set; }
    public string? MaterialLedgerJson { get; set; }
    public string? MaterialSourcingAlternativesJson { get; set; }

    /// <summary>
    /// What this plan was solved under: the operating-state scenario, the campaign objective weights
    /// and the per-group composition scores, and each resource's scheduling mode and capacity. Master
    /// data changes after a plan is cut, so without this a plan cannot be explained - or compared with
    /// a later one - after the fact.
    /// </summary>
    public string? PlanningAssumptionsJson { get; set; }

    /// <summary>
    /// Every operation of the effective route and what the planner decided about it, including the
    /// ones it chose not to run (#34). A heat whose VD was skipped because the grade did not require
    /// it is otherwise indistinguishable from a heat on a route that never had a VD.
    /// </summary>
    public string? RouteOperationDecisionsJson { get; set; }
}

public sealed class PlanOperationSnapshot : Entity
{
    public Guid PlanVersionId { get; set; }
    public required string PlanningKey { get; set; }
    public Guid SourceEntityId { get; set; }
    public PlanOperationType OperationType { get; set; }
    public ProcessOperationType ProcessOperationType { get; set; } = ProcessOperationType.Unknown;

    /// <summary>
    /// Where this operation sat in the effective route (#34). Without it a plan records the operations
    /// that ran but not the chain they came from, so a read model can only redraw what survived.
    /// </summary>
    public string? RouteCode { get; set; }

    public int? RouteSequenceNumber { get; set; }

    public Guid ResourceId { get; set; }
    public Guid? CommittedResourceId { get; set; }
    public Guid? ActualResourceId { get; set; }
    public OperationAssignmentCommitmentState AssignmentCommitmentState { get; set; } = OperationAssignmentCommitmentState.Flexible;
    public string? EligibleResourceOptionsJson { get; set; }

    public string? PredecessorPlanningKeysJson { get; set; }
    public string? AssignmentPolicyJson { get; set; }
    public DateTime? CommitmentLastEvaluatedOnUtc { get; set; }

    public Guid? PreviousPlannedResourceId { get; set; }
    public string? RedispatchReasonCode { get; set; }
    public string? RedispatchComment { get; set; }
    public DateTime? RedispatchedOnUtc { get; set; }

    public OperationExecutionStatus ExecutionStatus { get; set; } = OperationExecutionStatus.Planned;
    public DateTime? ActualStartUtc { get; set; }
    public DateTime? ActualEndUtc { get; set; }
    public decimal ActualQuantityMt { get; set; }
    public DateTime? LastExecutionChangedOnUtc { get; set; }
    public string? ExecutionHistoryJson { get; set; }

    public bool IsOffPlanActualResource { get; set; }
    public string? OffPlanActualReasonCode { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public decimal QuantityMt { get; set; }
    public required string GradeCode { get; set; }
    public required string CrossSectionCode { get; set; }
}

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
