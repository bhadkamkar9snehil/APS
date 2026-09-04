using APS.Application;
using APS.Domain;
using Microsoft.Extensions.Logging;

namespace APS.Infrastructure;

/// <summary>
/// Canonical production orchestration. HTTP/UI callers provide demand selection and planning controls only;
/// Sales Order reconciliation, MTO/MTS manufacturing demand, plant masters, current inventory, committed
/// in-process supply and Plan Version persistence are resolved behind this boundary.
/// </summary>
public sealed class PlanningLifecycleService(
    IPlanningEngine _planningEngine,
    IPlanVersionRepository _plans,
    IPlanningMasterDataProvider _masters,
    IInventorySnapshotProvider _inventory,
    IReplanningActualStateProvider _actualState,
    IProductionDemandOrchestrationService _demand,
    ILogger<PlanningLifecycleService> _logger,
    IOrderServicePolicyService? _orderServicePolicies = null) : IPlanningLifecycleService
{
    public async Task<PersistedPlanningRunResult> CalculateAsync(
        PlanningCalculationRequest request,
        CancellationToken cancellationToken = default)
    {
        var referenceTime = DateTime.UtcNow;
        var masterData = await _masters.GetAsync(cancellationToken);
        ValidateProductionConfiguration(request, masterData);

        var inventory = await _inventory.GetInventoryAsync(cancellationToken);
        var demand = await _demand.PrepareAsync(
            request.Demand,
            inventory,
            masterData,
            referenceTime,
            request.HorizonEndUtc,
            cancellationToken);
        demand = await ApplyOrderServiceWindowsAsync(demand, cancellationToken);
        EnsureDemandIsPlannable(demand);

        var planningRequest = BuildPlanningRequest(request, demand.ProductionOrders, masterData, inventory);
        var result = _planningEngine.Run(planningRequest);
        var version = await _plans.SaveAsync(new PersistPlanningRunRequest(
            planningRequest,
            result,
            PlanTriggerType.Manual,
            referenceTime,
            "Canonical production planning calculation",
            demand), cancellationToken);

        _logger.LogInformation(
            "Completed canonical planning lifecycle. PlanVersionId={PlanVersionId} ProductionOrders={ProductionOrderCount} MtoDemand={MtoDemandCount} Feasible={Feasible}",
            result.PlanVersionId,
            demand.ProductionOrders.Count,
            demand.MakeToOrderDemand.Count,
            result.IsFeasible);

        return new PersistedPlanningRunResult(result, version);
    }

    public async Task<PersistedPlanningRunResult> ReplanAsync(
        Guid baselinePlanVersionId,
        PlanningRecalculationRequest request,
        CancellationToken cancellationToken = default)
    {
        var baseline = await _plans.GetAsync(baselinePlanVersionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Plan Version {baselinePlanVersionId} was not found.");
        var baselineCampaignAllocations = await _plans.GetBaselineCampaignAllocationsAsync(
            baselinePlanVersionId,
            cancellationToken);

        // Operational/workbench replans must not silently reset heat/campaign/structure/scenario controls
        // just because a UI caller still has legacy default fields. The immutable baseline is the source of
        // truth for those controls unless the caller explicitly says the planner changed the profile.
        var effectivePlanning = ApplyBaselinePlanningControls(
            request.Planning,
            baseline.Assumptions,
            request.UseBaselinePlanningControls);
        var effectiveTimeFence = request.UseBaselinePlanningControls
            ? baseline.Assumptions?.TimeFencePolicy ?? request.TimeFencePolicy
            : request.TimeFencePolicy;
        var effectiveRepairScope = request.RepairScope
            ?? (request.UseBaselinePlanningControls ? baseline.Assumptions?.RepairScopePolicy : null);

        var masterData = await _masters.GetAsync(cancellationToken);
        ValidateProductionConfiguration(effectivePlanning, masterData);

        var referenceTime = request.ReferenceTimeUtc ?? DateTime.UtcNow;
        var actualState = await _actualState.GetAsync(
            baselinePlanVersionId,
            referenceTime,
            baseline.Operations,
            cancellationToken);

        var demand = await _demand.PrepareAsync(
            effectivePlanning.Demand,
            actualState.Inventory,
            masterData,
            referenceTime,
            effectivePlanning.HorizonEndUtc,
            cancellationToken);
        demand = await ApplyOrderServiceWindowsAsync(demand, cancellationToken);
        EnsureDemandIsPlannable(demand);

        var committedSupplies = actualState.EffectiveCommittedFutureSupplies
            .GroupBy(x => x.SupplyReference, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .OrderBy(x => x.AvailableFromUtc)
            .ThenBy(x => x.SupplyReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var replanContext = new PlanningReplanContext(
            baselinePlanVersionId,
            referenceTime,
            effectiveTimeFence,
            actualState.BaselineOperations,
            ResourceOverrides: request.ResourceOverrides,
            RepairScope: effectiveRepairScope,
            BaselineCampaignAllocations: baselineCampaignAllocations,
            ScheduleOverrides: request.ScheduleOverrides);

        var planningRequest = BuildPlanningRequest(
            effectivePlanning,
            demand.ProductionOrders,
            masterData,
            actualState.Inventory,
            committedSupplies,
            replanContext);

        var result = _planningEngine.Run(planningRequest);
        var trigger = request.ResourceOverrides is { Count: > 0 } || request.ScheduleOverrides is { Count: > 0 }
            ? PlanTriggerType.OperationalRedispatch
            : request.Trigger;
        var reason = request.Reason ?? (trigger == PlanTriggerType.OperationalRedispatch
            ? "Operational resource redispatch"
            : "Replanning from authoritative demand, execution and inventory state");

        var version = await _plans.SaveAsync(new PersistPlanningRunRequest(
            planningRequest,
            result,
            trigger,
            referenceTime,
            reason,
            demand), cancellationToken);

        _logger.LogInformation(
            "Completed canonical replan. BaselinePlanVersionId={BaselinePlanVersionId} PlanVersionId={PlanVersionId} ProductionOrders={ProductionOrderCount} Feasible={Feasible} BaselineControlsInherited={BaselineControlsInherited}",
            baselinePlanVersionId,
            result.PlanVersionId,
            demand.ProductionOrders.Count,
            result.IsFeasible,
            request.UseBaselinePlanningControls);

        return new PersistedPlanningRunResult(result, version, actualState);
    }

    private async Task<DemandOrchestrationResult> ApplyOrderServiceWindowsAsync(
        DemandOrchestrationResult demand,
        CancellationToken cancellationToken)
    {
        if (_orderServicePolicies is null || demand.MakeToOrderDemand.Count == 0) return demand;
        var salesOrderIds = demand.MakeToOrderDemand.Select(x => x.SalesOrderId).Distinct().ToArray();
        var policies = await _orderServicePolicies.GetAsync(salesOrderIds, cancellationToken);
        return OrderServiceWindow.Apply(demand, policies);
    }

    private static PlanningCalculationRequest ApplyBaselinePlanningControls(
        PlanningCalculationRequest requested,
        PlanningAssumptions? assumptions,
        bool useBaselineControls)
    {
        if (!useBaselineControls || assumptions is null) return requested;

        // Null is meaningful for ScenarioCode and AssignmentPolicies. A persisted null means the plan
        // used the configured baseline plant / no explicit commitment policy; it must not inherit stale
        // values from whichever planner session happens to request the replan.
        var campaignPolicy = assumptions.CampaignPolicy
            ?? requested.CampaignPolicy with { ObjectiveWeights = assumptions.CampaignObjectiveWeights };

        return requested with
        {
            CampaignPolicy = campaignPolicy,
            StructurePolicy = assumptions.StructurePolicy ?? requested.StructurePolicy,
            MaxSolverSeconds = assumptions.MaxSolverSeconds ?? requested.MaxSolverSeconds,
            AssignmentPolicies = assumptions.AssignmentPolicies,
            ScenarioCode = assumptions.ScenarioCode
        };
    }

    private static PlanningRunRequest BuildPlanningRequest(
        PlanningCalculationRequest request,
        IReadOnlyCollection<ProductionOrder> productionOrders,
        PlanningMasterDataSnapshot masterData,
        IReadOnlyCollection<InventoryPosition> inventory,
        IReadOnlyCollection<CommittedMaterialSupply>? committedSupplies = null,
        PlanningReplanContext? replanContext = null)
    {
        var productionInventory = inventory
            .Where(x => x.Stage != InventoryStage.FinishedGoods)
            .ToArray();
        var campaignPolicy = replanContext?.BaselineCampaignAllocations is { Count: > 0 } baselineCampaignAllocations
            ? request.CampaignPolicy with { BaselineCampaignAllocations = baselineCampaignAllocations }
            : request.CampaignPolicy;

        return new PlanningRunRequest(
            ProductionOrders: productionOrders,
            Inventory: productionInventory,
            Resources: masterData.Resources,
            Capabilities: masterData.ResourceCapabilities,
            ResourceCalendars: masterData.ResourceCalendars,
            TransitionRules: masterData.TransitionRules,
            FlowLinks: masterData.FlowLinks,
            CampaignPolicy: campaignPolicy,
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
            BillsOfMaterial: masterData.EffectiveBillsOfMaterial,
            ExecutionMode: PlanningExecutionMode.Production,
            GradeTemperatureRequirements: masterData.EffectiveGradeTemperatureRequirements,
            ResourceTemperatureCapabilities: masterData.EffectiveResourceTemperatureCapabilities,
            Scenario: ResolveScenario(request.ScenarioCode, masterData));
    }

    private static PlanningScenario? ResolveScenario(string? scenarioCode, PlanningMasterDataSnapshot masterData)
    {
        if (string.IsNullOrWhiteSpace(scenarioCode)) return null;
        var scenario = masterData.EffectivePlanningScenarios
            .FirstOrDefault(x => string.Equals(x.ScenarioCode, scenarioCode, StringComparison.OrdinalIgnoreCase));
        return scenario?.IsBaseline == true ? null : scenario;
    }

    private static void ValidateProductionConfiguration(
        PlanningCalculationRequest request,
        PlanningMasterDataSnapshot masterData)
    {
        var issues = new List<string>();

        if (request.HorizonEndUtc <= request.HorizonStartUtc)
            issues.Add("Planning horizon end must be after its start.");
        if (masterData.Resources.Count == 0)
            issues.Add("No physical resources are configured in APS master data.");
        if (masterData.RoutePlanning is null)
            issues.Add("No configured manufacturing-route operations are available; production planning cannot use the simplified compatibility structure fallback.");

        if (!string.IsNullOrWhiteSpace(request.ScenarioCode) &&
            !masterData.EffectivePlanningScenarios.Any(x =>
                string.Equals(x.ScenarioCode, request.ScenarioCode.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add($"Planning scenario '{request.ScenarioCode.Trim()}' was not found. Refresh Planning Controls or select the configured baseline plant.");
        }

        var supplyPolicy = request.MaterialSupplyPolicy;
        if (supplyPolicy is not null &&
            (supplyPolicy.AllowExternalBuy || supplyPolicy.AllowTransfer || supplyPolicy.AllowManualSupply))
        {
            issues.Add("Production APS is manufacturing-only. BUY, TRANSFER and manual speculative supply planning are not permitted; known incoming material must arrive through authoritative supply/inventory integration.");
        }

        if (issues.Count > 0)
            throw new PlanningConfigurationException(issues.ToArray());
    }

    private static void EnsureDemandIsPlannable(DemandOrchestrationResult demand)
    {
        var errors = demand.Issues.Where(x => x.Severity == PlanningIssueSeverity.Error).ToArray();
        if (errors.Length == 0) return;
        throw new PlanningConfigurationException(errors.Select(x => $"{x.Code}: {x.Message}").ToArray());
    }
}
