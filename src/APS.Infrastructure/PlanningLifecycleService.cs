using APS.Application;
using APS.Domain;

namespace APS.Infrastructure;

/// <summary>
/// Canonical production orchestration. HTTP/UI callers provide demand and planning controls only;
/// plant masters, current inventory, committed in-process supply and Plan Version persistence are
/// resolved here so every production calculation uses the same state boundary.
/// </summary>
public sealed class PlanningLifecycleService : IPlanningLifecycleService
{
    private readonly IPlanningEngine _planningEngine;
    private readonly IPlanVersionRepository _plans;
    private readonly IPlanningMasterDataProvider _masters;
    private readonly IInventorySnapshotProvider _inventory;
    private readonly IReplanningActualStateProvider _actualState;

    public PlanningLifecycleService(
        IPlanningEngine planningEngine,
        IPlanVersionRepository plans,
        IPlanningMasterDataProvider masters,
        IInventorySnapshotProvider inventory,
        IReplanningActualStateProvider actualState)
    {
        _planningEngine = planningEngine;
        _plans = plans;
        _masters = masters;
        _inventory = inventory;
        _actualState = actualState;
    }

    public async Task<PersistedPlanningRunResult> CalculateAsync(
        PlanningCalculationRequest request,
        CancellationToken cancellationToken = default)
    {
        var masterData = await _masters.GetAsync(cancellationToken);
        ValidateProductionConfiguration(request, masterData);

        var inventory = await _inventory.GetInventoryAsync(cancellationToken);
        var planningRequest = BuildPlanningRequest(request, masterData, inventory);
        var result = _planningEngine.Run(planningRequest);
        var referenceTime = DateTime.UtcNow;
        var version = await _plans.SaveAsync(new PersistPlanningRunRequest(
            planningRequest,
            result,
            PlanTriggerType.Manual,
            referenceTime,
            "Canonical production planning calculation"), cancellationToken);

        return new PersistedPlanningRunResult(result, version);
    }

    public async Task<PersistedPlanningRunResult> ReplanAsync(
        Guid baselinePlanVersionId,
        PlanningRecalculationRequest request,
        CancellationToken cancellationToken = default)
    {
        var baseline = await _plans.GetAsync(baselinePlanVersionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Plan Version {baselinePlanVersionId} was not found.");

        var masterData = await _masters.GetAsync(cancellationToken);
        ValidateProductionConfiguration(request.Planning, masterData);

        var referenceTime = request.ReferenceTimeUtc ?? DateTime.UtcNow;
        var actualState = await _actualState.GetAsync(
            baselinePlanVersionId,
            referenceTime,
            baseline.Operations,
            cancellationToken);

        var committedSupplies = actualState.EffectiveCommittedFutureSupplies
            .GroupBy(x => x.SupplyReference, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .OrderBy(x => x.AvailableFromUtc)
            .ThenBy(x => x.SupplyReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var replanContext = new PlanningReplanContext(
            baselinePlanVersionId,
            referenceTime,
            request.TimeFencePolicy,
            actualState.BaselineOperations,
            request.ResourceOverrides);

        var planningRequest = BuildPlanningRequest(
            request.Planning,
            masterData,
            actualState.Inventory,
            committedSupplies,
            replanContext);

        var result = _planningEngine.Run(planningRequest);
        var trigger = request.ResourceOverrides is { Count: > 0 }
            ? PlanTriggerType.OperationalRedispatch
            : request.Trigger;
        var reason = request.Reason ?? (trigger == PlanTriggerType.OperationalRedispatch
            ? "Operational resource redispatch"
            : "Replanning from authoritative execution and inventory state");

        var version = await _plans.SaveAsync(new PersistPlanningRunRequest(
            planningRequest,
            result,
            trigger,
            referenceTime,
            reason), cancellationToken);

        return new PersistedPlanningRunResult(result, version, actualState);
    }

    private static PlanningRunRequest BuildPlanningRequest(
        PlanningCalculationRequest request,
        PlanningMasterDataSnapshot masterData,
        IReadOnlyCollection<InventoryPosition> inventory,
        IReadOnlyCollection<CommittedMaterialSupply>? committedSupplies = null,
        PlanningReplanContext? replanContext = null)
    {
        return new PlanningRunRequest(
            ProductionOrders: request.ProductionOrders,
            Inventory: inventory,
            Resources: masterData.Resources,
            Capabilities: masterData.ResourceCapabilities,
            ResourceCalendars: masterData.ResourceCalendars,
            TransitionRules: masterData.TransitionRules,
            FlowLinks: masterData.FlowLinks,
            CampaignPolicy: request.CampaignPolicy,
            StructurePolicy: request.StructurePolicy,
            HorizonStartUtc: request.HorizonStartUtc,
            HorizonEndUtc: request.HorizonEndUtc,
            MaxSolverSeconds: request.MaxSolverSeconds,
            CampaignNumberPrefix: request.CampaignNumberPrefix,
            ReplanContext: replanContext,
            RoutePlanning: masterData.RoutePlanning,
            SteelGrades: masterData.EffectiveSteelGrades,
            CrossSections: masterData.EffectiveCrossSections,
            MaterialSpecifications: masterData.EffectiveMaterialSpecifications,
            PackagingSpecifications: masterData.EffectivePackagingSpecifications,
            ExternalMaterialSupplies: masterData.EffectiveExternalMaterialSupplies,
            MaterialSupplyPolicy: request.MaterialSupplyPolicy,
            AssignmentPolicies: request.AssignmentPolicies,
            MaterialSourcingRules: masterData.EffectiveMaterialSourcingRules,
            CommittedMaterialSupplies: committedSupplies,
            ExecutionMode: PlanningExecutionMode.Production);
    }

    private static void ValidateProductionConfiguration(
        PlanningCalculationRequest request,
        PlanningMasterDataSnapshot masterData)
    {
        var issues = new List<string>();

        if (masterData.Resources.Count == 0)
            issues.Add("No physical resources are configured in APS master data.");
        if (masterData.RoutePlanning is null)
            issues.Add("No configured manufacturing-route operations are available; production planning cannot use the simplified compatibility structure fallback.");

        var supplyPolicy = request.MaterialSupplyPolicy;
        if (supplyPolicy is not null &&
            (supplyPolicy.AllowExternalBuy || supplyPolicy.AllowTransfer || supplyPolicy.AllowManualSupply))
        {
            issues.Add("Production APS is manufacturing-only. BUY, TRANSFER and manual speculative supply planning are not permitted; known incoming material must arrive through authoritative supply/inventory integration.");
        }

        if (issues.Count > 0)
            throw new PlanningConfigurationException(issues.ToArray());
    }
}
