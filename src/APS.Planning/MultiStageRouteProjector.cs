using APS.Application;
using APS.Domain;

namespace APS.Planning;

/// <summary>
/// Projects the configured manufacturing route after CCM. There is no special "first HotRoll" path:
/// RollingPlan is only the quantity/allocation anchor, while every physical downstream step (including
/// the first HotRoll) becomes a RouteOperationPlan in configured sequence order.
///
/// Reheat is a route decision, not a fixed topology stage. Fresh/hot feed prefers direct hot charge when
/// the route, order/grade policy, effective thermal state and physical hot-transfer links allow it.
/// Yard/cold feed selects a configured Reheat step before HotRoll; a scheduled direct-hot window that
/// cannot be met is reprojected through that same configured step. Resource commitment/redispatch remains #16.
/// </summary>
internal static class MultiStageRouteProjector
{
    public static ProductionStructurePlanningResult Apply(
        ProductionStructurePlanningResult structure,
        CampaignPlanningResult campaignPlan,
        RoutePlanningInput routePlanning,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyCollection<ResourceCapability> genericCapabilities,
        IReadOnlyCollection<PlantFlowLink>? flowLinks = null,
        IReadOnlyCollection<ExternalMaterialSupply>? externalSupplies = null,
        IReadOnlyCollection<CommittedMaterialSupply>? committedSupplies = null,
        IReadOnlyCollection<GradeProcessTemperatureRequirement>? gradeTemperatureRequirements = null,
        DateTime? thermalReferenceTimeUtc = null,
        IReadOnlySet<string>? forcedThermalReheatRoutes = null)
    {
        var issues = structure.Issues.ToList();
        var tasks = structure.SchedulingTasks.ToList();
        var routePlans = (structure.RouteOperationPlans ?? Array.Empty<RouteOperationPlan>()).ToList();
        var decisions = (structure.RouteOperationDecisions ?? Array.Empty<RouteOperationDecision>()).ToList();
        var thermalDecisions = (structure.BilletThermalDecisions ?? Array.Empty<BilletThermalDecision>()).ToList();
        var links = flowLinks ?? Array.Empty<PlantFlowLink>();

        var activeResources = resources
            .Where(x => x.IsActive && x.OperatingState is
                ResourceOperatingState.Available or
                ResourceOperatingState.CapacityDerated or
                ResourceOperatingState.QualityRestricted)
            .ToDictionary(x => x.Id);
        var routeCapabilities = routePlanning.ResourceCapabilities
            .GroupBy(x => x.ResourceId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var capabilities = genericCapabilities
            .GroupBy(x => x.ResourceId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var operationsByRoute = routePlanning.Operations
            .GroupBy(x => x.RouteCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(y => y.SequenceNumber).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var explicitTopology = resources.Any(x => x.ProcessUnitType != ProcessUnitType.Unknown);

        var inventoryByPo = campaignPlan.InventoryAllocations
            .Where(x => x.Use is
                PlanningInventoryUse.IntermediateFeed or
                PlanningInventoryUse.ExternalIntermediateFeed or
                PlanningInventoryUse.PlannedPurchaseFeed or
                PlanningInventoryUse.PlannedTransferFeed or
                PlanningInventoryUse.ManualPlannedFeed or
                PlanningInventoryUse.CommittedInternalProductionFeed)
            .GroupBy(x => x.ProductionOrderId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var externalByReference = (externalSupplies ?? Array.Empty<ExternalMaterialSupply>())
            .GroupBy(x => x.SupplyReference, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var committedByReference = (committedSupplies ?? Array.Empty<CommittedMaterialSupply>())
            .GroupBy(x => x.SupplyReference, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var castTaskByHeat = tasks
            .Where(x => x.ProcessOperationType == ProcessOperationType.Ccm || x.TaskType == FiniteScheduleTaskType.Casting)
            .GroupBy(x => x.SourceEntityId)
            .ToDictionary(x => x.Key, x => x.First());
        var heatOutput = (campaignPlan.HeatAllocations ?? Array.Empty<CampaignHeatAllocation>())
            .GroupBy(x => x.CampaignHeatId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.PlannedOutputQuantityMt));
        var remainingSupplyByHeat = structure.CastSequences
            .SelectMany(x => x.Heats)
            .Select(x => x.CampaignHeatId)
            .Distinct()
            .ToDictionary(
                heatId => heatId,
                heatId => heatOutput.TryGetValue(heatId, out var output)
                    ? output
                    : structure.PlannedBilletSupplies.Where(x => x.CampaignHeatId == heatId).Sum(x => x.QuantityMt));

        foreach (var rolling in structure.RollingPlans.OrderBy(x => x.SequenceNumber))
        {
            if (!operationsByRoute.TryGetValue(rolling.RouteCode, out var fullRoute))
            {
                issues.Add(Error("ROUTE_NOT_FOUND", $"No route master exists for {rolling.RouteCode}.", rolling.Id));
                continue;
            }

            var ccmIndex = Array.FindIndex(fullRoute, x => x.ProcessOperationType == ProcessOperationType.Ccm);
            var operations = (ccmIndex >= 0 ? fullRoute.Skip(ccmIndex + 1) : fullRoute).ToArray();
            if (operations.Length == 0) continue;

            var orders = rolling.Allocations
                .Where(x => x.ProductionOrder is not null)
                .Select(x => x.ProductionOrder!)
                .DistinctBy(x => x.Id)
                .ToArray();
            if (orders.Length == 0)
            {
                issues.Add(Error("ROUTE_DEMAND_MISSING", $"Rolling demand {rolling.Id} has no Production Order allocations.", rolling.Id));
                continue;
            }

            var casterSections = orders.Select(x => x.CasterSectionCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var finalSections = orders.Select(x => x.FinalCrossSectionCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (casterSections.Length != 1 || finalSections.Length != 1)
            {
                issues.Add(Error(
                    "ROUTE_SECTION_AMBIGUOUS",
                    $"Rolling demand {rolling.Id} contains multiple caster/final cross-sections and cannot be projected as one route chain.",
                    rolling.Id));
                continue;
            }

            var cursors = BuildFeedCursors(
                rolling,
                structure,
                castTaskByHeat,
                remainingSupplyByHeat,
                inventoryByPo,
                externalByReference,
                committedByReference,
                issues);
            if (cursors.Count == 0 || HasSourceError(issues, rolling.Id)) continue;

            var currentSection = casterSections[0];
            var finalSection = finalSections[0];
            var upstreamPlanId = rolling.Id;
            var seenHotRoll = false;

            for (var operationIndex = 0; operationIndex < operations.Length; operationIndex++)
            {
                var operation = operations[operationIndex];
                var effective = ResolveRequirement(operation, orders, issues, rolling.Id);
                if (effective == EffectiveRequirement.Conflict) break;

                if (effective == EffectiveRequirement.Forbidden)
                {
                    decisions.Add(Decision(
                        rolling.Id,
                        rolling.RouteCode,
                        operation,
                        RouteOperationOutcome.SkippedForbidden,
                        "GRADE_OR_ORDER_FORBIDS"));
                    continue;
                }

                var optionalReheatSelected = false;
                string? optionalReheatReason = null;
                if (effective == EffectiveRequirement.Optional)
                {
                    if (operation.ProcessOperationType == ProcessOperationType.Reheat)
                    {
                        optionalReheatReason = OptionalReheatReason(
                            operationIndex,
                            operations,
                            currentSection,
                            rolling,
                            orders,
                            cursors,
                            seenHotRoll,
                            activeResources,
                            routeCapabilities,
                            capabilities,
                            links,
                            gradeTemperatureRequirements ?? Array.Empty<GradeProcessTemperatureRequirement>(),
                            thermalReferenceTimeUtc,
                            forcedThermalReheatRoutes?.Contains(rolling.RouteCode) == true);
                        optionalReheatSelected = optionalReheatReason is not null;
                        if (!optionalReheatSelected)
                        {
                            decisions.Add(Decision(
                                rolling.Id,
                                rolling.RouteCode,
                                operation,
                                RouteOperationOutcome.SkippedOptional,
                                "HOT_CHARGE_PREFERRED"));
                            continue;
                        }
                    }
                    else if (!ChangesTowardDownstreamNeed(operationIndex, operations, operation, currentSection, finalSection))
                    {
                        decisions.Add(Decision(
                            rolling.Id,
                            rolling.RouteCode,
                            operation,
                            RouteOperationOutcome.SkippedOptional,
                            "OPTIONAL_AND_NOT_REQUIRED"));
                        continue;
                    }
                }

                var inputSection = operation.InputCrossSectionCode ?? currentSection;
                var outputSection = ResolveOutputSection(
                    operation,
                    orders,
                    inputSection,
                    currentSection,
                    finalSection,
                    activeResources,
                    routeCapabilities,
                    capabilities);
                if (!Same(inputSection, currentSection))
                {
                    issues.Add(Error(
                        "ROUTE_SECTION_DISCONTINUITY",
                        $"Route {rolling.RouteCode} operation {operation.SequenceNumber} expects {inputSection} but upstream produces {currentSection}.",
                        operation.Id));
                    break;
                }

                if (operation.ProcessOperationType == ProcessOperationType.HotRoll)
                {
                    if (cursors.Any(x => !x.IsKnownHot))
                    {
                        issues.Add(Error(
                            "REHEAT_ROUTE_MISSING",
                            $"Cold/buffered billet feed reaches HotRoll on route {rolling.RouteCode} without an included Reheat operation.",
                            rolling.Id));
                        break;
                    }
                    if (!seenHotRoll &&
                        DirectHotChargeForbidden(orders) &&
                        cursors.Any(x => !x.PassedReheat))
                    {
                        issues.Add(Error(
                            "DIRECT_HOT_CHARGE_FORBIDDEN_REHEAT_REQUIRED",
                            $"Grade/order policy forbids direct hot charge on route {rolling.RouteCode}; a Reheat operation must be configured and included before the first HotRoll.",
                            rolling.Id));
                        break;
                    }
                    if (operation.RequiredChargeMode == ChargeMode.ColdCharge && cursors.Any(x => !x.PassedReheat))
                    {
                        issues.Add(Error(
                            "COLD_CHARGE_REHEAT_REQUIRED",
                            $"HotRoll operation {operation.SequenceNumber} requires cold-charge/reheat preparation but no Reheat operation was included.",
                            operation.Id));
                        break;
                    }
                }

                var eligible = BuildEligibleResources(
                    operation,
                    orders,
                    rolling.PlannedQuantityMt,
                    inputSection,
                    outputSection,
                    activeResources,
                    routeCapabilities,
                    capabilities);
                if (eligible.Count == 0)
                {
                    issues.Add(Error(
                        operation.ProcessOperationType == ProcessOperationType.Reheat
                            ? "REHEAT_RESOURCE_MISSING"
                            : "ROUTE_RESOURCE_NOT_ELIGIBLE",
                        $"No available physical resource can perform {operation.ProcessOperationType} for {rolling.GradeCode} {inputSection}->{outputSection} on route {rolling.RouteCode}.",
                        operation.Id));
                    break;
                }

                var routePlan = NewRoutePlan(rolling, upstreamPlanId, operation, inputSection, outputSection);
                routePlans.Add(routePlan);

                var due = orders.Min(x => x.RequiredDate);
                var priority = orders.Max(x => x.Priority);
                var newTasks = new List<FiniteScheduleTask>(cursors.Count);
                foreach (var cursor in cursors)
                {
                    var options = eligible.Select(x => new FiniteScheduleResourceOption(
                            x.Resource.Id,
                            DurationMinutes(cursor.QuantityMt, x.RouteCapabilities, x.GenericCapabilities, x.Resource),
                            x.AssignmentPenalty,
                            "CONFIGURED_ROUTE_CAPABILITY"))
                        .ToArray();

                    IReadOnlyCollection<FiniteScheduleDependency> dependencies = Array.Empty<FiniteScheduleDependency>();
                    if (cursor.Predecessor is not null)
                    {
                        var directHotTransfer = operation.ProcessOperationType == ProcessOperationType.HotRoll &&
                                                cursor.IsKnownHot &&
                                                !cursor.PassedReheat;
                        dependencies = new[]
                        {
                            BuildDependency(
                                cursor.Predecessor,
                                options,
                                operation,
                                links,
                                explicitTopology,
                                directHotTransfer,
                                issues,
                                rolling.Id)
                        };
                    }

                    newTasks.Add(new FiniteScheduleTask(
                        Guid.NewGuid(),
                        routePlan.Id,
                        MapTaskType(operation.ProcessOperationType),
                        $"{operation.ProcessOperationType} {operation.SequenceNumber} - {rolling.GradeCode}/{outputSection}",
                        rolling.GradeCode,
                        outputSection,
                        cursor.QuantityMt,
                        cursor.Predecessor is null ? cursor.AvailableFromUtc : null,
                        due,
                        priority,
                        options,
                        dependencies,
                        operation.ProcessOperationType));
                }
                tasks.AddRange(newTasks);

                if (operation.ProcessOperationType == ProcessOperationType.HotRoll)
                {
                    var gradeIds = orders
                        .Select(x => x.SteelGrade?.Id)
                        .Where(x => x.HasValue)
                        .Select(x => x!.Value)
                        .ToHashSet();
                    var minimumEntry = (gradeTemperatureRequirements ?? Array.Empty<GradeProcessTemperatureRequirement>())
                        .Where(x =>
                            gradeIds.Contains(x.SteelGradeId) &&
                            x.ProcessOperationType == ProcessOperationType.HotRoll &&
                            x.MinimumEntryTemperatureC.HasValue)
                        .Select(x => x.MinimumEntryTemperatureC!.Value)
                        .DefaultIfEmpty(decimal.MinValue)
                        .Max();
                    for (var cursorIndex = 0; cursorIndex < cursors.Count; cursorIndex++)
                    {
                        var cursor = cursors[cursorIndex];
                        var reheatReason = cursor.ReheatReason;
                        var thermalReheat = IsThermalReheatReason(reheatReason);
                        var policyReheat = reheatReason is not null && !thermalReheat;
                        thermalDecisions.Add(new BilletThermalDecision(
                            rolling.Id,
                            newTasks[cursorIndex].TaskId,
                            cursor.Predecessor?.TaskId,
                            rolling.RouteCode,
                            rolling.GradeCode,
                            currentSection,
                            cursor.ThermalBasis ?? (cursor.PassedReheat
                                ? BilletThermalSourceBasis.UnknownYard
                                : BilletThermalSourceBasis.PlannedCcm),
                            cursor.TemperatureC,
                            cursor.TemperatureObservedOnUtc,
                            minimumEntry == decimal.MinValue ? null : minimumEntry,
                            cursor.PassedReheat ? null : cursor.TemperatureC,
                            null,
                            null,
                            null,
                            cursor.PassedReheat ? BilletThermalOutcome.Reheated : BilletThermalOutcome.HotDirect,
                            reheatReason ?? "HOT_CHARGE_PATH_SELECTED",
                            cursor.PassedReheat ? "CONFIGURED_REHEAT_PATH" : "PENDING_SCHEDULED_TRANSFER",
                            thermalReheat,
                            policyReheat,
                            thermalReheat && reheatReason is not null ? new[] { reheatReason } : Array.Empty<string>(),
                            Array.Empty<string>()));
                    }
                }

                decisions.Add(Decision(
                    routePlan.Id,
                    rolling.RouteCode,
                    operation,
                    RouteOperationOutcome.Included,
                    effective == EffectiveRequirement.Required
                        ? "REQUIRED"
                        : optionalReheatSelected
                            ? optionalReheatReason!
                            : "ROUTE_TRANSFORMATION_REQUIRED"));

                for (var i = 0; i < cursors.Count; i++)
                {
                    cursors[i].Predecessor = newTasks[i];
                    cursors[i].AvailableFromUtc = null;
                    if (operation.ProcessOperationType == ProcessOperationType.Reheat)
                    {
                        cursors[i].IsKnownHot = true;
                        cursors[i].PassedReheat = true;
                        cursors[i].ReheatReason = optionalReheatReason ?? "REHEAT_REQUIRED_BY_ROUTE_OR_POLICY";
                    }
                    else if (operation.ProcessOperationType == ProcessOperationType.HotRoll)
                    {
                        cursors[i].IsKnownHot = true;
                    }
                    if (operation.IsInventoryDecouplingPoint)
                    {
                        // A decoupling point deliberately breaks guaranteed hot continuity. The material
                        // still exists; a later HotRoll must re-establish thermal readiness via Reheat.
                        cursors[i].IsKnownHot = false;
                        cursors[i].PassedReheat = false;
                    }
                }

                upstreamPlanId = routePlan.Id;
                currentSection = outputSection;
                seenHotRoll |= operation.ProcessOperationType == ProcessOperationType.HotRoll;
            }

            if (HasSourceError(issues, rolling.Id)) continue;
            if (!seenHotRoll)
            {
                issues.Add(Error(
                    "ROUTE_HOT_ROLL_NOT_PROJECTED",
                    $"Route {rolling.RouteCode} created rolling demand but no HotRoll operation was projected.",
                    rolling.Id));
            }
            else if (!Same(currentSection, finalSection))
            {
                issues.Add(Error(
                    "ROUTE_FINAL_SECTION_NOT_REACHED",
                    $"Route {rolling.RouteCode} ends at {currentSection} but Production Orders require {finalSection}.",
                    rolling.Id));
            }
        }

        return structure with
        {
            SchedulingTasks = tasks,
            Issues = issues,
            RouteOperationPlans = routePlans,
            RouteOperationDecisions = decisions,
            BilletThermalDecisions = thermalDecisions
        };
    }

    private static RouteOperationPlan NewRoutePlan(
        RollingPlan rolling,
        Guid upstreamPlanId,
        ManufacturingRouteOperation operation,
        string inputSection,
        string outputSection)
    {
        var plan = new RouteOperationPlan
        {
            RouteCode = rolling.RouteCode,
            UpstreamPlanId = upstreamPlanId,
            ProcessOperationType = operation.ProcessOperationType,
            ReleaseWorkOrderType = operation.ReleaseWorkOrderType,
            SequenceNumber = operation.SequenceNumber,
            ResourceId = null,
            GradeCode = rolling.GradeCode,
            InputMaterialSpecificationCode = operation.InputMaterialSpecificationCode,
            OutputMaterialSpecificationCode = operation.OutputMaterialSpecificationCode,
            InputCrossSectionCode = inputSection,
            OutputCrossSectionCode = outputSection,
            PlannedQuantityMt = rolling.PlannedQuantityMt,
            MinimumQueueTime = operation.MinimumQueueTime,
            MaximumQueueTime = operation.MaximumQueueTime,
            IsInventoryDecouplingPoint = operation.IsInventoryDecouplingPoint
        };
        foreach (var allocation in rolling.Allocations)
        {
            plan.Allocations.Add(new RouteOperationPlanAllocation
            {
                RouteOperationPlanId = plan.Id,
                RouteOperationPlan = plan,
                CampaignId = allocation.CampaignId,
                ProductionOrderId = allocation.ProductionOrderId,
                ProductionOrder = allocation.ProductionOrder,
                PlannedQuantityMt = allocation.PlannedQuantityMt
            });
        }
        return plan;
    }

    private static List<FeedCursor> BuildFeedCursors(
        RollingPlan rolling,
        ProductionStructurePlanningResult structure,
        IReadOnlyDictionary<Guid, FiniteScheduleTask> castTaskByHeat,
        IDictionary<Guid, decimal> remainingSupplyByHeat,
        IReadOnlyDictionary<Guid, PlanningInventoryAllocation[]> inventoryByPo,
        IReadOnlyDictionary<string, ExternalMaterialSupply> externalByReference,
        IReadOnlyDictionary<string, CommittedMaterialSupply> committedByReference,
        ICollection<PlanningIssue> issues)
    {
        var result = new List<FeedCursor>();
        if (rolling.FreshSteelQuantityMt > 0m)
        {
            var campaignIds = rolling.Allocations
                .Where(x => x.FreshSteelQuantityMt > 0m)
                .Select(x => x.CampaignId)
                .ToHashSet();
            var heatIds = structure.CastSequences
                .OrderBy(x => x.SequenceNumber)
                .SelectMany(x => x.Heats.OrderBy(y => y.Position))
                .Where(x => campaignIds.Contains(x.CampaignHeat.CampaignId) &&
                            Same(x.CampaignHeat.GradeCode, rolling.GradeCode) &&
                            castTaskByHeat.ContainsKey(x.CampaignHeatId))
                .Select(x => x.CampaignHeatId)
                .Distinct()
                .ToArray();

            var remaining = rolling.FreshSteelQuantityMt;
            foreach (var heatId in heatIds)
            {
                if (remaining <= 0m) break;
                if (!remainingSupplyByHeat.TryGetValue(heatId, out var available) || available <= 0m) continue;
                var quantity = Math.Min(remaining, available);
                remaining -= quantity;
                remainingSupplyByHeat[heatId] = available - quantity;
                result.Add(new FeedCursor(quantity, castTaskByHeat[heatId], null, isKnownHot: true));
            }

            if (remaining > 0.0001m)
            {
                issues.Add(Error(
                    "INSUFFICIENT_PLANNED_CAST_OUTPUT",
                    $"Rolling demand {rolling.Id} requires {rolling.FreshSteelQuantityMt:0.####} MT fresh feed but only {rolling.FreshSteelQuantityMt - remaining:0.####} MT planned cast output is available.",
                    rolling.Id));
            }
            return result;
        }

        var poIds = rolling.Allocations.Select(x => x.ProductionOrderId).ToHashSet();
        var sourceAllocations = poIds
            .SelectMany(id => inventoryByPo.TryGetValue(id, out var values)
                ? values
                : Array.Empty<PlanningInventoryAllocation>())
            .ToArray();
        if (sourceAllocations.Length == 0)
        {
            issues.Add(Error(
                "ROLLING_FEED_SOURCE_MISSING",
                $"Rolling demand {rolling.Id} has no qualified billet source allocation.",
                rolling.Id));
            return result;
        }

        var availability = sourceAllocations
            .Where(x => x.AvailableFromUtc.HasValue)
            .Select(x => x.AvailableFromUtc!.Value)
            .ToArray();
        var availableFrom = availability.Length == 0 ? (DateTime?)null : availability.Max();
        var thermalFacts = sourceAllocations
            .Select(x => ResolveFeedThermalFact(x, externalByReference, committedByReference))
            .ToArray();
        var allKnownHot = thermalFacts.All(x => x.State is ChargeMode.HotDirect or ChargeMode.HotBuffered);
        var numericFacts = thermalFacts.Where(x => x.TemperatureC.HasValue).ToArray();
        var conservativeNumeric = numericFacts.OrderBy(x => x.TemperatureC).FirstOrDefault();
        result.Add(new FeedCursor(
            rolling.PlannedQuantityMt,
            null,
            availableFrom,
            allKnownHot,
            conservativeNumeric?.TemperatureC,
            conservativeNumeric?.Basis,
            conservativeNumeric?.ObservedOnUtc));
        return result;
    }

    private static string? OptionalReheatReason(
        int operationIndex,
        IReadOnlyList<ManufacturingRouteOperation> operations,
        string currentSection,
        RollingPlan rolling,
        IReadOnlyCollection<ProductionOrder> orders,
        IReadOnlyCollection<FeedCursor> cursors,
        bool seenHotRoll,
        IReadOnlyDictionary<Guid, Resource> activeResources,
        IReadOnlyDictionary<Guid, RouteResourceCapability[]> routeCapabilities,
        IReadOnlyDictionary<Guid, ResourceCapability[]> genericCapabilities,
        IReadOnlyCollection<PlantFlowLink> links,
        IReadOnlyCollection<GradeProcessTemperatureRequirement> gradeTemperatureRequirements,
        DateTime? thermalReferenceTimeUtc,
        bool forceThermalRecovery)
    {
        if (RequiresReheat(orders)) return "POLICY_REQUIRES_REHEAT";
        if (!seenHotRoll && DirectHotChargeForbidden(orders)) return "DIRECT_HOT_CHARGE_FORBIDDEN";
        if (forceThermalRecovery) return "SCHEDULED_THERMAL_WINDOW_REQUIRES_REHEAT";

        var gradeIds = orders
            .Select(x => x.SteelGrade?.Id)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToHashSet();
        var rollingRequirements = gradeTemperatureRequirements
            .Where(x => gradeIds.Contains(x.SteelGradeId) && x.ProcessOperationType == ProcessOperationType.HotRoll)
            .ToArray();
        var minimumEntry = rollingRequirements
            .Where(x => x.MinimumEntryTemperatureC.HasValue)
            .Select(x => x.MinimumEntryTemperatureC!.Value)
            .DefaultIfEmpty(decimal.MinValue)
            .Max();
        var maximumHotHold = rollingRequirements
            .Where(x => x.MaximumHoldingMinutesAfterExit.HasValue)
            .Select(x => x.MaximumHoldingMinutesAfterExit!.Value)
            .DefaultIfEmpty(int.MaxValue)
            .Min();

        foreach (var cursor in cursors.Where(x => x.TemperatureC.HasValue))
        {
            if (maximumHotHold != int.MaxValue &&
                thermalReferenceTimeUtc.HasValue &&
                cursor.TemperatureObservedOnUtc.HasValue &&
                thermalReferenceTimeUtc.Value > cursor.TemperatureObservedOnUtc.Value.AddMinutes(maximumHotHold))
                return cursor.ThermalBasis == BilletThermalSourceBasis.ActualMeasurement
                    ? "ACTUAL_THERMAL_STATE_EXPIRED"
                    : "PLANNED_THERMAL_STATE_EXPIRED";
            if (minimumEntry != decimal.MinValue && cursor.TemperatureC!.Value < minimumEntry)
                return cursor.ThermalBasis == BilletThermalSourceBasis.ActualMeasurement
                    ? "ACTUAL_TEMPERATURE_BELOW_ROLLING_MINIMUM"
                    : "PREDICTED_TEMPERATURE_BELOW_ROLLING_MINIMUM";
            if (minimumEntry != decimal.MinValue)
                cursor.IsKnownHot = true;
        }

        if (cursors.Any(x => !x.TemperatureC.HasValue && !x.IsKnownHot))
            return "THERMAL_STATE_REQUIRES_REHEAT";

        var nextHotRoll = operations
            .Skip(operationIndex + 1)
            .FirstOrDefault(x => x.ProcessOperationType == ProcessOperationType.HotRoll);
        if (nextHotRoll is null) return null;
        if (nextHotRoll.RequiredChargeMode == ChargeMode.ColdCharge) return "COLD_CHARGE_REQUIRED";

        var input = nextHotRoll.InputCrossSectionCode ?? currentSection;
        var output = nextHotRoll.OutputCrossSectionCode ?? rolling.OutputCrossSectionCode;
        var eligibleHotRoll = BuildEligibleResources(
            nextHotRoll,
            orders,
            rolling.PlannedQuantityMt,
            input,
            output,
            activeResources,
            routeCapabilities,
            genericCapabilities);
        if (eligibleHotRoll.Count == 0) return null;

        // Inventory/committed hot supply has no producing resource in this run. Its thermal state is the
        // authoritative evidence. Fresh/internal feed has a predecessor and must also have a physical
        // hot-transfer path to at least one eligible mill to bypass reheating.
        foreach (var cursor in cursors.Where(x => x.Predecessor is not null))
        {
            var direct = cursor.Predecessor!.ResourceOptions.Any(from =>
                eligibleHotRoll.Any(to => links.Any(link =>
                    link.IsEnabled &&
                    link.SupportsHotTransfer &&
                    link.FromResourceId == from.ResourceId &&
                    link.ToResourceId == to.Resource.Id &&
                    (!link.FromProcessOperationType.HasValue || link.FromProcessOperationType == cursor.Predecessor.ProcessOperationType) &&
                    (!link.ToProcessOperationType.HasValue || link.ToProcessOperationType == ProcessOperationType.HotRoll))));
            if (!direct) return "DIRECT_HOT_TRANSFER_UNAVAILABLE";
        }
        return null;
    }

    private static IReadOnlyList<EligibleResource> BuildEligibleResources(
        ManufacturingRouteOperation operation,
        IReadOnlyCollection<ProductionOrder> orders,
        decimal plannedQuantityMt,
        string inputSection,
        string outputSection,
        IReadOnlyDictionary<Guid, Resource> resources,
        IReadOnlyDictionary<Guid, RouteResourceCapability[]> routeCapabilities,
        IReadOnlyDictionary<Guid, ResourceCapability[]> genericCapabilities)
    {
        var result = new List<EligibleResource>();
        var requiredResourceIds = orders
            .SelectMany(x => RequiredResourcesFor(x, operation.ProcessOperationType))
            .Distinct()
            .ToArray();
        if (requiredResourceIds.Length > 1) return result;

        var unitType = SteelmakingRouteProjector.UnitTypeFor(operation.ProcessOperationType);
        foreach (var resource in resources.Values)
        {
            if (requiredResourceIds.Length == 1 && resource.Id != requiredResourceIds[0]) continue;

            var routeValues = routeCapabilities.TryGetValue(resource.Id, out var routeAll)
                ? routeAll.Where(x =>
                    x.ProcessOperationType == operation.ProcessOperationType &&
                    orders.All(order =>
                        Matches(x.RouteCode, order.RouteCode) &&
                        Matches(x.GradeCode, order.GradeCode) &&
                        Matches(x.GradeFamilyCode, order.GradeFamilyCode) &&
                        Matches(x.CastingClassCode, order.SteelGrade?.CastingClassCode) &&
                        Matches(x.ProductFamilyCode, order.ProductFamilyCode)) &&
                    Matches(x.InputCrossSectionCode, inputSection) &&
                    Matches(x.OutputCrossSectionCode, outputSection) &&
                    Fits(x.MinimumQuantityMt, x.MaximumQuantityMt, plannedQuantityMt))
                    .ToArray()
                : Array.Empty<RouteResourceCapability>();
            if (routeCapabilities.ContainsKey(resource.Id) && routeValues.Length == 0) continue;

            var genericValues = genericCapabilities.TryGetValue(resource.Id, out var genericAll)
                ? genericAll.Where(x =>
                    (x.ProcessOperationType == operation.ProcessOperationType ||
                     (!x.ProcessOperationType.HasValue && resource.ProcessUnitType == unitType)) &&
                    orders.All(order =>
                        Matches(x.RouteCode, order.RouteCode) &&
                        Matches(x.GradeCode, order.GradeCode) &&
                        Matches(x.GradeFamilyCode, order.GradeFamilyCode) &&
                        Matches(x.CastingClassCode, order.SteelGrade?.CastingClassCode) &&
                        Matches(x.ProductFamilyCode, order.ProductFamilyCode)) &&
                    Matches(x.InputCrossSectionCode, inputSection) &&
                    Matches(x.OutputCrossSectionCode, outputSection) &&
                    Fits(x.MinimumQuantityMt, x.MaximumQuantityMt, plannedQuantityMt))
                    .ToArray()
                : Array.Empty<ResourceCapability>();
            if (genericCapabilities.ContainsKey(resource.Id) && genericValues.Length == 0) continue;

            var hasCapabilityEvidence = routeValues.Length > 0 || genericValues.Length > 0;
            if (!hasCapabilityEvidence && resource.ProcessUnitType != unitType) continue;

            var penalty = routeValues.Select(x => x.AssignmentPenalty)
                .Concat(genericValues.Select(x => x.AssignmentPenalty))
                .DefaultIfEmpty(0)
                .Min();
            if (routeValues.Any(x => x.IsPreferred) || genericValues.Any(x => x.IsPreferred)) penalty = 0;
            result.Add(new EligibleResource(resource, routeValues, genericValues, penalty));
        }
        return result;
    }

    private static string ResolveOutputSection(
        ManufacturingRouteOperation operation,
        IReadOnlyCollection<ProductionOrder> orders,
        string inputSection,
        string currentSection,
        string finalSection,
        IReadOnlyDictionary<Guid, Resource> resources,
        IReadOnlyDictionary<Guid, RouteResourceCapability[]> routeCapabilities,
        IReadOnlyDictionary<Guid, ResourceCapability[]> genericCapabilities)
    {
        if (!string.IsNullOrWhiteSpace(operation.OutputCrossSectionCode))
            return operation.OutputCrossSectionCode;

        var expectedUnit = SteelmakingRouteProjector.UnitTypeFor(operation.ProcessOperationType);
        var outputs = new List<string>();
        foreach (var resource in resources.Values.Where(x => x.ProcessUnitType == expectedUnit))
        {
            if (routeCapabilities.TryGetValue(resource.Id, out var routeValues))
            {
                outputs.AddRange(routeValues
                    .Where(x => x.ProcessOperationType == operation.ProcessOperationType &&
                                orders.All(order => Matches(x.RouteCode, order.RouteCode) &&
                                                    Matches(x.GradeCode, order.GradeCode) &&
                                                    Matches(x.GradeFamilyCode, order.GradeFamilyCode) &&
                                                    Matches(x.ProductFamilyCode, order.ProductFamilyCode)) &&
                                Matches(x.InputCrossSectionCode, inputSection) &&
                                !string.IsNullOrWhiteSpace(x.OutputCrossSectionCode))
                    .Select(x => x.OutputCrossSectionCode!));
            }

            if (genericCapabilities.TryGetValue(resource.Id, out var genericValues))
            {
                outputs.AddRange(genericValues
                    .Where(x => (x.ProcessOperationType == operation.ProcessOperationType || !x.ProcessOperationType.HasValue) &&
                                orders.All(order => Matches(x.RouteCode, order.RouteCode) &&
                                                    Matches(x.GradeCode, order.GradeCode) &&
                                                    Matches(x.GradeFamilyCode, order.GradeFamilyCode) &&
                                                    Matches(x.ProductFamilyCode, order.ProductFamilyCode)) &&
                                Matches(x.InputCrossSectionCode, inputSection) &&
                                !string.IsNullOrWhiteSpace(x.OutputCrossSectionCode))
                    .Select(x => x.OutputCrossSectionCode!));
            }
        }

        var distinct = outputs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return distinct.FirstOrDefault(x => Same(x, finalSection))
               ?? (distinct.Length == 1 ? distinct[0] : currentSection);
    }

    private static FiniteScheduleDependency BuildDependency(
        FiniteScheduleTask predecessor,
        IReadOnlyCollection<FiniteScheduleResourceOption> successorOptions,
        ManufacturingRouteOperation operation,
        IReadOnlyCollection<PlantFlowLink> links,
        bool requirePhysicalPath,
        bool requireHotTransfer,
        ICollection<PlanningIssue> issues,
        Guid sourceId)
    {
        var pairs = new List<FiniteScheduleDependencyResourcePair>();
        foreach (var from in predecessor.ResourceOptions)
            foreach (var to in successorOptions)
            {
                var link = links.FirstOrDefault(x =>
                    x.IsEnabled &&
                    x.FromResourceId == from.ResourceId &&
                    x.ToResourceId == to.ResourceId &&
                    (!x.FromProcessOperationType.HasValue || x.FromProcessOperationType == predecessor.ProcessOperationType) &&
                    (!x.ToProcessOperationType.HasValue || x.ToProcessOperationType == operation.ProcessOperationType) &&
                    (!requireHotTransfer || x.SupportsHotTransfer));
                if (link is null) continue;

                pairs.Add(new FiniteScheduleDependencyResourcePair(
                    from.ResourceId,
                    to.ResourceId,
                    Math.Max(Minutes(operation.MinimumQueueTime), Minutes(link.MinimumTransferTime)),
                    MinNullable(
                        operation.MaximumQueueTime.HasValue ? Minutes(operation.MaximumQueueTime.Value) : null,
                        link.MaximumTransferTime.HasValue ? Minutes(link.MaximumTransferTime.Value) : null)));
            }

        if (pairs.Count > 0) return new FiniteScheduleDependency(predecessor.TaskId, 0, null, pairs);

        if (requireHotTransfer)
        {
            issues.Add(Error(
                "DIRECT_HOT_TRANSFER_UNAVAILABLE",
                $"No enabled hot-transfer path exists from {predecessor.ProcessOperationType} into {operation.ProcessOperationType}; use a configured Reheat/buffer path instead.",
                sourceId));
            return new FiniteScheduleDependency(predecessor.TaskId);
        }
        if (requirePhysicalPath)
        {
            issues.Add(Error(
                "ROUTE_PHYSICAL_FLOW_MISSING",
                $"No enabled physical flow path exists from {predecessor.ProcessOperationType} into {operation.ProcessOperationType}.",
                sourceId));
            return new FiniteScheduleDependency(predecessor.TaskId);
        }

        return new FiniteScheduleDependency(
            predecessor.TaskId,
            Minutes(operation.MinimumQueueTime),
            operation.MaximumQueueTime.HasValue ? Minutes(operation.MaximumQueueTime.Value) : null);
    }

    private static EffectiveRequirement ResolveRequirement(
        ManufacturingRouteOperation operation,
        IReadOnlyCollection<ProductionOrder> orders,
        ICollection<PlanningIssue> issues,
        Guid sourceId)
    {
        var values = new List<RequirementDisposition> { operation.Requirement };
        foreach (var order in orders)
        {
            var grade = order.SteelGrade?.ProcessRequirements
                .FirstOrDefault(x => x.ProcessOperationType == operation.ProcessOperationType)?.Requirement;
            if (grade.HasValue) values.Add(grade.Value);

            if (operation.ProcessOperationType == ProcessOperationType.Reheat && order.Requirement?.RequireReheating == true)
                values.Add(RequirementDisposition.Required);
            if (operation.ProcessOperationType == ProcessOperationType.Tmt && order.Requirement?.RequireTmt == true)
                values.Add(RequirementDisposition.Required);

            values.AddRange(order.Requirement?.ProcessOverrides
                .Where(x => x.ProcessOperationType == operation.ProcessOperationType)
                .Select(x => x.Requirement) ?? Array.Empty<RequirementDisposition>());
        }

        if (values.Contains(RequirementDisposition.Required) && values.Contains(RequirementDisposition.Forbidden))
        {
            issues.Add(Error(
                "DOWNSTREAM_PROCESS_REQUIREMENT_CONFLICT",
                $"Conflicting Required/Forbidden requirements exist for {operation.ProcessOperationType}.",
                sourceId));
            return EffectiveRequirement.Conflict;
        }
        if (values.Contains(RequirementDisposition.Forbidden)) return EffectiveRequirement.Forbidden;
        if (values.Contains(RequirementDisposition.Required)) return EffectiveRequirement.Required;
        return EffectiveRequirement.Optional;
    }

    private static bool RequiresReheat(IEnumerable<ProductionOrder> orders) => orders.Any(order =>
        order.Requirement?.RequireReheating == true ||
        order.SteelGrade?.ProcessRequirements.Any(x =>
            x.ProcessOperationType == ProcessOperationType.Reheat &&
            x.Requirement == RequirementDisposition.Required) == true ||
        order.Requirement?.ProcessOverrides.Any(x =>
            x.ProcessOperationType == ProcessOperationType.Reheat &&
            x.Requirement == RequirementDisposition.Required) == true);

    private static bool DirectHotChargeForbidden(IEnumerable<ProductionOrder> orders) => orders.Any(order =>
        order.SteelGrade?.HotChargeEligible == false ||
        order.Requirement?.ForbidHotCharge == true);

    private static bool ChangesTowardDownstreamNeed(
        int operationIndex,
        IReadOnlyList<ManufacturingRouteOperation> operations,
        ManufacturingRouteOperation operation,
        string currentSection,
        string finalSection)
    {
        if (string.IsNullOrWhiteSpace(operation.OutputCrossSectionCode)) return false;
        if (Same(operation.OutputCrossSectionCode, currentSection)) return false;
        if (Same(operation.OutputCrossSectionCode, finalSection)) return true;
        return operations
            .Skip(operationIndex + 1)
            .Any(next => Same(next.InputCrossSectionCode, operation.OutputCrossSectionCode));
    }

    private static FeedThermalFact ResolveFeedThermalFact(
        PlanningInventoryAllocation allocation,
        IReadOnlyDictionary<string, ExternalMaterialSupply> externalByReference,
        IReadOnlyDictionary<string, CommittedMaterialSupply> committedByReference)
    {
        if (allocation.ThermalState.HasValue || allocation.EstimatedTemperatureC.HasValue)
        {
            return new FeedThermalFact(
                allocation.ThermalState,
                allocation.EstimatedTemperatureC,
                allocation.ThermalBasis ?? BilletThermalSourceBasis.UnknownYard,
                allocation.TemperatureObservedOnUtc ?? allocation.AvailableFromUtc);
        }
        if (allocation.SourceReference is null)
            return new FeedThermalFact(null, null, BilletThermalSourceBasis.UnknownYard, null);
        return allocation.Use switch
        {
            PlanningInventoryUse.ExternalIntermediateFeed =>
                externalByReference.TryGetValue(allocation.SourceReference, out var external)
                    ? new FeedThermalFact(
                        external.ThermalState,
                        external.EstimatedTemperatureC,
                        BilletThermalSourceBasis.CategoricalExternal,
                        external.AvailableFromUtc)
                    : new FeedThermalFact(null, null, BilletThermalSourceBasis.UnknownYard, null),
            PlanningInventoryUse.CommittedInternalProductionFeed =>
                committedByReference.TryGetValue(allocation.SourceReference, out var committed)
                    ? new FeedThermalFact(
                        committed.ThermalState,
                        committed.EstimatedTemperatureC,
                        committed.ThermalBasis ?? BilletThermalSourceBasis.CategoricalCommitted,
                        committed.TemperatureObservedOnUtc ?? committed.AvailableFromUtc)
                    : new FeedThermalFact(null, null, BilletThermalSourceBasis.UnknownYard, null),
            _ => new FeedThermalFact(null, null, BilletThermalSourceBasis.UnknownYard, null)
        };
    }

    private static IEnumerable<Guid> RequiredResourcesFor(ProductionOrder order, ProcessOperationType operationType)
    {
        if (order.Requirement?.RequiredResourceId is { } general) yield return general;
        foreach (var process in order.Requirement?.ProcessOverrides ?? Array.Empty<OrderProcessRequirement>())
            if (process.ProcessOperationType == operationType && process.RequiredResourceId.HasValue)
                yield return process.RequiredResourceId.Value;
    }

    private static RouteOperationDecision Decision(
        Guid sourceEntityId,
        string routeCode,
        ManufacturingRouteOperation operation,
        RouteOperationOutcome outcome,
        string reasonCode) => new(
        sourceEntityId,
        routeCode,
        operation.SequenceNumber,
        operation.ProcessOperationType,
        operation.Requirement,
        outcome,
        reasonCode);

    private static FiniteScheduleTaskType MapTaskType(ProcessOperationType type) => type switch
    {
        ProcessOperationType.Reheat => FiniteScheduleTaskType.Reheating,
        ProcessOperationType.HotRoll => FiniteScheduleTaskType.HotRolling,
        ProcessOperationType.ColdRoll => FiniteScheduleTaskType.ColdRolling,
        ProcessOperationType.Tmt => FiniteScheduleTaskType.Tmt,
        ProcessOperationType.Cool => FiniteScheduleTaskType.Cooling,
        ProcessOperationType.Cut => FiniteScheduleTaskType.Cutting,
        ProcessOperationType.Bundle => FiniteScheduleTaskType.Bundling,
        ProcessOperationType.Coil => FiniteScheduleTaskType.Coiling,
        ProcessOperationType.Finish => FiniteScheduleTaskType.Finishing,
        _ => FiniteScheduleTaskType.Finishing
    };

    private static int DurationMinutes(
        decimal quantityMt,
        IReadOnlyCollection<RouteResourceCapability> routeCapabilities,
        IReadOnlyCollection<ResourceCapability> genericCapabilities,
        Resource resource)
    {
        var fixedDuration = routeCapabilities
            .Where(x => x.FixedDurationMinutes.HasValue)
            .Select(x => x.FixedDurationMinutes!.Value)
            .Concat(genericCapabilities.Where(x => x.FixedDurationMinutes.HasValue).Select(x => x.FixedDurationMinutes!.Value))
            .DefaultIfEmpty(resource.NominalResidenceMinutes ?? 0)
            .Max();
        if (fixedDuration > 0) return fixedDuration;

        var throughput = routeCapabilities
            .Where(x => x.ThroughputMtPerHour.HasValue && x.ThroughputMtPerHour.Value > 0m)
            .Select(x => x.ThroughputMtPerHour!.Value)
            .Concat(genericCapabilities.Where(x => x.ThroughputMtPerHour.HasValue && x.ThroughputMtPerHour.Value > 0m).Select(x => x.ThroughputMtPerHour!.Value))
            .Append(resource.NominalThroughputMtPerHour ?? 0m)
            .DefaultIfEmpty(0m)
            .Max();
        return throughput <= 0m
            ? 60
            : Math.Max(1, (int)Math.Ceiling((double)(quantityMt / throughput * 60m)));
    }

    private static bool Matches(string? configured, string? actual) =>
        string.IsNullOrWhiteSpace(configured) || Same(configured, actual);
    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool Fits(decimal? minimum, decimal? maximum, decimal quantity) =>
        (!minimum.HasValue || quantity >= minimum.Value) && (!maximum.HasValue || quantity <= maximum.Value);
    private static int Minutes(TimeSpan value) => Math.Max(0, (int)Math.Ceiling(value.TotalMinutes));
    private static int? MinNullable(int? first, int? second) =>
        !first.HasValue ? second : !second.HasValue ? first : Math.Min(first.Value, second.Value);
    private static bool HasSourceError(IEnumerable<PlanningIssue> issues, Guid sourceId) =>
        issues.Any(x => x.Severity == PlanningIssueSeverity.Error && x.SourceId == sourceId);
    private static PlanningIssue Error(string code, string message, Guid sourceId) =>
        new(PlanningIssueSeverity.Error, code, message, sourceId);

    private static bool IsThermalReheatReason(string? reason) => reason is
        "ACTUAL_THERMAL_STATE_EXPIRED" or
        "PLANNED_THERMAL_STATE_EXPIRED" or
        "ACTUAL_TEMPERATURE_BELOW_ROLLING_MINIMUM" or
        "PREDICTED_TEMPERATURE_BELOW_ROLLING_MINIMUM" or
        "SCHEDULED_THERMAL_WINDOW_REQUIRES_REHEAT" or
        "THERMAL_STATE_REQUIRES_REHEAT" or
        "DIRECT_HOT_TRANSFER_UNAVAILABLE";

    private sealed record EligibleResource(
        Resource Resource,
        IReadOnlyCollection<RouteResourceCapability> RouteCapabilities,
        IReadOnlyCollection<ResourceCapability> GenericCapabilities,
        int AssignmentPenalty);

    private sealed class FeedCursor(
        decimal quantityMt,
        FiniteScheduleTask? predecessor,
        DateTime? availableFromUtc,
        bool isKnownHot,
        decimal? temperatureC = null,
        BilletThermalSourceBasis? thermalBasis = null,
        DateTime? temperatureObservedOnUtc = null)
    {
        public decimal QuantityMt { get; } = quantityMt;
        public FiniteScheduleTask? Predecessor { get; set; } = predecessor;
        public DateTime? AvailableFromUtc { get; set; } = availableFromUtc;
        public bool IsKnownHot { get; set; } = isKnownHot;
        public decimal? TemperatureC { get; } = temperatureC;
        public BilletThermalSourceBasis? ThermalBasis { get; } = thermalBasis;
        public DateTime? TemperatureObservedOnUtc { get; } = temperatureObservedOnUtc;
        public bool PassedReheat { get; set; }
        public string? ReheatReason { get; set; }
    }

    private sealed record FeedThermalFact(
        ChargeMode? State,
        decimal? TemperatureC,
        BilletThermalSourceBasis Basis,
        DateTime? ObservedOnUtc);

    private enum EffectiveRequirement
    {
        Optional = 0,
        Required = 1,
        Forbidden = 2,
        Conflict = 3
    }
}
