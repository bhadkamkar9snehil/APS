using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class ConfiguredRouteProductionStructureBuilder
{
    public static ProductionStructurePlanningResult Build(ProductionStructurePlanningRequest request)
    {
        var routePlanning = request.RoutePlanning
            ?? throw new ArgumentException("RoutePlanning is required for configured-route planning.", nameof(request));
        var issues = ValidateRouteMaster(routePlanning).ToList();
        if (issues.Any(x => x.Severity == PlanningIssueSeverity.Error)) return Empty(issues);

        var resources = request.Resources
            .Where(x => x.IsActive && x.OperatingState is ResourceOperatingState.Available or ResourceOperatingState.CapacityDerated or ResourceOperatingState.QualityRestricted)
            .ToDictionary(x => x.Id);
        var routeCapabilities = routePlanning.ResourceCapabilities.GroupBy(x => x.ResourceId).ToDictionary(x => x.Key, x => x.ToArray());
        var routeOperations = routePlanning.Operations
            .GroupBy(x => x.RouteCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.SequenceNumber).ToArray(), StringComparer.OrdinalIgnoreCase);

        // Cast-sequence formation and physical caster assignment are split (#16): LogicalCastSequenceProjector
        // groups heats into logical sequences and guarantees each has at least one common eligible caster, but
        // leaves CasterResourceId empty - that's a CP-SAT decision, resolved post-solve by
        // ResolvedCastingPlanProjector rather than pre-selected here.
        var casterStructure = LogicalCastSequenceProjector.Apply(
            new ProductionStructurePlanningResult(
                Array.Empty<CastSequence>(), Array.Empty<RollingPlan>(), Array.Empty<PlannedBilletSupply>(), Array.Empty<FiniteScheduleTask>(), issues),
            request);
        if (casterStructure.Issues.Any(x => x.Severity == PlanningIssueSeverity.Error)) return casterStructure;

        var rollingPlans = new List<RollingPlan>();
        var schedulingTasks = new List<FiniteScheduleTask>();
        var finalIssues = casterStructure.Issues.ToList();
        BuildHotRollingPlan(request, resources, routeCapabilities, routeOperations, casterStructure.CastSequences, rollingPlans, schedulingTasks, finalIssues);

        return new ProductionStructurePlanningResult(casterStructure.CastSequences, rollingPlans, casterStructure.PlannedBilletSupplies, schedulingTasks, finalIssues);
    }

    private static IEnumerable<PlanningIssue> ValidateRouteMaster(RoutePlanningInput input)
    {
        foreach (var route in input.Operations.GroupBy(x => x.RouteCode, StringComparer.OrdinalIgnoreCase))
        {
            var sequenceNumbers = route.Select(x => x.SequenceNumber).ToArray();
            if (sequenceNumbers.Distinct().Count() != sequenceNumbers.Length)
                yield return new PlanningIssue(PlanningIssueSeverity.Error, "ROUTE_SEQUENCE_DUPLICATE", $"Route {route.Key} contains duplicate operation sequence numbers.");

            // A route legitimately has no HotRoll operation when the plan sells cast intermediate
            // (billet/bloom/slab) directly rather than rolling it - #34 acceptance scenario 6. HotRoll
            // absence is not itself an error; BuildHotRollingPlan skips such routes rather than erroring.
            foreach (var operation in route)
            {
                if (operation.YieldPct <= 0m || operation.YieldPct > 100m)
                    yield return new PlanningIssue(PlanningIssueSeverity.Error, "ROUTE_YIELD_INVALID", $"Route {route.Key} operation {operation.SequenceNumber} has invalid yield {operation.YieldPct}.", operation.Id);
                else if (operation.YieldPct != 100m && operation.ProcessOperationType is not (ProcessOperationType.Ccm or ProcessOperationType.HotRoll or ProcessOperationType.ColdRoll))
                    yield return new PlanningIssue(PlanningIssueSeverity.Warning, "ROUTE_YIELD_REQUIRES_MATERIAL_BALANCE", $"Route {route.Key} operation {operation.SequenceNumber} has {operation.YieldPct}% yield; time-phased material balance must carry the loss downstream.", operation.Id);
            }
        }
    }

    private static void BuildHotRollingPlan(
        ProductionStructurePlanningRequest request,
        IReadOnlyDictionary<Guid, Resource> resources,
        IReadOnlyDictionary<Guid, RouteResourceCapability[]> capabilities,
        IReadOnlyDictionary<string, ManufacturingRouteOperation[]> routeOperations,
        IReadOnlyCollection<CastSequence> castSequences,
        List<RollingPlan> rollingPlans,
        List<FiniteScheduleTask> schedulingTasks,
        List<PlanningIssue> issues)
    {
        var explicitHotMills = resources.Values.Any(x => x.ProcessUnitType == ProcessUnitType.HotRollingMill);
        var mills = resources.Values
            .Where(x => x.ProcessUnitType == ProcessUnitType.HotRollingMill || (!explicitHotMills && x.ResourceType == ResourceType.RollingMill))
            .ToArray();
        var lines = request.Campaigns
            .SelectMany(campaign => campaign.Allocations.Where(x => x.ProductionOrder is not null).SelectMany(allocation => SplitFeed(campaign, allocation)))
            .ToArray();

        var groups = lines
            .GroupBy(line => new HotRollingGroupKey(
                line.ProductionOrder.GradeCode,
                line.ProductionOrder.CasterSectionCode,
                line.ProductionOrder.FinalCrossSectionCode,
                line.ProductionOrder.RouteCode,
                line.ProductionOrder.ProductFamilyCode,
                line.RequiresFreshSteel,
                request.Policy.AllowCrossCampaignRollingPlans ? null : line.Campaign.Id))
            .OrderBy(x => x.Min(y => y.ProductionOrder.RequiredDate))
            .ThenBy(x => x.Key.GradeCode)
            .ThenBy(x => x.Key.FinalCrossSectionCode)
            .ToArray();

        var planSequence = 0;
        foreach (var group in groups)
        {
            var groupLines = group.OrderByDescending(x => x.ProductionOrder.Priority).ThenBy(x => x.ProductionOrder.RequiredDate).ThenBy(x => x.ProductionOrder.ProductionOrderNumber).ToArray();
            var representative = groupLines[0].ProductionOrder;
            if (!routeOperations.TryGetValue(representative.RouteCode, out var operations))
            {
                issues.Add(new PlanningIssue(PlanningIssueSeverity.Error, "ROUTE_NOT_FOUND", $"No route master exists for {representative.RouteCode}.", representative.Id));
                continue;
            }

            // No HotRoll in this route: the plan sells the cast intermediate directly (billet/bloom/slab),
            // so there is nothing further to schedule here - not an error (#34 acceptance scenario 6).
            var hotOperation = operations.FirstOrDefault(x => x.ProcessOperationType == ProcessOperationType.HotRoll);
            if (hotOperation is null) continue;
            var inputSection = hotOperation.InputCrossSectionCode ?? representative.CasterSectionCode;
            var outputSection = hotOperation.OutputCrossSectionCode ?? representative.FinalCrossSectionCode;
            var quantity = groupLines.Sum(x => x.QuantityMt);
            var fallback = Math.Max(1, (int)Math.Ceiling((double)(quantity / 100m) * request.Policy.DefaultRollingMinutesPer100Mt));

            var options = mills.Select(resource =>
                {
                    var matching = MatchRouteCapabilities(resource, capabilities, representative, ProcessOperationType.HotRoll, inputSection, outputSection);
                    if (matching.Count == 0) return null;
                    var duration = DurationMinutes(quantity, matching.Select(x => x.ThroughputMtPerHour), fallback);
                    var penalty = matching.Select(x => x.AssignmentPenalty).DefaultIfEmpty(0).Min();
                    if (matching.Any(x => x.IsPreferred)) penalty = 0;
                    return new FiniteScheduleResourceOption(resource.Id, duration, penalty);
                })
                .Where(x => x is not null).Cast<FiniteScheduleResourceOption>().ToArray();

            if (options.Length == 0)
            {
                issues.Add(new PlanningIssue(PlanningIssueSeverity.Error, "HOT_MILL_NOT_ELIGIBLE", $"No hot-rolling resource is eligible for {representative.GradeCode} {inputSection}->{outputSection} on route {representative.RouteCode}.", representative.Id));
                continue;
            }

            var distinctCampaigns = groupLines.Select(x => x.Campaign.Id).Distinct().ToArray();
            var distinctPos = groupLines.Select(x => x.ProductionOrder.Id).Distinct().ToArray();
            var plan = new RollingPlan
            {
                CampaignId = distinctCampaigns.Length == 1 ? distinctCampaigns[0] : null,
                ProductionOrderId = distinctPos.Length == 1 ? distinctPos[0] : null,
                RollingMillResourceId = null,
                SequenceNumber = ++planSequence,
                GradeCode = representative.GradeCode,
                InputCrossSectionCode = inputSection,
                OutputCrossSectionCode = outputSection,
                RouteCode = representative.RouteCode,
                PlannedQuantityMt = quantity,
                ExistingIntermediateInventoryMt = groupLines.Sum(x => x.ExistingIntermediateInventoryMt),
                FreshSteelQuantityMt = groupLines.Sum(x => x.FreshSteelQuantityMt)
            };

            foreach (var line in groupLines)
            {
                plan.Allocations.Add(new RollingPlanAllocation
                {
                    RollingPlanId = plan.Id,
                    RollingPlan = plan,
                    CampaignId = line.Campaign.Id,
                    ProductionOrderId = line.ProductionOrder.Id,
                    ProductionOrder = line.ProductionOrder,
                    PlannedQuantityMt = line.QuantityMt,
                    ExistingIntermediateInventoryMt = line.ExistingIntermediateInventoryMt,
                    FreshSteelQuantityMt = line.FreshSteelQuantityMt
                });
            }
            rollingPlans.Add(plan);

            if (group.Key.RequiresFreshSteel && !HasCastSupply(castSequences, groupLines, representative.GradeCode))
                issues.Add(new PlanningIssue(PlanningIssueSeverity.Error, "ROLLING_WITHOUT_CAST_SUPPLY", $"Hot rolling plan {plan.Id} requires fresh {representative.GradeCode} material but no cast sequence produces it.", plan.Id));

            schedulingTasks.Add(new FiniteScheduleTask(
                Guid.NewGuid(),
                plan.Id,
                FiniteScheduleTaskType.HotRolling,
                $"Hot Roll {plan.SequenceNumber} - {plan.GradeCode}/{plan.OutputCrossSectionCode}",
                plan.GradeCode,
                plan.OutputCrossSectionCode,
                plan.PlannedQuantityMt,
                null,
                groupLines.Min(x => x.ProductionOrder.RequiredDate),
                groupLines.Max(x => x.ProductionOrder.Priority),
                options,
                Array.Empty<FiniteScheduleDependency>(),
                ProcessOperationType.HotRoll));
        }
    }

    private static IEnumerable<HotRollingDemandLine> SplitFeed(Campaign campaign, CampaignAllocation allocation)
    {
        var po = allocation.ProductionOrder!;
        if (allocation.ExistingIntermediateInventoryMt > 0m)
            yield return new HotRollingDemandLine(campaign, po, allocation.ExistingIntermediateInventoryMt, allocation.ExistingIntermediateInventoryMt, 0m, false);
        if (allocation.FreshSteelQuantityMt > 0m)
            yield return new HotRollingDemandLine(campaign, po, allocation.FreshSteelQuantityMt, 0m, allocation.FreshSteelQuantityMt, true);
    }

    private static IReadOnlyList<RouteResourceCapability> MatchRouteCapabilities(Resource resource, IReadOnlyDictionary<Guid, RouteResourceCapability[]> capabilities, ProductionOrder po, ProcessOperationType operationType, string inputSection, string outputSection)
    {
        if (!capabilities.TryGetValue(resource.Id, out var values)) return Array.Empty<RouteResourceCapability>();
        return values.Where(x =>
            x.ProcessOperationType == operationType &&
            Matches(x.RouteCode, po.RouteCode) && Matches(x.GradeCode, po.GradeCode) && Matches(x.GradeFamilyCode, po.GradeFamilyCode) && Matches(x.CastingClassCode, po.SteelGrade?.CastingClassCode) &&
            Matches(x.InputCrossSectionCode, inputSection) && Matches(x.OutputCrossSectionCode, outputSection) && Matches(x.ProductFamilyCode, po.ProductFamilyCode)).ToArray();
    }

    private static bool HasCastSupply(IReadOnlyCollection<CastSequence> sequences, IReadOnlyCollection<HotRollingDemandLine> lines, string gradeCode)
    {
        var campaigns = lines.Select(x => x.Campaign.Id).ToHashSet();
        return sequences.Any(sequence => sequence.Heats.Any(heat => campaigns.Contains(heat.CampaignHeat.CampaignId) && Matches(heat.CampaignHeat.GradeCode, gradeCode)));
    }

    private static int DurationMinutes(decimal quantityMt, IEnumerable<decimal?> throughputs, int fallbackMinutes)
    {
        var throughput = throughputs.Where(x => x.HasValue && x.Value > 0m).Select(x => x!.Value).DefaultIfEmpty(0m).Max();
        return throughput <= 0m ? Math.Max(1, fallbackMinutes) : Math.Max(1, (int)Math.Ceiling((double)(quantityMt / throughput * 60m)));
    }

    private static bool Matches(string? configured, string? actual) => string.IsNullOrWhiteSpace(configured) || string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);

    private static ProductionStructurePlanningResult Empty(IReadOnlyCollection<PlanningIssue> issues) => new(
        Array.Empty<CastSequence>(), Array.Empty<RollingPlan>(), Array.Empty<PlannedBilletSupply>(), Array.Empty<FiniteScheduleTask>(), issues);

    private sealed record HotRollingDemandLine(Campaign Campaign, ProductionOrder ProductionOrder, decimal QuantityMt, decimal ExistingIntermediateInventoryMt, decimal FreshSteelQuantityMt, bool RequiresFreshSteel);
    private sealed record HotRollingGroupKey(string GradeCode, string InputCrossSectionCode, string FinalCrossSectionCode, string RouteCode, string? ProductFamilyCode, bool RequiresFreshSteel, Guid? CampaignPartition);
}
