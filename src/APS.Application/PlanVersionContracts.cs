using APS.Domain;

namespace APS.Application;

public sealed record PersistPlanningRunRequest(
    PlanningRunRequest PlanningRequest,
    PlanningRunResult PlanningResult,
    PlanTriggerType Trigger,
    DateTime ReferenceTimeUtc,
    string? Reason = null,
    DemandOrchestrationResult? Demand = null);

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
    IReadOnlyCollection<PlanningSupplyAlternative>? SourcingAlternatives = null)
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
}
