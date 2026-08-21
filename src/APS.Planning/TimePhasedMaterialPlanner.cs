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
        var canonicalMaterialByPo = (request.PrecomputedCampaignMaterialDemand ?? Array.Empty<PrecomputedCampaignMaterialDemand>())
            .ToDictionary(x => x.ProductionOrderId);
        var rollingQuantityByPo = structure.RollingPlans
            .SelectMany(x => x.Allocations)
            .GroupBy(x => x.ProductionOrderId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.PlannedQuantityMt));
        var routePlansByUpstream = (structure.RouteOperationPlans ?? Array.Empty<RouteOperationPlan>())
            .GroupBy(x => x.UpstreamPlanId)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.SequenceNumber).ToArray());

        foreach (var allocation in campaignPlan.InventoryAllocations.Where(x =>
                     x.Use is PlanningInventoryUse.IntermediateFeed or
                         PlanningInventoryUse.ExternalIntermediateFeed or
                         PlanningInventoryUse.CommittedInternalProductionFeed))
        {
            if (!poById.TryGetValue(allocation.ProductionOrderId, out var po)) continue;
            var pool = PoolKey(po);
            var availability = allocation.AvailableFromUtc ?? request.HorizonStartUtc;
            var sourceType = allocation.Use switch
            {
                PlanningInventoryUse.ExternalIntermediateFeed => BilletSupplySourceType.ExternalPurchased,
                PlanningInventoryUse.CommittedInternalProductionFeed => BilletSupplySourceType.InternalCastPlanned,
                _ => BilletSupplySourceType.ExistingInventory
            };
            var explanation = allocation.Use switch
            {
                PlanningInventoryUse.ExternalIntermediateFeed => $"Confirmed external billet {allocation.SourceReference ?? "supply"}.",
                PlanningInventoryUse.CommittedInternalProductionFeed => $"Committed baseline internal production {allocation.SourceReference ?? "supply"}; this is already released/in-process and is not a new MAKE decision.",
                _ => "Qualified existing billet inventory."
            };
            var ledgerType = allocation.Use switch
            {
                PlanningInventoryUse.ExternalIntermediateFeed => MaterialBalanceEventType.ExternalReceipt,
                PlanningInventoryUse.CommittedInternalProductionFeed => MaterialBalanceEventType.PlannedProductionReceipt,
                _ => MaterialBalanceEventType.OpeningInventory
            };

            reservations.Add(new MaterialSupplyReservation
            {
                ProductionOrderId = po.Id,
                MaterialSpecificationCode = allocation.MaterialCode,
                GradeCode = allocation.GradeCode,
                CrossSectionCode = allocation.CrossSectionCode,
                InventoryStage = allocation.Stage,
                ExternalSourceType = sourceType,
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
                Explanation: explanation,
                ProductionOrderId: po.Id,
                MaterialCode: allocation.MaterialCode,
                MaterialSpecificationCode: allocation.MaterialCode,
                GradeCode: allocation.GradeCode,
                CrossSectionCode: allocation.CrossSectionCode,
                LocationCode: allocation.LocationCode,
                SupplyReference: allocation.SourceReference,
                LedgerEventType: ledgerType));
        }

        foreach (var allocation in campaignPlan.PlannedSupplyAllocations ?? Array.Empty<PlanningSupplyAllocation>())
        {
            if (allocation.ActionType is MaterialSupplyActionType.Make or MaterialSupplyActionType.Unsourced) continue;
            if (!poById.TryGetValue(allocation.ProductionOrderId, out var po)) continue;
            if (!allocation.ExpectedReceiptUtc.HasValue)
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "PLANNED_SUPPLY_RECEIPT_TIME_MISSING",
                    $"{allocation.ActionType} supply for {po.ProductionOrderNumber} has no expected receipt time.",
                    po.Id));
                continue;
            }

            var sourceType = allocation.ActionType switch
            {
                MaterialSupplyActionType.Buy => BilletSupplySourceType.ExternalPurchased,
                MaterialSupplyActionType.Transfer => BilletSupplySourceType.InTransit,
                _ => BilletSupplySourceType.ManualPlannerSupply
            };
            var ledgerType = allocation.ActionType switch
            {
                MaterialSupplyActionType.Buy => MaterialBalanceEventType.PlannedPurchaseReceipt,
                MaterialSupplyActionType.Transfer => MaterialBalanceEventType.PlannedTransferReceipt,
                _ => MaterialBalanceEventType.ExternalReceipt
            };

            reservations.Add(new MaterialSupplyReservation
            {
                ProductionOrderId = po.Id,
                MaterialSpecificationCode = po.MaterialCode,
                GradeCode = po.GradeCode,
                CrossSectionCode = po.CasterSectionCode,
                InventoryStage = InventoryStage.InTransit,
                ExternalSourceType = sourceType,
                SupplyReference = allocation.SupplyReference,
                LocationCode = allocation.DestinationLocationCode,
                QuantityMt = allocation.QuantityMt,
                AvailableFromUtc = allocation.ExpectedReceiptUtc.Value,
                Status = MaterialReservationStatus.Planned
            });
            events.Add(new ScheduledMaterialEvent(
                PoolKey(po),
                Kg(allocation.QuantityMt),
                ScheduledMaterialEventTiming.FixedTime,
                FixedTimeUtc: allocation.ExpectedReceiptUtc.Value,
                Explanation: $"Plan-required {allocation.ActionType} billet receipt ({allocation.RuleCode ?? "default sourcing rule"}).",
                ProductionOrderId: po.Id,
                MaterialCode: po.MaterialCode,
                MaterialSpecificationCode: po.MaterialCode,
                GradeCode: po.GradeCode,
                CrossSectionCode: po.CasterSectionCode,
                LocationCode: allocation.DestinationLocationCode,
                SupplyReference: allocation.SupplyReference,
                LedgerEventType: ledgerType));
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
            // #58: billet is consumed by the first actual configured downstream operation, not by a
            // special first-HotRoll task. If Reheat is selected it consumes the billet; if direct hot
            // charge is selected the first HotRoll consumes it; a required pre-roll conditioning step
            // can be the first consumer as well. Compatibility mode falls back to direct RollingPlan tasks.
            FiniteScheduleTask[] feedTasks;
            if (routePlansByUpstream.TryGetValue(rolling.Id, out var firstRoutePlans))
            {
                var firstIds = firstRoutePlans.Select(x => x.Id).ToHashSet();
                feedTasks = structure.SchedulingTasks
                    .Where(x => firstIds.Contains(x.SourceEntityId))
                    .ToArray();
            }
            else
            {
                var sourceTasks = structure.SchedulingTasks.Where(x => x.SourceEntityId == rolling.Id).ToArray();
                feedTasks = sourceTasks.Where(x => x.ProcessOperationType == ProcessOperationType.Reheat).ToArray();
                if (feedTasks.Length == 0)
                {
                    feedTasks = sourceTasks.Where(x =>
                        x.ProcessOperationType == ProcessOperationType.HotRoll ||
                        x.TaskType == FiniteScheduleTaskType.HotRolling).ToArray();
                }
            }
            if (feedTasks.Length == 0) continue;

            var totalFeedTaskQuantity = feedTasks.Sum(x => x.QuantityMt);
            if (totalFeedTaskQuantity <= 0m) continue;

            foreach (var allocation in rolling.Allocations.Where(x => x.ProductionOrder is not null))
            {
                var po = allocation.ProductionOrder!;
                var rollingTotalForPo = rollingQuantityByPo.GetValueOrDefault(po.Id);
                var allocationFeedQuantity = allocation.PlannedQuantityMt;
                if (canonicalMaterialByPo.TryGetValue(po.Id, out var canonical) && rollingTotalForPo > 0m)
                {
                    allocationFeedQuantity = decimal.Round(
                        canonical.SteelFeedRequirementMt * allocation.PlannedQuantityMt / rollingTotalForPo,
                        4,
                        MidpointRounding.AwayFromZero);
                }

                decimal assigned = 0m;
                for (var index = 0; index < feedTasks.Length; index++)
                {
                    var task = feedTasks[index];
                    var quantity = index == feedTasks.Length - 1
                        ? allocationFeedQuantity - assigned
                        : decimal.Round(allocationFeedQuantity * task.QuantityMt / totalFeedTaskQuantity, 4, MidpointRounding.AwayFromZero);
                    quantity = Math.Max(0m, quantity);
                    assigned += quantity;
                    if (quantity <= 0m) continue;

                    events.Add(new ScheduledMaterialEvent(
                        PoolKey(po),
                        -Kg(quantity),
                        ScheduledMaterialEventTiming.TaskStart,
                        TaskId: task.TaskId,
                        Explanation: canonicalMaterialByPo.ContainsKey(po.Id)
                            ? $"Canonical BOM-derived steel feed to {task.ProcessOperationType} for rolling demand {rolling.Id}."
                            : $"Billet feed to {task.ProcessOperationType} for rolling demand {rolling.Id}.",
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
                (x.EventType is MaterialBalanceEventType.PlannedProductionReceipt or
                    MaterialBalanceEventType.ExternalReceipt or
                    MaterialBalanceEventType.PlannedPurchaseReceipt or
                    MaterialBalanceEventType.PlannedTransferReceipt) &&
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
                MaterialUom = "MT",
                GrossQuantity = required,
                CoveredQuantity = required,
                NetRequirementQuantity = 0m,
                ShortfallQuantity = 0m,
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
                requirement.ShortfallQuantity = requirement.RequiredQuantityMt;
                requirement.NetRequirementQuantity = requirement.RequiredQuantityMt;
                requirement.CoveredQuantity = 0m;
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
        var poById = request.ProductionOrders.ToDictionary(x => x.Id);

        foreach (var allocation in campaignPlan.PlannedSupplyAllocations ?? Array.Empty<PlanningSupplyAllocation>())
        {
            if (!poById.TryGetValue(allocation.ProductionOrderId, out var po)) continue;
            result.Add(new MaterialSupplyRequirement
            {
                MaterialRequirementId = Guid.Empty,
                ProductionOrderId = po.Id,
                MaterialCode = po.MaterialCode,
                GradeCode = po.GradeCode,
                CrossSectionCode = po.CasterSectionCode,
                ActionType = allocation.ActionType,
                QuantityMt = allocation.QuantityMt,
                RequiredReceiptUtc = allocation.RequiredReceiptUtc,
                ExpectedReceiptUtc = allocation.ExpectedReceiptUtc,
                SupplyReference = allocation.SupplyReference,
                SupplierCode = allocation.SupplierCode,
                SourceLocationCode = allocation.SourceLocationCode,
                DestinationLocationCode = allocation.DestinationLocationCode,
                IsFirm = allocation.IsFirm,
                Explanation = allocation.ActionType switch
                {
                    MaterialSupplyActionType.Buy => $"Plan requires procurement of {allocation.QuantityMt:0.####} MT qualified billet.",
                    MaterialSupplyActionType.Transfer => $"Plan requires transfer of {allocation.QuantityMt:0.####} MT qualified billet.",
                    MaterialSupplyActionType.Manual => $"Plan uses a planner-authorized supply assumption for {allocation.QuantityMt:0.####} MT qualified billet.",
                    MaterialSupplyActionType.Unsourced => $"No approved source path exists for {allocation.QuantityMt:0.####} MT qualified billet.",
                    _ => null
                }
            });
        }

        foreach (var allocation in campaignPlan.HeatAllocations ?? Array.Empty<CampaignHeatAllocation>())
        {
            if (allocation.PlannedOutputQuantityMt <= 0m || !poById.TryGetValue(allocation.ProductionOrderId, out var po)) continue;
            heatById.TryGetValue(allocation.CampaignHeatId, out var heat);
            result.Add(new MaterialSupplyRequirement
            {
                MaterialRequirementId = Guid.Empty,
                ProductionOrderId = po.Id,
                MaterialCode = po.MaterialCode,
                GradeCode = po.GradeCode,
                CrossSectionCode = po.CasterSectionCode,
                ActionType = MaterialSupplyActionType.Make,
                QuantityMt = allocation.PlannedOutputQuantityMt,
                RequiredReceiptUtc = po.RequiredDate,
                ExpectedReceiptUtc = null,
                UpstreamCampaignId = heat?.CampaignId,
                UpstreamHeatId = allocation.CampaignHeatId,
                IsFirm = false,
                Explanation = $"APS-planned internal billet receipt from heat {allocation.CampaignHeatId}; stock need not exist when the campaign is created."
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
