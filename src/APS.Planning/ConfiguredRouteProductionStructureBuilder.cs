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

        var routeOperations = routePlanning.Operations
            .GroupBy(x => x.RouteCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.SequenceNumber).ToArray(), StringComparer.OrdinalIgnoreCase);

        // Cast-sequence formation and physical caster assignment are split (#16): LogicalCastSequenceProjector
        // forms logical sequences and leaves the physical CCM choice to CP-SAT.
        var casterStructure = LogicalCastSequenceProjector.Apply(
            new ProductionStructurePlanningResult(
                Array.Empty<CastSequence>(),
                Array.Empty<RollingPlan>(),
                Array.Empty<PlannedBilletSupply>(),
                Array.Empty<FiniteScheduleTask>(),
                issues),
            request);
        if (casterStructure.Issues.Any(x => x.Severity == PlanningIssueSeverity.Error)) return casterStructure;

        var rollingPlans = new List<RollingPlan>();
        var finalIssues = casterStructure.Issues.ToList();
        BuildRollingDemandPlans(
            request,
            routeOperations,
            casterStructure.CastSequences,
            rollingPlans,
            finalIssues);

        // In configured-route mode RollingPlan is an allocation/material-demand anchor only. No downstream
        // process task is invented here. MultiStageRouteProjector projects every configured operation after
        // CCM in route order, including the first HotRoll, so HotRoll is no longer an architectural pivot.
        return new ProductionStructurePlanningResult(
            casterStructure.CastSequences,
            rollingPlans,
            casterStructure.PlannedBilletSupplies,
            Array.Empty<FiniteScheduleTask>(),
            finalIssues);
    }

    private static IEnumerable<PlanningIssue> ValidateRouteMaster(RoutePlanningInput input)
    {
        foreach (var route in input.Operations.GroupBy(x => x.RouteCode, StringComparer.OrdinalIgnoreCase))
        {
            var sequenceNumbers = route.Select(x => x.SequenceNumber).ToArray();
            if (sequenceNumbers.Distinct().Count() != sequenceNumbers.Length)
                yield return new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "ROUTE_SEQUENCE_DUPLICATE",
                    $"Route {route.Key} contains duplicate operation sequence numbers.");

            foreach (var operation in route)
            {
                if (operation.YieldPct <= 0m || operation.YieldPct > 100m)
                    yield return new PlanningIssue(
                        PlanningIssueSeverity.Error,
                        "ROUTE_YIELD_INVALID",
                        $"Route {route.Key} operation {operation.SequenceNumber} has invalid yield {operation.YieldPct}.",
                        operation.Id);
                else if (operation.YieldPct != 100m && operation.ProcessOperationType is not (ProcessOperationType.Ccm or ProcessOperationType.HotRoll or ProcessOperationType.ColdRoll))
                    yield return new PlanningIssue(
                        PlanningIssueSeverity.Warning,
                        "ROUTE_YIELD_REQUIRES_MATERIAL_BALANCE",
                        $"Route {route.Key} operation {operation.SequenceNumber} has {operation.YieldPct}% yield; time-phased material balance must carry the loss downstream.",
                        operation.Id);
            }
        }
    }

    private static void BuildRollingDemandPlans(
        ProductionStructurePlanningRequest request,
        IReadOnlyDictionary<string, ManufacturingRouteOperation[]> routeOperations,
        IReadOnlyCollection<CastSequence> castSequences,
        List<RollingPlan> rollingPlans,
        List<PlanningIssue> issues)
    {
        var lines = request.Campaigns
            .SelectMany(campaign => campaign.Allocations
                .Where(x => x.ProductionOrder is not null)
                .SelectMany(allocation => SplitFeed(campaign, allocation)))
            .ToArray();

        var groups = lines
            .GroupBy(line => new RollingDemandGroupKey(
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

            // A route with no HotRoll legitimately terminates at cast intermediate (billet/bloom/slab).
            // Such a route has no RollingPlan and no invented downstream operation.
            var firstHotRoll = operations.FirstOrDefault(x => x.ProcessOperationType == ProcessOperationType.HotRoll);
            if (firstHotRoll is null) continue;

            var inputSection = firstHotRoll.InputCrossSectionCode ?? representative.CasterSectionCode;
            var outputSection = firstHotRoll.OutputCrossSectionCode ?? representative.FinalCrossSectionCode;
            var quantity = groupLines.Sum(x => x.QuantityMt);
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
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "ROLLING_WITHOUT_CAST_SUPPLY",
                    $"Rolling demand {plan.Id} requires fresh {representative.GradeCode} material but no cast sequence produces it.",
                    plan.Id));
        }
    }

    private static IEnumerable<RollingDemandLine> SplitFeed(Campaign campaign, CampaignAllocation allocation)
    {
        var po = allocation.ProductionOrder!;
        if (allocation.ExistingIntermediateInventoryMt > 0m)
            yield return new RollingDemandLine(
                campaign,
                po,
                allocation.ExistingIntermediateInventoryMt,
                allocation.ExistingIntermediateInventoryMt,
                0m,
                false);
        if (allocation.FreshSteelQuantityMt > 0m)
            yield return new RollingDemandLine(
                campaign,
                po,
                allocation.FreshSteelQuantityMt,
                0m,
                allocation.FreshSteelQuantityMt,
                true);
    }

    private static bool HasCastSupply(
        IReadOnlyCollection<CastSequence> sequences,
        IReadOnlyCollection<RollingDemandLine> lines,
        string gradeCode)
    {
        var campaigns = lines.Select(x => x.Campaign.Id).ToHashSet();
        return sequences.Any(sequence => sequence.Heats.Any(heat =>
            campaigns.Contains(heat.CampaignHeat.CampaignId) &&
            Matches(heat.CampaignHeat.GradeCode, gradeCode)));
    }

    private static bool Matches(string? configured, string? actual) =>
        string.IsNullOrWhiteSpace(configured) ||
        string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);

    private static ProductionStructurePlanningResult Empty(IReadOnlyCollection<PlanningIssue> issues) => new(
        Array.Empty<CastSequence>(),
        Array.Empty<RollingPlan>(),
        Array.Empty<PlannedBilletSupply>(),
        Array.Empty<FiniteScheduleTask>(),
        issues);

    private sealed record RollingDemandLine(
        Campaign Campaign,
        ProductionOrder ProductionOrder,
        decimal QuantityMt,
        decimal ExistingIntermediateInventoryMt,
        decimal FreshSteelQuantityMt,
        bool RequiresFreshSteel);

    private sealed record RollingDemandGroupKey(
        string GradeCode,
        string InputCrossSectionCode,
        string FinalCrossSectionCode,
        string RouteCode,
        string? ProductFamilyCode,
        bool RequiresFreshSteel,
        Guid? CampaignPartition);
}
