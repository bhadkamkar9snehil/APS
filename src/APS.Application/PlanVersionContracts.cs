using APS.Domain;

namespace APS.Application;

public sealed record PersistPlanningRunRequest(
    PlanningRunRequest PlanningRequest,
    PlanningRunResult PlanningResult,
    PlanTriggerType Trigger,
    DateTime ReferenceTimeUtc,
    string? Reason = null,
    DemandOrchestrationResult? Demand = null);

/// <summary>
/// The levers a plan was solved under, captured at the moment it was cut (#15/#17/#35). Resource
/// masters, scenarios and objective weights all change; a plan that cannot say what it assumed cannot
/// be defended later, and Plan Compare cannot tell an assumption change apart from a demand change.
/// </summary>
public sealed record PlanningAssumptions(
    /// <summary>The operating-state scenario the plant was planned under, or null for the configured plant.</summary>
    string? ScenarioCode,
    CampaignObjectiveWeights CampaignObjectiveWeights,
    IReadOnlyCollection<CampaignCompositionDecision> CampaignCompositionDecisions,
    IReadOnlyCollection<ResourceSchedulingAssumption> ResourceScheduling,
    IReadOnlyCollection<BilletThermalDecision>? BilletThermalDecisions = null,
    /// <summary>
    /// Effective calendar intervals supplied to the solver for this plan. Optional so plan versions
    /// persisted before calendar snapshotting was introduced remain deserializable.
    /// </summary>
    IReadOnlyCollection<ResourceCalendarAssumption>? ResourceCalendars = null);

/// <summary>How one physical resource was modelled by the solver for this plan (#35).</summary>
public sealed record ResourceSchedulingAssumption(
    Guid ResourceId,
    string ResourceCode,
    ResourceSchedulingMode SchedulingMode,
    ResourceCapacityBasis CapacityBasis,
    decimal? NominalConcurrentCapacity,
    decimal CapacityFactorPct,
    bool AppliesSequenceRules,
    /// <summary>
    /// Effective operating state used by the planning run. Null means the plan predates state
    /// snapshotting and consumers must use their documented compatibility fallback.
    /// </summary>
    ResourceOperatingState? OperatingState = null);

/// <summary>One effective resource-calendar interval supplied to the solver for a persisted plan.</summary>
public sealed record ResourceCalendarAssumption(
    Guid ResourceId,
    DateTime StartUtc,
    DateTime EndUtc,
    bool IsAvailable,
    decimal? CapacityFactorPct,
    string? ReasonCode);

public sealed record PlanVersionSnapshot(
    Guid PlanVersionId,
    string VersionNumber,
    Guid? ParentPlanVersionId,
    PlanVersionStatus Status,
    PlanTriggerType Trigger,
    DateTime CreatedOnUtc,
    DateTime ReferenceTimeUtc,
    DateTime HorizonStartUtc,
    DateTime HorizonEndUtc,
    string? SolverStatus,
    long? ObjectiveValue,
    bool IsActive,
    IReadOnlyCollection<BaselinePlanOperation> Operations,
    IReadOnlyCollection<PlanOperationResourceOptionSnapshot>? ResourceAlternatives = null,
    IReadOnlyCollection<OperationDispatchRevision>? DispatchRevisions = null,
    IReadOnlyCollection<MaterialRequirement>? MaterialRequirements = null,
    IReadOnlyCollection<MaterialSupplyRequirement>? MaterialSupplyRequirements = null,
    IReadOnlyCollection<MaterialSupplyReservation>? MaterialReservations = null,
    IReadOnlyCollection<MaterialBalanceEvent>? MaterialLedger = null,
    IReadOnlyCollection<PlanningSupplyAlternative>? SourcingAlternatives = null,
    /// <summary>
    /// What the plan was solved under. Master data moves on, so this is the only way to explain an
    /// older plan - or to compare two plans and know whether the difference came from the demand or
    /// from the assumptions.
    /// </summary>
    PlanningAssumptions? Assumptions = null,
    /// <summary>
    /// Every operation of the effective route and what the planner decided about it, including the
    /// steps it chose not to run and why (#34). This is what lets a read model draw the manufacturing
    /// chain the plan actually used rather than a fixed EAF/LRF/VD diagram.
    /// </summary>
    IReadOnlyCollection<RouteOperationDecision>? RouteOperationDecisions = null)
{
    /// <summary>
    /// Tree projection over the same persisted MaterialRequirements facts. The existing MaterialRequirements
    /// collection remains the flattened read model, so Plan-Version API consumers can use either representation
    /// without recomputing BOM lineage in the UI.
    /// </summary>
    public IReadOnlyCollection<MaterialRequirementTreeNode> MaterialRequirementTree =>
        MaterialRequirementReadModelBuilder.Build(
            PlanVersionId,
            MaterialRequirements ?? Array.Empty<MaterialRequirement>()).Roots;
}

public interface IPlanVersionRepository
{
    Task<PlanVersionSnapshot> SaveAsync(
        PersistPlanningRunRequest request,
        CancellationToken cancellationToken = default);

    Task<PlanVersionSnapshot?> GetAsync(
        Guid planVersionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<BaselinePlanOperation>> GetBaselineOperationsAsync(
        Guid planVersionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Historical PO->Campaign quantity membership used only as a stability baseline on replan (#15).
    /// New campaigns remain new entities; this is comparison evidence, not identity reuse.
    /// </summary>
    Task<IReadOnlyCollection<BaselineCampaignAllocation>> GetBaselineCampaignAllocationsAsync(
        Guid planVersionId,
        CancellationToken cancellationToken = default);
}
