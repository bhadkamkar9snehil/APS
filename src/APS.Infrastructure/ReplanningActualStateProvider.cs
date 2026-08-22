using System.Text.Json;
using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class ReplanningActualStateProvider(
    ApsDbContext db,
    IInventorySnapshotProvider inventoryProvider) : IReplanningActualStateProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ReplanningActualState> GetAsync(
        Guid baselinePlanVersionId,
        DateTime referenceTimeUtc,
        IReadOnlyCollection<BaselinePlanOperation> baselineOperations,
        CancellationToken cancellationToken = default)
    {
        var baseline = baselineOperations.ToDictionary(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase);
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var allOperationSnapshots = await db.PlanOperationSnapshots
            .AsNoTracking()
            .Where(x => x.PlanVersionId == baselinePlanVersionId)
            .ToListAsync(cancellationToken);

        foreach (var actual in allOperationSnapshots.Where(x => x.ExecutionStatus != OperationExecutionStatus.Planned))
        {
            if (actual.ExecutionStatus == OperationExecutionStatus.Completed)
            {
                baseline.Remove(actual.PlanningKey);
                completed.Add(actual.PlanningKey);
                continue;
            }

            if (actual.ExecutionStatus is OperationExecutionStatus.Running or OperationExecutionStatus.Held &&
                baseline.TryGetValue(actual.PlanningKey, out var planned))
            {
                var actualStart = actual.ActualStartUtc ?? planned.StartUtc;
                var duration = planned.EndUtc - planned.StartUtc;
                var expectedEnd = actual.ActualEndUtc ?? actualStart.Add(duration);
                baseline[actual.PlanningKey] = planned with
                {
                    ResourceId = actual.ActualResourceId ?? actual.CommittedResourceId ?? planned.ResourceId,
                    StartUtc = actualStart,
                    EndUtc = expectedEnd < referenceTimeUtc ? referenceTimeUtc : expectedEnd
                };
                running.Add(actual.PlanningKey);
                continue;
            }

            if (actual.ExecutionStatus == OperationExecutionStatus.Ready &&
                actual.CommittedResourceId.HasValue &&
                baseline.TryGetValue(actual.PlanningKey, out var readyPlanned))
            {
                baseline[actual.PlanningKey] = readyPlanned with { ResourceId = actual.CommittedResourceId.Value };
            }
        }

        // Casting-specific actuals remain supported because they also carry heat/cast/strand output data.
        var heatEvents = await db.HeatExecutionActuals
            .AsNoTracking()
            .Where(x => x.PlanVersionId == baselinePlanVersionId)
            .OrderBy(x => x.ChangedOnUtc)
            .ToListAsync(cancellationToken);
        var latestHeatEvents = heatEvents
            .GroupBy(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(y => y.ChangedOnUtc).First())
            .ToArray();

        foreach (var actual in latestHeatEvents)
        {
            if (actual.Status == HeatExecutionStatus.Completed)
            {
                baseline.Remove(actual.PlanningKey);
                completed.Add(actual.PlanningKey);
                continue;
            }

            if (actual.Status != HeatExecutionStatus.Running || !baseline.TryGetValue(actual.PlanningKey, out var planned)) continue;
            var actualStart = actual.ActualStartUtc ?? planned.StartUtc;
            var duration = planned.EndUtc - planned.StartUtc;
            var expectedEnd = actual.ActualEndUtc ?? actualStart.Add(duration);
            baseline[actual.PlanningKey] = planned with
            {
                ResourceId = actual.CasterResourceId ?? planned.ResourceId,
                StartUtc = actualStart,
                EndUtc = expectedEnd < referenceTimeUtc ? referenceTimeUtc : expectedEnd
            };
            running.Add(actual.PlanningKey);
        }

        // Work Order state remains a coarse fallback for integrations that have not yet sent
        // operation-grain actuals. It also tells us whether a baseline material-producing operation
        // has crossed the execution-release boundary and therefore represents protected future supply.
        var releasedOperations = await db.ScheduledOperations
            .AsNoTracking()
            .Where(x => x.PlanVersionId == baselinePlanVersionId && x.PlanningKey != null)
            .OrderBy(x => x.Start)
            .ToListAsync(cancellationToken);
        var workOrderIds = releasedOperations.Select(x => x.WorkOrderId).Distinct().ToArray();
        var workOrders = await db.WorkOrders
            .AsNoTracking()
            .Where(x => workOrderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var releasedPlanningKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var operationGroup in releasedOperations.GroupBy(x => x.WorkOrderId))
        {
            if (!workOrders.TryGetValue(operationGroup.Key, out var workOrder)) continue;
            var operationKeys = operationGroup
                .Where(x => !string.IsNullOrWhiteSpace(x.PlanningKey))
                .Select(x => x.PlanningKey!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (workOrder.Status is WorkOrderStatus.Released or WorkOrderStatus.Ready or WorkOrderStatus.Running or WorkOrderStatus.Held)
            {
                foreach (var key in operationKeys) releasedPlanningKeys.Add(key);
            }

            if (workOrder.Status == WorkOrderStatus.Completed)
            {
                foreach (var key in operationKeys)
                {
                    baseline.Remove(key);
                    completed.Add(key);
                }
                continue;
            }

            if (workOrder.Status != WorkOrderStatus.Running || !workOrder.ActualStart.HasValue) continue;
            var activeOperation = operationGroup
                .Where(x => !string.IsNullOrWhiteSpace(x.PlanningKey))
                .OrderBy(x => Math.Abs((x.Start - workOrder.ActualStart.Value).TotalMinutes))
                .FirstOrDefault();
            if (activeOperation?.PlanningKey is null || !baseline.TryGetValue(activeOperation.PlanningKey, out var planned)) continue;

            var duration = planned.EndUtc - planned.StartUtc;
            var expectedEnd = workOrder.ActualEnd ?? workOrder.ActualStart.Value.Add(duration);
            baseline[activeOperation.PlanningKey] = planned with
            {
                ResourceId = workOrder.ResourceId ?? activeOperation.ResourceId,
                StartUtc = workOrder.ActualStart.Value,
                EndUtc = expectedEnd < referenceTimeUtc ? referenceTimeUtc : expectedEnd
            };
            running.Add(activeOperation.PlanningKey);
        }

        var committedFutureSupplies = await BuildCommittedFutureSuppliesAsync(
            baselinePlanVersionId,
            referenceTimeUtc,
            allOperationSnapshots,
            latestHeatEvents,
            releasedPlanningKeys,
            cancellationToken);

        var inventory = await inventoryProvider.GetInventoryAsync(cancellationToken);
        return new ReplanningActualState(
            baseline.Values.OrderBy(x => x.StartUtc).ToArray(),
            inventory,
            completed.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            running.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            committedFutureSupplies);
    }

    private async Task<IReadOnlyCollection<CommittedMaterialSupply>> BuildCommittedFutureSuppliesAsync(
        Guid baselinePlanVersionId,
        DateTime referenceTimeUtc,
        IReadOnlyCollection<PlanOperationSnapshot> operations,
        IReadOnlyCollection<HeatExecutionActual> latestHeatEvents,
        IReadOnlySet<string> releasedPlanningKeys,
        CancellationToken cancellationToken)
    {
        var ledgerJson = await db.PlanVersionStates
            .AsNoTracking()
            .Where(x => x.PlanVersionId == baselinePlanVersionId)
            .Select(x => x.MaterialLedgerJson)
            .SingleOrDefaultAsync(cancellationToken);
        var ledger = DeserializeLedger(ledgerJson)
            .Where(x =>
                x.EventType == MaterialBalanceEventType.PlannedProductionReceipt &&
                x.QuantityDeltaMt > 0m &&
                x.CampaignHeatId.HasValue &&
                x.ProductionOrderId.HasValue)
            .ToArray();
        if (ledger.Length == 0) return Array.Empty<CommittedMaterialSupply>();

        var latestHeatByKey = latestHeatEvents.ToDictionary(x => x.PlanningKey, StringComparer.OrdinalIgnoreCase);
        var ccmByHeatId = operations
            .Where(x => x.ProcessOperationType == ProcessOperationType.Ccm)
            .GroupBy(x => x.SourceEntityId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(x => x.StartUtc).First());
        var result = new List<CommittedMaterialSupply>();

        foreach (var heatGroup in ledger.GroupBy(x => x.CampaignHeatId!.Value))
        {
            var heatId = heatGroup.Key;
            if (!ccmByHeatId.TryGetValue(heatId, out var ccm)) continue;
            if (ccm.ExecutionStatus is OperationExecutionStatus.Completed or OperationExecutionStatus.Cancelled) continue;

            var isCommitted =
                releasedPlanningKeys.Contains(ccm.PlanningKey) ||
                ccm.ExecutionStatus is OperationExecutionStatus.Ready or OperationExecutionStatus.Running or OperationExecutionStatus.Held ||
                ccm.AssignmentCommitmentState is OperationAssignmentCommitmentState.Committed or OperationAssignmentCommitmentState.Running;
            if (!isCommitted) continue;

            var plannedTotal = heatGroup.Sum(x => x.QuantityDeltaMt);
            if (plannedTotal <= 0m) continue;

            latestHeatByKey.TryGetValue(ccm.PlanningKey, out var heatActual);
            var actualQuantity = Math.Max(ccm.ActualQuantityMt, heatActual?.ActualQuantityMt ?? 0m);
            actualQuantity = Math.Clamp(actualQuantity, 0m, plannedTotal);
            var remainingTotal = plannedTotal - actualQuantity;
            if (remainingTotal <= 0m) continue;

            var eta = ResolveReceiptEta(ccm, heatActual, referenceTimeUtc);
            var remainingRatio = remainingTotal / plannedTotal;
            var remainingByPo = heatGroup
                .GroupBy(x => x.ProductionOrderId!.Value)
                .Select(group => new
                {
                    ProductionOrderId = group.Key,
                    PlannedMt = group.Sum(x => x.QuantityDeltaMt),
                    Template = group.OrderBy(x => x.EffectiveAtUtc).First()
                })
                .ToArray();

            decimal allocated = 0m;
            for (var index = 0; index < remainingByPo.Length; index++)
            {
                var item = remainingByPo[index];
                var quantity = index == remainingByPo.Length - 1
                    ? remainingTotal - allocated
                    : decimal.Round(item.PlannedMt * remainingRatio, 4, MidpointRounding.AwayFromZero);
                quantity = Math.Max(0m, quantity);
                allocated += quantity;
                if (quantity <= 0m) continue;

                result.Add(new CommittedMaterialSupply(
                    baselinePlanVersionId,
                    item.ProductionOrderId,
                    heatId,
                    $"BASELINE:{baselinePlanVersionId:N}:HEAT:{heatId:N}:PO:{item.ProductionOrderId:N}",
                    BilletSupplySourceType.InternalCastPlanned,
                    item.Template.MaterialSpecificationCode,
                    item.Template.GradeCode,
                    item.Template.CrossSectionCode,
                    quantity,
                    eta,
                    item.Template.LocationCode));
            }
        }

        return result
            .OrderBy(x => x.AvailableFromUtc)
            .ThenBy(x => x.ProductionOrderId)
            .ThenBy(x => x.SupplyReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DateTime ResolveReceiptEta(
        PlanOperationSnapshot ccm,
        HeatExecutionActual? heatActual,
        DateTime referenceTimeUtc)
    {
        DateTime eta;
        if (ccm.ActualEndUtc.HasValue || heatActual?.ActualEndUtc is not null)
        {
            eta = ccm.ActualEndUtc ?? heatActual!.ActualEndUtc!.Value;
        }
        else if (ccm.ActualStartUtc.HasValue || heatActual?.ActualStartUtc is not null)
        {
            var actualStart = ccm.ActualStartUtc ?? heatActual!.ActualStartUtc!.Value;
            eta = actualStart + (ccm.EndUtc - ccm.StartUtc);
        }
        else
        {
            eta = ccm.EndUtc;
        }

        return eta < referenceTimeUtc ? referenceTimeUtc : eta;
    }

    private static IReadOnlyCollection<MaterialBalanceEvent> DeserializeLedger(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<MaterialBalanceEvent>();
        try
        {
            return JsonSerializer.Deserialize<MaterialBalanceEvent[]>(json, JsonOptions)
                   ?? Array.Empty<MaterialBalanceEvent>();
        }
        catch (JsonException)
        {
            return Array.Empty<MaterialBalanceEvent>();
        }
    }
}
