using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class TimePhasedMaterialPlanner
{
    public static MaterialPlanningResult BuildPreSchedule(
        PlanningRunRequest request,
        CampaignPlanningResult campaignPlan,
        ProductionStructurePlanningResult structure)
    {
        var reservations = new List<MaterialSupplyReservation>();
        var events = new List<ScheduledMaterialEvent>();
        var issues = new List<PlanningIssue>();
        var poById = request.ProductionOrders.ToDictionary(x => x.Id);
        var heatById = campaignPlan.Campaigns.SelectMany(x => x.Heats).ToDictionary(x => x.Id);

        foreach (var allocation in campaignPlan.InventoryAllocations.Where(x =>
                     x.Use is PlanningInventoryUse.IntermediateFeed or PlanningInventoryUse.ExternalIntermediateFeed))
        {
            if (!poById.TryGetValue(allocation.ProductionOrderId, out var po)) continue;
            var pool = PoolKey(po);
            var availability = allocation.AvailableFromUtc ?? request.HorizonStartUtc;
            reservations.Add(new MaterialSupplyReservation
            {
                ProductionOrderId = po.Id,
                MaterialSpecificationCode = allocation.MaterialCode,
                GradeCode = allocation.GradeCode,
                CrossSectionCode = allocation.CrossSectionCode,
                InventoryStage = allocation.Stage,
                ExternalSourceType = allocation.Use == PlanningInventoryUse.ExternalIntermediateFeed
                    ? BilletSupplySourceType.ExternalPurchased
                    : BilletSupplySourceType.ExistingInventory,
                SupplyReference = allocation.SourceReference,
                LocationCode = allocation.LocationCode,
                QuantityMt = allocation.QuantityMt,
                AvailableFromUtc = availability,
                Status = MaterialReservationStatus.Planned
            });
            events.Add(new ScheduledMaterialEvent(
                pool,
                Kg(allocation.QuantityMt),
                ScheduledMaterialEventTiming.FixedTime,
                FixedTimeUtc: availability,
                Explanation: allocation.Use == PlanningInventoryUse.ExternalIntermediateFeed
                    ? $"Confirmed external billet {allocation.SourceReference ?? "supply"}."
                    : "Qualified existing billet inventory.",
                ProductionOrderId: po.Id,
                MaterialCode: allocation.MaterialCode,
                MaterialSpecificationCode: allocation.MaterialCode,
                GradeCode: allocation.GradeCode,
                CrossSectionCode: allocation.CrossSectionCode,
                LocationCode: allocation.LocationCode,
                SupplyReference: allocation.SourceReference,
                LedgerEventType: allocation.Use == PlanningInventoryUse.ExternalIntermediateFeed
                    ? MaterialBalanceEventType.ExternalReceipt
                    : MaterialBalanceEventType.OpeningInventory));
        }

        var ccmTaskByHeat = structure.SchedulingTasks
            .Where(x => x.ProcessOperationType == ProcessOperationType.Ccm || x.TaskType == FiniteScheduleTaskType.Casting)
            .GroupBy(x => x.SourceEntityId)
            .ToDictionary(x => x.Key, x => x.First());
        foreach (var allocation in campaignPlan.HeatAllocations ?? Array.Empty<CampaignHeatAllocation>())
        {
            if (!poById.TryGetValue(allocation.ProductionOrderId, out var po)) continue;
            if (!ccmTaskByHeat.TryGetValue(allocation.CampaignHeatId, out var ccmTask))
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "MATERIAL_CAST_RECEIPT_TASK_MISSING",
                    $"No CCM completion task exists for heat {allocation.CampaignHeatId} supplying {po.ProductionOrderNumber}.",
                    allocation.CampaignHeatId));
                continue;
            }

            events.Add(new ScheduledMaterialEvent(
                PoolKey(po),
                Kg(allocation.PlannedOutputQuantityMt),
                ScheduledMaterialEventTiming.TaskEnd,
                TaskId: ccmTask.TaskId,
                Explanation: $"APS-planned billet receipt from heat {allocation.CampaignHeatId}.",
                ProductionOrderId: po.Id,
                MaterialCode: po.MaterialCode,
                GradeCode: po.GradeCode,
                CrossSectionCode: po.CasterSectionCode,
                CampaignHeatId: allocation.CampaignHeatId,
                LedgerEventType: MaterialBalanceEventType.PlannedProductionReceipt));
        }

        foreach (var rolling in structure.RollingPlans)
        {
            var sourceTasks = structure.SchedulingTasks.Where(x => x.SourceEntityId == rolling.Id).ToArray();
            var feedTasks = sourceTasks.Where(x => x.ProcessOperationType == ProcessOperationType.Reheat).ToArray();
            if (feedTasks.Length == 0)
            {
                feedTasks = sourceTasks.Where(x =>
                    x.ProcessOperationType == ProcessOperationType.HotRoll ||
                    x.TaskType == FiniteScheduleTaskType.HotRolling).ToArray();
            }
            if (feedTasks.Length == 0) continue;

            var totalFeedTaskQuantity = feedTasks.Sum(x => x.QuantityMt);
            if (totalFeedTaskQuantity <= 0m) continue;

            foreach (var allocation in rolling.Allocations.Where(x => x.ProductionOrder is not null))
            {
                var po = allocation.ProductionOrder!;
                decimal assigned = 0m;
                for (var index = 0; index < feedTasks.Length; index++)
                {
                    var task = feedTasks[index];
                    var quantity = index == feedTasks.Length - 1
                        ? allocation.PlannedQuantityMt - assigned
                        : decimal.Round(allocation.PlannedQuantityMt * task.QuantityMt / totalFeedTaskQuantity, 4, MidpointRounding.AwayFromZero);
                    quantity = Math.Max(0m, quantity);
                    assigned += quantity;
                    if (quantity <= 0m) continue;

                    events.Add(new ScheduledMaterialEvent(
                        PoolKey(po),
                        -Kg(quantity),
                        ScheduledMaterialEventTiming.TaskStart,
                        TaskId: task.TaskId,
                        Explanation: $"Billet feed to {task.ProcessOperationType} for rolling plan {rolling.Id}.",
                        ProductionOrderId: po.Id,
                        MaterialCode: po.MaterialCode,
                        GradeCode: po.GradeCode,
                        CrossSectionCode: po.CasterSectionCode,
                        LedgerEventType: MaterialBalanceEventType.PlannedConsumption));
                }
            }
        }

        var supplyActions = BuildPreScheduleSupplyActions(request, campaignPlan, heatById);
        if (supplyActions.Any(x => x.ActionType == MaterialSupplyActionType.Unsourced))
        {
            foreach (var action in supplyActions.Where(x => x.ActionType == MaterialSupplyActionType.Unsourced))
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "MATERIAL_SUPPLY_UNSOURCED",
                    action.Explanation ?? $"No approved source exists for {action.QuantityMt:0.####} MT {action.GradeCode}/{action.CrossSectionCode}.",
                    action.ProductionOrderId));
            }
        }

        return new MaterialPlanningResult(
            reservations,
            events,
            Array.Empty<MaterialBalanceEvent>(),
            issues,
            Array.Empty<MaterialRequirement>(),
            supplyActions);
    }

    public static MaterialPlanningResult ResolveAfterSchedule(
        Guid planVersionId,
        PlanningRunRequest request,
        CampaignPlanningResult campaignPlan,
        MaterialPlanningResult preSchedule,
        FiniteScheduleResult schedule)
    {
        var assignmentByTask = schedule.Assignments.ToDictionary(x => x.TaskId);
        var ledger = new List<MaterialBalanceEvent>();
        var requirements = new List<MaterialRequirement>();
        var issues = preSchedule.Issues.ToList();
        var poById = request.ProductionOrders.ToDictionary(x => x.Id);

        foreach (var scheduled in preSchedule.ScheduleEvents)
        {
            DateTime effective;
            switch (scheduled.Timing)
            {
                case ScheduledMaterialEventTiming.FixedTime:
                    effective = scheduled.FixedTimeUtc ?? request.HorizonStartUtc;
                    break;
                case ScheduledMaterialEventTiming.TaskStart:
                    if (!scheduled.TaskId.HasValue || !assignmentByTask.TryGetValue(scheduled.TaskId.Value, out var startAssignment))
                    {
                        issues.Add(MissingTaskIssue(scheduled));
                        continue;
                    }
                    effective = startAssignment.StartUtc;
                    break;
                case ScheduledMaterialEventTiming.TaskEnd:
                    if (!scheduled.TaskId.HasValue || !assignmentByTask.TryGetValue(scheduled.TaskId.Value, out var endAssignment))
                    {
                        issues.Add(MissingTaskIssue(scheduled));
                        continue;
                    }
                    effective = endAssignment.EndUtc;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            ledger.Add(new MaterialBalanceEvent
            {
                PlanVersionId = planVersionId,
                EventType = scheduled.LedgerEventType ??
                            (scheduled.QuantityDeltaKg >= 0
                                ? MaterialBalanceEventType.PlannedProductionReceipt
                                : MaterialBalanceEventType.PlannedConsumption),
                MaterialPoolKey = scheduled.MaterialPoolKey,
                MaterialSpecificationCode = scheduled.MaterialSpecificationCode,
                GradeCode = scheduled.GradeCode ?? "UNKNOWN",
                CrossSectionCode = scheduled.CrossSectionCode ?? "UNKNOWN",
                LocationCode = scheduled.LocationCode,
                QuantityDeltaMt = scheduled.QuantityDeltaKg / 1000m,
                EffectiveAtUtc = effective,
                TaskId = scheduled.TaskId,
                ProductionOrderId = scheduled.ProductionOrderId,
                CampaignHeatId = scheduled.CampaignHeatId,
                SupplyReference = scheduled.SupplyReference,
                Explanation = scheduled.Explanation
            });
        }

        foreach (var scheduled in preSchedule.ScheduleEvents.Where(x => x.QuantityDeltaKg < 0 && x.ProductionOrderId.HasValue))
        {
            if (!scheduled.TaskId.HasValue || !assignmentByTask.TryGetValue(scheduled.TaskId.Value, out var assignment)) continue;
            if (!poById.TryGetValue(scheduled.ProductionOrderId!.Value, out var po)) continue;
            var required = -scheduled.QuantityDeltaKg / 1000m;
            var poolEvents = ledger
                .Where(x => x.ProductionOrderId == po.Id && x.QuantityDeltaMt > 0m && x.EffectiveAtUtc <= assignment.StartUtc)
                .OrderBy(x => x.EffectiveAtUtc)
                .ToArray();
            var usesFutureSupply = poolEvents.Any(x =>
                x.EventType is MaterialBalanceEventType.PlannedProductionReceipt or MaterialBalanceEventType.ExternalReceipt &&
                x.EffectiveAtUtc > request.HorizonStartUtc);
            var latestReceipt = poolEvents.LastOrDefault()?.EffectiveAtUtc;

            requirements.Add(new MaterialRequirement
            {
                PlanVersionId = planVersionId,
                RequirementKey = $"REQ:{po.Id:N}:{scheduled.TaskId:N}",
                SourceType = MaterialRequirementSourceType.ProcessOperation,
                SourceEntityId = scheduled.TaskId.Value,
                ProductionOrderId = po.Id,
                MaterialCode = po.MaterialCode,
                GradeCode = po.GradeCode,
                CrossSectionCode = po.CasterSectionCode,
                ProductForm = SteelProductForm.Billet,
                RequiredQuantityMt = required,
                RequiredAtUtc = assignment.StartUtc,
                Priority = po.Priority,
                Status = usesFutureSupply ? MaterialRequirementStatus.PlannedAvailable : MaterialRequirementStatus.AvailableNow,
                CoveredQuantityMt = required,
                ShortfallQuantityMt = 0m,
                ExpectedFullyAvailableAtUtc = latestReceipt,
                Explanation = usesFutureSupply
                    ? "Requirement is supplied by qualified future receipts before the scheduled consuming operation."
                    : "Requirement is covered by qualified supply available at the planning-horizon start."
            });
        }

        var balanceIssues = MaterialBalanceValidator.Validate(ledger);
        issues.AddRange(balanceIssues);
        if (balanceIssues.Count > 0)
        {
            foreach (var requirement in requirements.Where(x =>
                         balanceIssues.Any(issue => issue.SourceId == x.ProductionOrderId)))
            {
                requirement.Status = MaterialRequirementStatus.Shortfall;
                requirement.ShortfallQuantityMt = requirement.RequiredQuantityMt;
                requirement.CoveredQuantityMt = 0m;
                requirement.Explanation = "Scheduled material balance becomes negative before this requirement is fully satisfied.";
            }
        }

        var reservations = preSchedule.Reservations.Select(x =>
        {
            x.PlanVersionId = planVersionId;
            return x;
        }).ToArray();

        var firstRequirementByPo = requirements
            .Where(x => x.ProductionOrderId.HasValue)
            .GroupBy(x => x.ProductionOrderId!.Value)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.RequiredAtUtc).First());
        var supplyActions = (preSchedule.SupplyRequirements ?? Array.Empty<MaterialSupplyRequirement>()).Select(action =>
        {
            action.PlanVersionId = planVersionId;
            if (action.ProductionOrderId.HasValue && firstRequirementByPo.TryGetValue(action.ProductionOrderId.Value, out var requirement))
            {
                action.MaterialRequirementId = requirement.Id;
                action.RequiredReceiptUtc = requirement.RequiredAtUtc;
            }
            if (action.UpstreamHeatId.HasValue)
            {
                action.ExpectedReceiptUtc = ledger
                    .Where(x => x.CampaignHeatId == action.UpstreamHeatId &&
                                x.ProductionOrderId == action.ProductionOrderId &&
                                x.EventType == MaterialBalanceEventType.PlannedProductionReceipt)
                    .Select(x => (DateTime?)x.EffectiveAtUtc)
                    .OrderBy(x => x)
                    .FirstOrDefault();
            }
            return action;
        }).ToArray();

        return preSchedule with
        {
            Reservations = reservations,
            LedgerEvents = ledger,
            Issues = issues,
            Requirements = requirements,
            SupplyRequirements = supplyActions
        };
    }

    private static IReadOnlyCollection<MaterialSupplyRequirement> BuildPreScheduleSupplyActions(
        PlanningRunRequest request,
        CampaignPlanningResult campaignPlan,
        IReadOnlyDictionary<Guid, CampaignHeat> heatById)
    {
        var result = new List<MaterialSupplyRequirement>();
        var policy = request.MaterialSupplyPolicy ?? new MaterialSupplyPlanningPolicy();
        var poById = request.ProductionOrders.ToDictionary(x => x.Id);

        foreach (var allocation in campaignPlan.HeatAllocations ?? Array.Empty<CampaignHeatAllocation>())
        {
            if (allocation.PlannedOutputQuantityMt <= 0m || !poById.TryGetValue(allocation.ProductionOrderId, out var po)) continue;
            heatById.TryGetValue(allocation.CampaignHeatId, out var heat);
            var actionType = policy.AllowInternalMake
                ? MaterialSupplyActionType.Make
                : policy.AllowExternalBuy
                    ? MaterialSupplyActionType.Buy
                    : MaterialSupplyActionType.Unsourced;

            result.Add(new MaterialSupplyRequirement
            {
                MaterialRequirementId = Guid.Empty,
                ProductionOrderId = po.Id,
                MaterialCode = po.MaterialCode,
                GradeCode = po.GradeCode,
                CrossSectionCode = po.CasterSectionCode,
                ActionType = actionType,
                QuantityMt = allocation.PlannedOutputQuantityMt,
                RequiredReceiptUtc = po.RequiredDate,
                ExpectedReceiptUtc = null,
                UpstreamCampaignId = heat?.CampaignId,
                UpstreamHeatId = actionType == MaterialSupplyActionType.Make ? allocation.CampaignHeatId : null,
                IsFirm = false,
                Explanation = actionType switch
                {
                    MaterialSupplyActionType.Make =>
                        $"APS-planned internal billet receipt from heat {allocation.CampaignHeatId}; stock need not exist when the campaign is created.",
                    MaterialSupplyActionType.Buy =>
                        $"Internal make is disabled; procurement must provide {allocation.PlannedOutputQuantityMt:0.####} MT qualified billet.",
                    _ =>
                        $"No approved make/buy/transfer path exists for {allocation.PlannedOutputQuantityMt:0.####} MT qualified billet."
                }
            });
        }

        return result;
    }

    private static string PoolKey(ProductionOrder po) =>
        $"PO:{po.Id:N}|GRADE:{po.GradeCode}|SECTION:{po.CasterSectionCode}";

    private static long Kg(decimal mt) => checked((long)Math.Round(mt * 1000m, MidpointRounding.AwayFromZero));

    private static PlanningIssue MissingTaskIssue(ScheduledMaterialEvent materialEvent) => new(
        PlanningIssueSeverity.Error,
        "MATERIAL_EVENT_TASK_NOT_SCHEDULED",
        $"Material event for pool {materialEvent.MaterialPoolKey} references an unscheduled task {materialEvent.TaskId}.",
        materialEvent.TaskId);
}
