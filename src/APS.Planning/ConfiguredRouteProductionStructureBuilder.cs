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
        if (issues.Any(x => x.Severity == PlanningIssueSeverity.Error))
        {
            return Empty(issues);
        }

        var resources = request.Resources.Where(x => x.IsActive).ToDictionary(x => x.Id);
        var casterCapabilities = request.Capabilities
            .GroupBy(x => x.ResourceId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var routeCapabilities = routePlanning.ResourceCapabilities
            .GroupBy(x => x.ResourceId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var routeOperations = routePlanning.Operations
            .GroupBy(x => x.RouteCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(y => y.SequenceNumber).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var castSequences = new List<CastSequence>();
        var billetSupplies = new List<PlannedBilletSupply>();
        BuildCasterPlan(
            request,
            resources,
            casterCapabilities,
            castSequences,
            billetSupplies,
            issues);

        var rollingPlans = new List<RollingPlan>();
        var schedulingTasks = new List<FiniteScheduleTask>();
        BuildHotRollingPlan(
            request,
            resources,
            routeCapabilities,
            routeOperations,
            castSequences,
            rollingPlans,
            schedulingTasks,
            issues);

        return new ProductionStructurePlanningResult(
            castSequences,
            rollingPlans,
            billetSupplies,
            schedulingTasks,
            issues);
    }

    private static IEnumerable<PlanningIssue> ValidateRouteMaster(RoutePlanningInput input)
    {
        foreach (var route in input.Operations.GroupBy(x => x.RouteCode, StringComparer.OrdinalIgnoreCase))
        {
            var sequenceNumbers = route.Select(x => x.SequenceNumber).ToArray();
            if (sequenceNumbers.Distinct().Count() != sequenceNumbers.Length)
            {
                yield return new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "ROUTE_SEQUENCE_DUPLICATE",
                    $"Route {route.Key} contains duplicate operation sequence numbers.");
            }

            if (!route.Any(x => x.OperationType == WorkOrderType.HotRolling))
            {
                yield return new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "ROUTE_HOT_ROLLING_MISSING",
                    $"Route {route.Key} does not contain a HotRolling operation.");
            }

            foreach (var operation in route)
            {
                if (operation.YieldPct <= 0m || operation.YieldPct > 100m)
                {
                    yield return new PlanningIssue(
                        PlanningIssueSeverity.Error,
                        "ROUTE_YIELD_INVALID",
                        $"Route {route.Key} operation {operation.SequenceNumber} has invalid yield {operation.YieldPct}.",
                        operation.Id);
                }
                else if (operation.YieldPct != 100m)
                {
                    yield return new PlanningIssue(
                        PlanningIssueSeverity.Error,
                        "DOWNSTREAM_ROUTE_YIELD_NOT_YET_PROPAGATED",
                        $"Route {route.Key} operation {operation.SequenceNumber} specifies {operation.YieldPct}% yield. Downstream route yields must remain 100% until backward quantity propagation is enabled.",
                        operation.Id);
                }
            }
        }
    }

    private static void BuildCasterPlan(
        ProductionStructurePlanningRequest request,
        IReadOnlyDictionary<Guid, Resource> resources,
        IReadOnlyDictionary<Guid, ResourceCapability[]> capabilities,
        List<CastSequence> castSequences,
        List<PlannedBilletSupply> billetSupplies,
        List<PlanningIssue> issues)
    {
        var states = resources.Values
            .Where(x => x.ResourceType == ResourceType.Caster)
            .ToDictionary(x => x.Id, x => new CasterState(x));

        foreach (var item in request.Campaigns
                     .OrderBy(x => x.RequiredDate)
                     .ThenBy(x => x.CampaignNumber)
                     .SelectMany(campaign => campaign.Heats
                         .OrderBy(x => x.SequenceNumber)
                         .Select(heat => (Campaign: campaign, Heat: heat))))
        {
            var family = GradeFamilyFor(item.Campaign, item.Heat.GradeCode);
            var candidates = states.Values
                .Select(state =>
                {
                    var matching = MatchCasterCapabilities(
                        state.Resource,
                        capabilities,
                        item.Campaign.RouteCode,
                        item.Heat.GradeCode,
                        family,
                        item.Campaign.CasterSectionCode);
                    if (matching.Count == 0) return null;
                    if (!TransitionAllowed(
                            request.TransitionRules,
                            state.Resource,
                            TransitionDimension.Grade,
                            state.LastGradeCode,
                            item.Heat.GradeCode))
                    {
                        return null;
                    }

                    var duration = DurationMinutes(
                        item.Heat.PlannedQuantityMt,
                        matching.Select(x => x.ThroughputMtPerHour),
                        request.Policy.DefaultCastingMinutesPerHeat);
                    var append = CanAppend(state, item.Campaign, item.Heat, request);
                    var transitionPenalty = TransitionPenalty(
                        request.TransitionRules,
                        state.Resource,
                        TransitionDimension.Grade,
                        state.LastGradeCode,
                        item.Heat.GradeCode);
                    var score = state.LoadMinutes + transitionPenalty +
                                (append ? 0 : request.Policy.SequenceBreakPenalty);
                    return new CasterCandidate(state, duration, append, score);
                })
                .Where(x => x is not null)
                .Cast<CasterCandidate>()
                .OrderBy(x => x.Score)
                .ThenBy(x => x.State.Resource.Code)
                .ToArray();

            if (candidates.Length == 0)
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "CASTER_NOT_ELIGIBLE",
                    $"No caster is eligible for campaign {item.Campaign.CampaignNumber} heat {item.Heat.SequenceNumber} ({item.Heat.GradeCode}/{item.Campaign.CasterSectionCode}).",
                    item.Heat.Id));
                continue;
            }

            var selected = candidates[0];
            var state = selected.State;
            CastSequence sequence;
            if (!selected.Append || state.CurrentSequence is null)
            {
                sequence = new CastSequence
                {
                    CampaignId = item.Campaign.Id,
                    CasterResourceId = state.Resource.Id,
                    SequenceNumber = ++state.SequenceNumber,
                    CasterSectionCode = item.Campaign.CasterSectionCode,
                    RouteCode = item.Campaign.RouteCode
                };
                state.CurrentSequence = sequence;
                castSequences.Add(sequence);
            }
            else
            {
                sequence = state.CurrentSequence;
                if (sequence.CampaignId != item.Campaign.Id)
                {
                    sequence.CampaignId = null;
                }
            }

            sequence.Heats.Add(new CastSequenceHeat
            {
                CastSequenceId = sequence.Id,
                CastSequence = sequence,
                CampaignHeatId = item.Heat.Id,
                CampaignHeat = item.Heat,
                Position = sequence.Heats.Count + 1
            });
            item.Heat.PreferredCasterResourceId = state.Resource.Id;
            state.LastGradeCode = item.Heat.GradeCode;
            state.LoadMinutes += selected.DurationMinutes;

            billetSupplies.Add(new PlannedBilletSupply(
                item.Campaign.Id,
                item.Heat.Id,
                sequence.Id,
                state.Resource.Id,
                item.Heat.GradeCode,
                item.Campaign.CasterSectionCode,
                ExpectedCastOutputForHeat(item.Campaign, item.Heat)));
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
        var millStates = resources.Values
            .Where(x => x.ResourceType == ResourceType.RollingMill)
            .ToDictionary(x => x.Id, x => new MillState(x));
        var lines = request.Campaigns
            .SelectMany(campaign => campaign.Allocations
                .Where(x => x.ProductionOrder is not null)
                .SelectMany(allocation => SplitFeed(campaign, allocation)))
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

        foreach (var group in groups)
        {
            var groupLines = group
                .OrderByDescending(x => x.ProductionOrder.Priority)
                .ThenBy(x => x.ProductionOrder.RequiredDate)
                .ThenBy(x => x.ProductionOrder.ProductionOrderNumber)
                .ToArray();
            var representative = groupLines[0].ProductionOrder;
            if (!routeOperations.TryGetValue(representative.RouteCode, out var operations))
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "ROUTE_NOT_FOUND",
                    $"No route master exists for {representative.RouteCode}.",
                    representative.Id));
                continue;
            }

            var hotOperation = operations.First(x => x.OperationType == WorkOrderType.HotRolling);
            var inputSection = hotOperation.InputCrossSectionCode ?? representative.CasterSectionCode;
            var outputSection = hotOperation.OutputCrossSectionCode ?? representative.FinalCrossSectionCode;
            var quantity = groupLines.Sum(x => x.QuantityMt);

            var candidates = millStates.Values
                .Select(state =>
                {
                    var matching = MatchRouteCapabilities(
                        state.Resource,
                        capabilities,
                        representative,
                        WorkOrderType.HotRolling,
                        inputSection,
                        outputSection);
                    if (matching.Count == 0) return null;
                    if (!TransitionAllowed(
                            request.TransitionRules,
                            state.Resource,
                            TransitionDimension.Grade,
                            state.LastGradeCode,
                            representative.GradeCode) ||
                        !TransitionAllowed(
                            request.TransitionRules,
                            state.Resource,
                            TransitionDimension.CrossSection,
                            state.LastOutputSectionCode,
                            outputSection))
                    {
                        return null;
                    }

                    var fallback = Math.Max(1, (int)Math.Ceiling(
                        (double)(quantity / 100m) * request.Policy.DefaultRollingMinutesPer100Mt));
                    var duration = DurationMinutes(
                        quantity,
                        matching.Select(x => x.ThroughputMtPerHour),
                        fallback);
                    var score = state.LoadMinutes +
                                TransitionPenalty(request.TransitionRules, state.Resource, TransitionDimension.Grade, state.LastGradeCode, representative.GradeCode) +
                                TransitionPenalty(request.TransitionRules, state.Resource, TransitionDimension.CrossSection, state.LastOutputSectionCode, outputSection);
                    return new MillCandidate(state, duration, score);
                })
                .Where(x => x is not null)
                .Cast<MillCandidate>()
                .OrderBy(x => x.Score)
                .ThenBy(x => x.State.Resource.Code)
                .ToArray();

            if (candidates.Length == 0)
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "HOT_MILL_NOT_ELIGIBLE",
                    $"No hot-rolling resource is eligible for {representative.GradeCode} {inputSection}->{outputSection} on route {representative.RouteCode}.",
                    representative.Id));
                continue;
            }

            var selected = candidates[0];
            var distinctCampaigns = groupLines.Select(x => x.Campaign.Id).Distinct().ToArray();
            var distinctPos = groupLines.Select(x => x.ProductionOrder.Id).Distinct().ToArray();
            var plan = new RollingPlan
            {
                CampaignId = distinctCampaigns.Length == 1 ? distinctCampaigns[0] : null,
                ProductionOrderId = distinctPos.Length == 1 ? distinctPos[0] : null,
                RollingMillResourceId = selected.State.Resource.Id,
                SequenceNumber = ++selected.State.SequenceNumber,
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
            selected.State.LoadMinutes += selected.DurationMinutes;
            selected.State.LastGradeCode = representative.GradeCode;
            selected.State.LastOutputSectionCode = outputSection;

            if (group.Key.RequiresFreshSteel && !HasCastSupply(castSequences, groupLines, representative.GradeCode))
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "ROLLING_WITHOUT_CAST_SUPPLY",
                    $"Hot rolling plan {plan.Id} requires fresh {representative.GradeCode} material but no cast sequence produces it.",
                    plan.Id));
            }

            var due = groupLines.Min(x => x.ProductionOrder.RequiredDate);
            var priority = groupLines.Max(x => x.ProductionOrder.Priority);
            schedulingTasks.Add(new FiniteScheduleTask(
                Guid.NewGuid(),
                plan.Id,
                FiniteScheduleTaskType.HotRolling,
                $"Hot Roll {plan.SequenceNumber} - {plan.GradeCode}/{plan.OutputCrossSectionCode}",
                plan.GradeCode,
                plan.OutputCrossSectionCode,
                plan.PlannedQuantityMt,
                null,
                due,
                priority,
                new[] { new FiniteScheduleResourceOption(selected.State.Resource.Id, selected.DurationMinutes) },
                Array.Empty<FiniteScheduleDependency>()));
        }
    }

    private static IEnumerable<HotRollingDemandLine> SplitFeed(Campaign campaign, CampaignAllocation allocation)
    {
        var po = allocation.ProductionOrder!;
        if (allocation.ExistingIntermediateInventoryMt > 0m)
        {
            yield return new HotRollingDemandLine(
                campaign,
                po,
                allocation.ExistingIntermediateInventoryMt,
                allocation.ExistingIntermediateInventoryMt,
                0m,
                false);
        }

        if (allocation.FreshSteelQuantityMt > 0m)
        {
            yield return new HotRollingDemandLine(
                campaign,
                po,
                allocation.FreshSteelQuantityMt,
                0m,
                allocation.FreshSteelQuantityMt,
                true);
        }
    }

    private static IReadOnlyList<ResourceCapability> MatchCasterCapabilities(
        Resource resource,
        IReadOnlyDictionary<Guid, ResourceCapability[]> capabilities,
        string routeCode,
        string gradeCode,
        string? gradeFamilyCode,
        string outputSection)
    {
        if (!capabilities.TryGetValue(resource.Id, out var values)) return Array.Empty<ResourceCapability>();
        return values.Where(x =>
            Matches(x.RouteCode, routeCode) &&
            (string.IsNullOrWhiteSpace(x.GradeCode) || Matches(x.GradeCode, gradeCode)) &&
            (string.IsNullOrWhiteSpace(x.GradeFamilyCode) || Matches(x.GradeFamilyCode, gradeFamilyCode)) &&
            (string.IsNullOrWhiteSpace(x.OutputCrossSectionCode) || Matches(x.OutputCrossSectionCode, outputSection)))
            .ToArray();
    }

    private static IReadOnlyList<RouteResourceCapability> MatchRouteCapabilities(
        Resource resource,
        IReadOnlyDictionary<Guid, RouteResourceCapability[]> capabilities,
        ProductionOrder po,
        WorkOrderType operationType,
        string inputSection,
        string outputSection)
    {
        if (!capabilities.TryGetValue(resource.Id, out var values)) return Array.Empty<RouteResourceCapability>();
        return values.Where(x =>
            x.OperationType == operationType &&
            Matches(x.RouteCode, po.RouteCode) &&
            (string.IsNullOrWhiteSpace(x.GradeCode) || Matches(x.GradeCode, po.GradeCode)) &&
            (string.IsNullOrWhiteSpace(x.GradeFamilyCode) || Matches(x.GradeFamilyCode, po.GradeFamilyCode)) &&
            (string.IsNullOrWhiteSpace(x.InputCrossSectionCode) || Matches(x.InputCrossSectionCode, inputSection)) &&
            (string.IsNullOrWhiteSpace(x.OutputCrossSectionCode) || Matches(x.OutputCrossSectionCode, outputSection)) &&
            (string.IsNullOrWhiteSpace(x.ProductFamilyCode) || Matches(x.ProductFamilyCode, po.ProductFamilyCode)))
            .ToArray();
    }

    private static bool HasCastSupply(
        IReadOnlyCollection<CastSequence> sequences,
        IReadOnlyCollection<HotRollingDemandLine> lines,
        string gradeCode)
    {
        var campaigns = lines.Select(x => x.Campaign.Id).ToHashSet();
        return sequences.Any(sequence => sequence.Heats.Any(heat =>
            campaigns.Contains(heat.CampaignHeat.CampaignId) &&
            Matches(heat.CampaignHeat.GradeCode, gradeCode)));
    }

    private static bool CanAppend(
        CasterState state,
        Campaign campaign,
        CampaignHeat heat,
        ProductionStructurePlanningRequest request)
    {
        var current = state.CurrentSequence;
        if (current is null || current.Heats.Count >= request.Policy.MaximumHeatsPerCastSequence) return false;
        if (!Matches(current.CasterSectionCode, campaign.CasterSectionCode) || !Matches(current.RouteCode, campaign.RouteCode)) return false;
        if (!request.Policy.AllowCrossCampaignCastSequences && current.CampaignId != campaign.Id) return false;
        return TransitionAllowed(
            request.TransitionRules,
            state.Resource,
            TransitionDimension.Grade,
            state.LastGradeCode,
            heat.GradeCode);
    }

    private static decimal ExpectedCastOutputForHeat(Campaign campaign, CampaignHeat heat)
    {
        var gradeHeats = campaign.Heats
            .Where(x => Matches(x.GradeCode, heat.GradeCode))
            .OrderBy(x => x.SequenceNumber)
            .ToArray();
        var output = campaign.Allocations
            .Where(x => x.ProductionOrder is not null && x.FreshSteelQuantityMt > 0m &&
                        Matches(x.ProductionOrder.GradeCode, heat.GradeCode))
            .Sum(x => x.FreshSteelQuantityMt);
        var input = gradeHeats.Sum(x => x.PlannedQuantityMt);
        if (output <= 0m || input <= 0m) return 0m;

        var index = Array.FindIndex(gradeHeats, x => x.Id == heat.Id);
        if (index < 0) return 0m;
        if (index == gradeHeats.Length - 1)
        {
            var prior = gradeHeats.Take(index)
                .Sum(x => decimal.Round(x.PlannedQuantityMt / input * output, 4, MidpointRounding.AwayFromZero));
            return output - prior;
        }
        return decimal.Round(heat.PlannedQuantityMt / input * output, 4, MidpointRounding.AwayFromZero);
    }

    private static string? GradeFamilyFor(Campaign campaign, string gradeCode) =>
        campaign.Allocations
            .Select(x => x.ProductionOrder)
            .FirstOrDefault(x => x is not null && Matches(x.GradeCode, gradeCode))
            ?.GradeFamilyCode;

    private static int DurationMinutes(
        decimal quantityMt,
        IEnumerable<decimal?> throughputs,
        int fallbackMinutes)
    {
        var throughput = throughputs
            .Where(x => x.HasValue && x.Value > 0m)
            .Select(x => x!.Value)
            .DefaultIfEmpty(0m)
            .Max();
        return throughput <= 0m
            ? Math.Max(1, fallbackMinutes)
            : Math.Max(1, (int)Math.Ceiling((double)(quantityMt / throughput * 60m)));
    }

    private static bool TransitionAllowed(
        IReadOnlyCollection<TransitionRule> rules,
        Resource resource,
        TransitionDimension dimension,
        string? from,
        string to)
    {
        if (string.IsNullOrWhiteSpace(from) || Matches(from, to)) return true;
        return FindTransitionRule(rules, resource, dimension, from, to)?.IsAllowed ?? true;
    }

    private static int TransitionPenalty(
        IReadOnlyCollection<TransitionRule> rules,
        Resource resource,
        TransitionDimension dimension,
        string? from,
        string to)
    {
        if (string.IsNullOrWhiteSpace(from) || Matches(from, to)) return 0;
        return FindTransitionRule(rules, resource, dimension, from, to)?.Penalty ?? 0;
    }

    private static TransitionRule? FindTransitionRule(
        IReadOnlyCollection<TransitionRule> rules,
        Resource resource,
        TransitionDimension dimension,
        string from,
        string to) =>
        rules
            .Where(x => x.Dimension == dimension && Matches(x.FromCode, from) && Matches(x.ToCode, to))
            .OrderByDescending(x => x.ResourceId == resource.Id)
            .ThenByDescending(x => x.ResourceType == resource.ResourceType)
            .FirstOrDefault(x =>
                x.ResourceId == resource.Id ||
                x.ResourceType == resource.ResourceType ||
                (!x.ResourceId.HasValue && !x.ResourceType.HasValue));

    private static bool Matches(string? configured, string? actual) =>
        string.IsNullOrWhiteSpace(configured) ||
        string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);

    private static ProductionStructurePlanningResult Empty(IReadOnlyCollection<PlanningIssue> issues) => new(
        Array.Empty<CastSequence>(),
        Array.Empty<RollingPlan>(),
        Array.Empty<PlannedBilletSupply>(),
        Array.Empty<FiniteScheduleTask>(),
        issues);

    private sealed class CasterState(Resource resource)
    {
        public Resource Resource { get; } = resource;
        public int LoadMinutes { get; set; }
        public int SequenceNumber { get; set; }
        public string? LastGradeCode { get; set; }
        public CastSequence? CurrentSequence { get; set; }
    }

    private sealed record CasterCandidate(CasterState State, int DurationMinutes, bool Append, int Score);

    private sealed class MillState(Resource resource)
    {
        public Resource Resource { get; } = resource;
        public int LoadMinutes { get; set; }
        public int SequenceNumber { get; set; }
        public string? LastGradeCode { get; set; }
        public string? LastOutputSectionCode { get; set; }
    }

    private sealed record MillCandidate(MillState State, int DurationMinutes, int Score);

    private sealed record HotRollingDemandLine(
        Campaign Campaign,
        ProductionOrder ProductionOrder,
        decimal QuantityMt,
        decimal ExistingIntermediateInventoryMt,
        decimal FreshSteelQuantityMt,
        bool RequiresFreshSteel);

    private sealed record HotRollingGroupKey(
        string GradeCode,
        string InputCrossSectionCode,
        string FinalCrossSectionCode,
        string RouteCode,
        string? ProductFamilyCode,
        bool RequiresFreshSteel,
        Guid? CampaignPartition);
}
