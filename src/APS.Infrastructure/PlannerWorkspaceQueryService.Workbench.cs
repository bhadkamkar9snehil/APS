using APS.Application;
using APS.Domain;

namespace APS.Infrastructure;

public sealed partial class PlannerWorkspaceQueryService
{
    public async Task<PlanningWorkbenchView?> GetPlanningWorkbenchAsync(
        Guid? planVersionId = null,
        Guid? baselinePlanVersionId = null,
        CancellationToken cancellationToken = default)
    {
        var plan = planVersionId.HasValue
            ? await GetPlanContextAsync(planVersionId.Value, cancellationToken)
            : await GetCurrentPlanAsync(cancellationToken);
        if (plan is null) return null;

        var demand = await GetDemandSupplyAsync(plan.PlanVersionId, cancellationToken);
        var campaigns = await GetCampaignStudioAsync(plan.PlanVersionId, cancellationToken);
        var schedule = await GetFiniteScheduleAsync(plan.PlanVersionId, cancellationToken);
        var material = await GetMaterialFlowAsync(plan.PlanVersionId, cancellationToken);
        if (demand is null || campaigns is null || schedule is null || material is null) return null;

        PlanContextView? baseline = null;
        PlanComparisonWorkspaceView? comparison = null;
        var effectiveBaselineId = baselinePlanVersionId ?? plan.ParentPlanVersionId;
        if (effectiveBaselineId.HasValue && effectiveBaselineId.Value != plan.PlanVersionId)
        {
            baseline = await GetPlanContextAsync(effectiveBaselineId.Value, cancellationToken);
            if (baseline is not null)
            {
                comparison = await GetPlanComparisonAsync(
                    effectiveBaselineId.Value,
                    plan.PlanVersionId,
                    cancellationToken);
            }
        }

        var exceptions = BuildExceptions(plan, demand, schedule, material);
        var lateDemand = demand.Rows.Count(row => row.RequiredDate < schedule.ScheduleEndUtc);
        var queue = new PlanningQueueView(
            demand.Rows.Count,
            demand.Rows.Count(row => row.Status != ProductionOrderStatus.Planned &&
                                     row.Status != ProductionOrderStatus.Firmed &&
                                     row.Status != ProductionOrderStatus.Released),
            lateDemand,
            campaigns.CampaignCount,
            material.Pools.Count,
            exceptions.Count(x => x.Severity == PlanningWorkbenchExceptionSeverity.Critical),
            exceptions.Count(x => x.Severity == PlanningWorkbenchExceptionSeverity.Warning));

        return new PlanningWorkbenchView(
            plan,
            baseline,
            demand,
            campaigns,
            schedule,
            material,
            comparison,
            queue,
            exceptions);
    }

    private static IReadOnlyCollection<PlanningWorkbenchException> BuildExceptions(
        PlanContextView plan,
        DemandSupplyView demand,
        FiniteScheduleWorkspaceView schedule,
        MaterialFlowWorkspaceView material)
    {
        var result = new List<PlanningWorkbenchException>();

        if (plan.Status == PlanVersionStatus.Failed)
        {
            result.Add(new PlanningWorkbenchException(
                "PLAN-INFEASIBLE",
                PlanningWorkbenchExceptionKind.InfeasiblePlan,
                PlanningWorkbenchExceptionSeverity.Critical,
                "Plan is infeasible",
                plan.Reason ?? "The solver could not produce a feasible schedule.",
                new PlannerEntityRef(PlannerEntityType.PlanVersion, plan.PlanVersionId, plan.VersionNumber)));
        }

        foreach (var row in demand.Rows.Where(x => x.UncoveredQuantityMt > 0m))
        {
            result.Add(new PlanningWorkbenchException(
                $"DEMAND-UNCOVERED-{row.ProductionOrderNumber}",
                PlanningWorkbenchExceptionKind.UncoveredDemand,
                PlanningWorkbenchExceptionSeverity.Critical,
                $"{row.ProductionOrderNumber} has uncovered demand",
                $"{row.UncoveredQuantityMt:0.##} MT is not covered by stock, supply, or fresh production.",
                new PlannerEntityRef(PlannerEntityType.ProductionOrder, row.ProductionOrderId, row.ProductionOrderNumber)));
        }

        foreach (var salesOrder in demand.EffectiveSalesOrders().Where(x => x.PlannerAttentionRequired))
        {
            result.Add(new PlanningWorkbenchException(
                $"DEMAND-ATTENTION-{salesOrder.SalesOrderNumber}-{salesOrder.SalesOrderItemNumber}",
                PlanningWorkbenchExceptionKind.DemandAttention,
                PlanningWorkbenchExceptionSeverity.Warning,
                $"{salesOrder.SalesOrderNumber}/{salesOrder.SalesOrderItemNumber} needs attention",
                salesOrder.ReasonCode ?? "The reconciled sales-order requirement needs a planner decision.",
                new PlannerEntityRef(PlannerEntityType.SalesOrder, salesOrder.SalesOrderId, $"{salesOrder.SalesOrderNumber}/{salesOrder.SalesOrderItemNumber}")));
        }

        foreach (var lane in schedule.ResourceLanes.Where(x => x.OperatingState != ResourceOperatingState.Available))
        {
            result.Add(new PlanningWorkbenchException(
                $"RESOURCE-{lane.ResourceCode}-{lane.OperatingState}",
                PlanningWorkbenchExceptionKind.ResourceUnavailable,
                PlanningWorkbenchExceptionSeverity.Critical,
                $"{lane.ResourceCode} is {lane.OperatingState}",
                $"{lane.Operations.Count} scheduled operation(s) use this resource.",
                new PlannerEntityRef(PlannerEntityType.Resource, lane.ResourceId, lane.ResourceCode)));
        }

        foreach (var pool in material.Pools.Where(x => x.ClosingBalanceMt < 0m))
        {
            result.Add(new PlanningWorkbenchException(
                $"MATERIAL-{pool.MaterialPoolKey}",
                PlanningWorkbenchExceptionKind.MaterialShortage,
                PlanningWorkbenchExceptionSeverity.Critical,
                $"{pool.GradeCode} / {pool.CrossSectionCode} is short",
                $"Closing balance is {pool.ClosingBalanceMt:0.##} MT.",
                new PlannerEntityRef(PlannerEntityType.MaterialSupply, Guid.Empty, $"{pool.GradeCode}/{pool.CrossSectionCode}")));
        }

        return result;
    }
}

internal static class DemandSupplyViewExtensions
{
    public static IReadOnlyCollection<SalesOrderDemandRowView> EffectiveSalesOrders(this DemandSupplyView view) =>
        view.SalesOrders ?? Array.Empty<SalesOrderDemandRowView>();
}
