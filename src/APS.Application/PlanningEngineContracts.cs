using APS.Domain;

namespace APS.Application;

public enum PlanningExecutionMode
{
    Compatibility = 0,
    Production = 1
}

public sealed record PlanningTimeFencePolicy(
    int FrozenMinutes = 120,
    int SlushyMinutes = 720,
    int SlushyMovementPenaltyPerMinute = 50,
    int SlushyResourceChangePenalty = 5000);

public sealed record OperationAssignmentPolicy(
    ProcessOperationType ProcessOperationType,
    int FirmMinutesBeforeStart = 120,
    int CommitMinutesBeforeStart = 30,
    bool AllowRedispatchWhenFirm = true,
    bool AllowRedispatchWhenCommittedForDisruption = true,
    bool CommitWhenPredecessorRunning = false,
    bool CommitWhenPredecessorCompleted = false,
    bool RequireDispatchAcknowledgement = false);

public sealed record BaselinePlanOperation(
    string PlanningKey,
    Guid ResourceId,
    DateTime StartUtc,
    DateTime EndUtc,
    FiniteScheduleTaskType TaskType);

public sealed record OperationResourceOverride(
    string PlanningKey,
    Guid ResourceId,
    OperationAssignmentCommitmentState CommitmentState = OperationAssignmentCommitmentState.Committed,
    string ReasonCode = "OPERATIONAL_REDISPATCH",
    string? Comment = null);

public sealed record RepairScopePolicy(
    int SuccessorDepth = 4,
    int RepairHorizonMinutes = 720,
    bool FreezeUnaffectedOperations = true,
    bool IncludeSameResourceNeighbors = true);

public sealed record PlanningReplanContext(
    Guid BaselinePlanVersionId,
    DateTime ReferenceTimeUtc,
    PlanningTimeFencePolicy TimeFencePolicy,
    IReadOnlyCollection<BaselinePlanOperation> BaselineOperations,
    IReadOnlyCollection<OperationResourceOverride>? ResourceOverrides = null,
    RepairScopePolicy? RepairScope = null);

public sealed record PlanningTaskIdentity(
    Guid TaskId,
    Guid SourceEntityId,
    string PlanningKey,
    FiniteScheduleTaskType TaskType);

public sealed record PlanningOperationResourceAlternative(
    Guid TaskId,
    Guid SourceEntityId,
    string PlanningKey,
    ProcessOperationType ProcessOperationType,
    Guid ResourceId,
    int DurationMinutes,
    int AssignmentPenalty,
    bool WasSelected,
    string? EligibilityBasisCode = null);

public sealed record MaterialSupplyPlanningPolicy(
    bool AllowInternalMake = true,
    bool AllowExternalBuy = false,
    bool AllowTransfer = false,
    bool AllowManualSupply = false,
    TimeSpan? DefaultExternalLeadTime = null,
    bool PreserveCustomerQualifiedPools = true);

public sealed record PlanningRunRequest(
    IReadOnlyCollection<ProductionOrder> ProductionOrders,
    IReadOnlyCollection<InventoryPosition> Inventory,
    IReadOnlyCollection<Resource> Resources,
    IReadOnlyCollection<ResourceCapability> Capabilities,
    IReadOnlyCollection<ResourceCalendar> ResourceCalendars,
    IReadOnlyCollection<TransitionRule> TransitionRules,
    IReadOnlyCollection<PlantFlowLink> FlowLinks,
    CampaignPlanningPolicy CampaignPolicy,
    ProductionStructurePlanningPolicy StructurePolicy,
    DateTime HorizonStartUtc,
    DateTime HorizonEndUtc,
    int MaxSolverSeconds = 20,
    string CampaignNumberPrefix = "CMP",
    PlanningReplanContext? ReplanContext = null,
    RoutePlanningInput? RoutePlanning = null,
    IReadOnlyCollection<SteelGrade>? SteelGrades = null,
    IReadOnlyCollection<CrossSectionSpecification>? CrossSections = null,
    IReadOnlyCollection<MaterialSpecification>? MaterialSpecifications = null,
    IReadOnlyCollection<PackagingSpecification>? PackagingSpecifications = null,
    IReadOnlyCollection<ExternalMaterialSupply>? ExternalMaterialSupplies = null,
    MaterialSupplyPlanningPolicy? MaterialSupplyPolicy = null,
    IReadOnlyCollection<OperationAssignmentPolicy>? AssignmentPolicies = null,
    IReadOnlyCollection<MaterialSourcingRule>? MaterialSourcingRules = null,
    IReadOnlyCollection<CommittedMaterialSupply>? CommittedMaterialSupplies = null,
    PlanningExecutionMode ExecutionMode = PlanningExecutionMode.Compatibility,
    IReadOnlyCollection<BillOfMaterial>? BillsOfMaterial = null,
    IReadOnlyCollection<PrecomputedCampaignMaterialDemand>? PrecomputedCampaignMaterialDemand = null,
    /// <summary>
    /// Grade temperature/superheat envelopes per process operation (#9). Absent, the plan carries no
    /// thermal constraint at all - which is why these must reach the engine rather than sit in master data.
    /// </summary>
    IReadOnlyCollection<GradeProcessTemperatureRequirement>? GradeTemperatureRequirements = null,
    /// <summary>Thermal ability of each physical resource, used to prove a transfer keeps the heat in window.</summary>
    IReadOnlyCollection<ResourceTemperatureCapability>? ResourceTemperatureCapabilities = null,
    /// <summary>
    /// Plant operating-state scenario to plan under (#17) - outages, deratings and grade restrictions
    /// applied to the resource/capability/calendar masters before anything else runs. Null plans the
    /// plant as configured.
    /// </summary>
    PlanningScenario? Scenario = null);

public sealed record PlanningRunResult(
    Guid PlanVersionId,
    DateTime CreatedOnUtc,
    CampaignPlanningResult CampaignPlan,
    ProductionStructurePlanningResult ProductionStructure,
    FiniteScheduleResult Schedule,
    bool IsFeasible,
    IReadOnlyCollection<PlanningTaskIdentity>? TaskIdentities = null,
    Guid? BaselinePlanVersionId = null,
    IReadOnlyCollection<PlannedPackagingUnit>? PlannedPackagingUnits = null,
    IReadOnlyCollection<PlanOrderRequirementSnapshot>? RequirementSnapshots = null,
    IReadOnlyCollection<PlanningOperationResourceAlternative>? ResourceAlternatives = null,
    MaterialPlanningResult? MaterialPlan = null);

public interface IPlanningEngine
{
    PlanningRunResult Run(PlanningRunRequest request);
}
