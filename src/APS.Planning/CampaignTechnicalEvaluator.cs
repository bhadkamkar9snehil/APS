using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal sealed record CampaignTechnicalEvaluation(
    bool IsFeasible,
    decimal GradeTransitionCost,
    decimal HeatTargetDeviationMt,
    IReadOnlyList<string> GradeSequence,
    string? Reason = null,
    bool HasFurnaceEvaluation = false)
{
    public static CampaignTechnicalEvaluation Neutral { get; } = new(true, 0m, 0m, Array.Empty<string>());
}

/// <summary>
/// Technical scoring gate for #15. Campaign composition is allowed to trade service against economic
/// efficiency, but it is not allowed to choose a grouping whose fresh-steel heat structure, configured
/// downstream route, or grade sequence is physically impossible. The evaluator deliberately reuses the
/// canonical heat builder and the already-materialized transition-rule snapshot supplied by PlanningEngine.
/// It is a feasibility/scoring signal, not a second finite scheduler.
/// </summary>
internal static class CampaignTechnicalEvaluator
{
    public static CampaignTechnicalEvaluation Evaluate(
        CampaignCandidate candidate,
        IReadOnlyDictionary<Guid, ProductionOrder> ordersById,
        IReadOnlyDictionary<Guid, decimal> rollingRequirementsMt,
        IReadOnlyDictionary<Guid, decimal> freshSteelRequirementsMt,
        CampaignPlanningRequest request)
    {
        if (candidate.Slices.Count == 0) return CampaignTechnicalEvaluation.Neutral;

        var representative = ordersById[candidate.Slices[0].Requirement.ProductionOrderId];
        var campaign = new Campaign
        {
            CampaignNumber = "CANDIDATE",
            GradeSequenceClassCode = representative.SteelGrade?.SequenceClassCode
                                     ?? representative.GradeSequenceClassCode
                                     ?? $"GRADE:{representative.GradeCode}",
            CasterSectionCode = representative.CasterSectionCode,
            RouteCode = representative.RouteCode,
            RequiredDate = candidate.RequiredDate,
            Status = CampaignStatus.Draft
        };

        foreach (var slice in candidate.Slices)
        {
            if (!ordersById.TryGetValue(slice.Requirement.ProductionOrderId, out var po))
                return Reject($"Production Order {slice.Requirement.ProductionOrderNumber} was not available for candidate evaluation.");

            var rolling = rollingRequirementsMt.TryGetValue(po.Id, out var r) ? r : 0m;
            var fresh = freshSteelRequirementsMt.TryGetValue(po.Id, out var f) ? f : 0m;
            var freshRatio = rolling <= 0m ? 0m : Math.Clamp(fresh / rolling, 0m, 1m);
            var freshSlice = decimal.Round(slice.QuantityMt * freshRatio, 4, MidpointRounding.AwayFromZero);

            campaign.Allocations.Add(new CampaignAllocation
            {
                CampaignId = campaign.Id,
                Campaign = campaign,
                ProductionOrderId = po.Id,
                ProductionOrder = po,
                PlannedQuantityMt = slice.QuantityMt,
                ExistingIntermediateInventoryMt = Math.Max(0m, slice.QuantityMt - freshSlice),
                FreshSteelQuantityMt = freshSlice
            });
            campaign.PlannedQuantityMt += slice.QuantityMt;
            campaign.ExistingIntermediateInventoryMt += Math.Max(0m, slice.QuantityMt - freshSlice);
            campaign.FreshSteelRequirementMt += freshSlice;
        }

        var route = ValidateRequiredDownstreamResources(candidate, ordersById, request);
        if (!route.IsFeasible) return route;

        var furnaceEvaluated = false;
        if (campaign.FreshSteelRequirementMt > 0m && request.Resources is { Count: > 0 })
        {
            furnaceEvaluated = true;
            try
            {
                CanonicalCampaignHeatBuilder.Rebuild(campaign, request);
            }
            catch (InvalidOperationException ex)
            {
                return Reject(ex.Message);
            }
        }

        var heatTargetDeviation = campaign.Heats.Sum(heat =>
            Math.Abs(heat.PlannedQuantityMt - (heat.TargetQuantityMt ?? heat.PlannedQuantityMt)));

        var grades = campaign.GradeSequence
            .OrderBy(x => x.SequenceNumber)
            .Select(x => x.GradeCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (grades.Length == 0)
        {
            grades = candidate.Slices
                .Select(x => ordersById[x.Requirement.ProductionOrderId].GradeCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var sequence = OptimizeGradeSequence(grades, request.TransitionRules ?? Array.Empty<TransitionRule>());
        if (!sequence.IsFeasible) return Reject(sequence.Reason ?? "No allowed grade sequence exists for the campaign.");

        return new CampaignTechnicalEvaluation(
            true,
            sequence.Cost,
            decimal.Round(heatTargetDeviation, 4, MidpointRounding.AwayFromZero),
            sequence.Grades,
            null,
            furnaceEvaluated);
    }

    /// <summary>
    /// Applies the exact grade ordering used during candidate scoring to the materialized Campaign so
    /// the persisted grade sequence/heats cannot silently differ from the objective that selected it.
    /// </summary>
    public static void ApplySelectedGradeSequence(Campaign campaign, CampaignTechnicalEvaluation evaluation)
    {
        if (!evaluation.IsFeasible || evaluation.GradeSequence.Count == 0) return;

        var rank = evaluation.GradeSequence
            .Select((grade, index) => (grade, index))
            .ToDictionary(x => x.grade, x => x.index, StringComparer.OrdinalIgnoreCase);

        var gradeRows = campaign.GradeSequence
            .OrderBy(x => rank.TryGetValue(x.GradeCode, out var value) ? value : int.MaxValue)
            .ThenBy(x => x.SequenceNumber)
            .ToArray();
        campaign.GradeSequence.Clear();
        for (var i = 0; i < gradeRows.Length; i++)
        {
            gradeRows[i].SequenceNumber = i + 1;
            campaign.GradeSequence.Add(gradeRows[i]);
        }

        var heatRows = campaign.Heats
            .OrderBy(x => rank.TryGetValue(x.GradeCode, out var value) ? value : int.MaxValue)
            .ThenBy(x => x.SequenceNumber)
            .ToArray();
        campaign.Heats.Clear();
        for (var i = 0; i < heatRows.Length; i++)
        {
            heatRows[i].SequenceNumber = i + 1;
            campaign.Heats.Add(heatRows[i]);
        }
    }

    private static CampaignTechnicalEvaluation ValidateRequiredDownstreamResources(
        CampaignCandidate candidate,
        IReadOnlyDictionary<Guid, ProductionOrder> ordersById,
        CampaignPlanningRequest request)
    {
        if (request.RoutePlanning is null || request.Resources is not { Count: > 0 })
            return CampaignTechnicalEvaluation.Neutral;

        foreach (var po in candidate.Slices
                     .Select(x => ordersById[x.Requirement.ProductionOrderId])
                     .DistinctBy(x => x.Id))
        {
            var route = request.RoutePlanning.Operations
                .Where(x => string.Equals(x.RouteCode, po.RouteCode, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.SequenceNumber)
                .ToArray();
            var ccmSequence = route.FirstOrDefault(x => x.ProcessOperationType == ProcessOperationType.Ccm)?.SequenceNumber;
            if (!ccmSequence.HasValue) continue;

            var currentSection = po.CasterSectionCode;
            foreach (var operation in route.Where(x => x.SequenceNumber > ccmSequence.Value))
            {
                var disposition = ResolveRequirement(operation, po);
                if (disposition == RequirementDisposition.Forbidden) continue;

                var changesTowardFinal = disposition == RequirementDisposition.Optional &&
                                         !string.IsNullOrWhiteSpace(operation.OutputCrossSectionCode) &&
                                         string.Equals(operation.OutputCrossSectionCode, po.FinalCrossSectionCode, StringComparison.OrdinalIgnoreCase) &&
                                         !string.Equals(currentSection, po.FinalCrossSectionCode, StringComparison.OrdinalIgnoreCase);
                if (disposition != RequirementDisposition.Required && !changesTowardFinal) continue;

                var inputSection = operation.InputCrossSectionCode ?? currentSection;
                var outputSection = operation.OutputCrossSectionCode ?? currentSection;
                if (!string.Equals(inputSection, currentSection, StringComparison.OrdinalIgnoreCase))
                {
                    return Reject(
                        $"Route {po.RouteCode} operation {operation.SequenceNumber} expects {inputSection} but the candidate produces {currentSection}.");
                }

                if (!HasEligibleResource(operation, po, inputSection, outputSection, request))
                {
                    return Reject(
                        $"No active eligible {operation.ProcessOperationType} resource exists for {po.ProductionOrderNumber} " +
                        $"({inputSection}->{outputSection}) on route {po.RouteCode}.");
                }

                currentSection = outputSection;
            }

            if (!string.Equals(currentSection, po.FinalCrossSectionCode, StringComparison.OrdinalIgnoreCase) &&
                route.Any(x => x.SequenceNumber > ccmSequence.Value))
            {
                return Reject(
                    $"Configured route {po.RouteCode} ends candidate projection at {currentSection} but {po.ProductionOrderNumber} requires {po.FinalCrossSectionCode}.");
            }
        }

        return CampaignTechnicalEvaluation.Neutral;
    }

    private static RequirementDisposition ResolveRequirement(ManufacturingRouteOperation operation, ProductionOrder po)
    {
        var values = new List<RequirementDisposition> { operation.Requirement };
        var grade = po.SteelGrade?.ProcessRequirements.FirstOrDefault(x => x.ProcessOperationType == operation.ProcessOperationType)?.Requirement;
        if (grade.HasValue) values.Add(grade.Value);
        var orderValues = po.Requirement?.ProcessOverrides
            .Where(x => x.ProcessOperationType == operation.ProcessOperationType)
            .Select(x => x.Requirement)
            .ToArray() ?? Array.Empty<RequirementDisposition>();
        values.AddRange(orderValues);
        if (operation.ProcessOperationType == ProcessOperationType.Reheat && po.Requirement?.RequireReheating == true)
            values.Add(RequirementDisposition.Required);
        if (operation.ProcessOperationType == ProcessOperationType.Tmt && po.Requirement?.RequireTmt == true)
            values.Add(RequirementDisposition.Required);

        // Required and Forbidden is a conflict; treat it as technically impossible here rather than
        // quietly choosing one side. Returning Required guarantees the resource/path is not skipped;
        // the canonical structure validator still emits the detailed conflict diagnostic later.
        if (values.Contains(RequirementDisposition.Required) && values.Contains(RequirementDisposition.Forbidden))
            return RequirementDisposition.Required;
        if (values.Contains(RequirementDisposition.Forbidden)) return RequirementDisposition.Forbidden;
        if (values.Contains(RequirementDisposition.Required)) return RequirementDisposition.Required;
        return RequirementDisposition.Optional;
    }

    private static bool HasEligibleResource(
        ManufacturingRouteOperation operation,
        ProductionOrder po,
        string inputSection,
        string outputSection,
        CampaignPlanningRequest request)
    {
        var routeCapabilities = request.RoutePlanning?.ResourceCapabilities
            .Where(x => x.ProcessOperationType == operation.ProcessOperationType &&
                        Matches(x.RouteCode, po.RouteCode) &&
                        Matches(x.GradeCode, po.GradeCode) &&
                        Matches(x.GradeFamilyCode, po.GradeFamilyCode) &&
                        Matches(x.CastingClassCode, po.SteelGrade?.CastingClassCode) &&
                        Matches(x.ProductFamilyCode, po.ProductFamilyCode))
            .ToArray() ?? Array.Empty<RouteResourceCapability>();
        var genericCapabilities = request.ResourceCapabilities ?? Array.Empty<ResourceCapability>();
        var requiredResourceIds = RequiredResourceIds(po, operation.ProcessOperationType);
        var requiredCapabilityClass = RequiredCapabilityClass(po, operation);

        foreach (var resource in request.Resources ?? Array.Empty<Resource>())
        {
            if (!resource.IsActive ||
                resource.OperatingState is not (ResourceOperatingState.Available or ResourceOperatingState.CapacityDerated or ResourceOperatingState.QualityRestricted))
                continue;
            if (requiredResourceIds.Count > 0 && !requiredResourceIds.Contains(resource.Id)) continue;
            if (resource.ProcessUnitType != ProcessUnitType.Unknown &&
                resource.ProcessUnitType != SteelmakingRouteProjector.UnitTypeFor(operation.ProcessOperationType))
                continue;

            var routeForResource = routeCapabilities.Where(x => x.ResourceId == resource.Id).ToArray();
            if (routeCapabilities.Length > 0 && routeForResource.Length == 0) continue;
            if (routeForResource.Length > 0 &&
                !routeForResource.Any(x =>
                    CrossSectionCapabilityMatcher.Matches(x, inputSection, outputSection, request.RoutePlanning?.CrossSections) &&
                    (string.IsNullOrWhiteSpace(requiredCapabilityClass) || Matches(x.CapabilityClassCode, requiredCapabilityClass))))
                continue;

            var genericForResource = genericCapabilities.Where(x => x.ResourceId == resource.Id).ToArray();
            if (genericForResource.Length > 0)
            {
                var matchingGeneric = genericForResource.Where(x =>
                        (!x.ProcessOperationType.HasValue || x.ProcessOperationType == operation.ProcessOperationType) &&
                        Matches(x.RouteCode, po.RouteCode) &&
                        Matches(x.GradeCode, po.GradeCode) &&
                        Matches(x.GradeFamilyCode, po.GradeFamilyCode) &&
                        Matches(x.CastingClassCode, po.SteelGrade?.CastingClassCode) &&
                        Matches(x.InputCrossSectionCode, inputSection) &&
                        Matches(x.OutputCrossSectionCode, outputSection) &&
                        Matches(x.ProductFamilyCode, po.ProductFamilyCode) &&
                        (string.IsNullOrWhiteSpace(requiredCapabilityClass) || Matches(x.CapabilityClassCode, requiredCapabilityClass)))
                    .ToArray();
                if (matchingGeneric.Length == 0) continue;
            }
            else if (!string.IsNullOrWhiteSpace(requiredCapabilityClass) && routeForResource.Length == 0)
            {
                continue;
            }

            return true;
        }
        return false;
    }

    private static HashSet<Guid> RequiredResourceIds(ProductionOrder po, ProcessOperationType operation)
    {
        var result = new HashSet<Guid>();
        if (po.Requirement?.RequiredResourceId is { } general) result.Add(general);
        foreach (var resourceId in po.Requirement?.ProcessOverrides
                     .Where(x => x.ProcessOperationType == operation && x.RequiredResourceId.HasValue)
                     .Select(x => x.RequiredResourceId!.Value)
                 ?? Enumerable.Empty<Guid>())
            result.Add(resourceId);
        return result;
    }

    private static string? RequiredCapabilityClass(ProductionOrder po, ManufacturingRouteOperation operation)
    {
        var orderClass = po.Requirement?.ProcessOverrides
            .Where(x => x.ProcessOperationType == operation.ProcessOperationType && !string.IsNullOrWhiteSpace(x.CapabilityClassCode))
            .Select(x => x.CapabilityClassCode)
            .FirstOrDefault();
        var gradeClass = po.SteelGrade?.ProcessRequirements
            .Where(x => x.ProcessOperationType == operation.ProcessOperationType && !string.IsNullOrWhiteSpace(x.CapabilityClassCode))
            .Select(x => x.CapabilityClassCode)
            .FirstOrDefault();
        return orderClass ?? gradeClass ?? operation.CapabilityClassCode;
    }

    private static SequenceResult OptimizeGradeSequence(
        IReadOnlyList<string> grades,
        IReadOnlyCollection<TransitionRule> rules)
    {
        var unique = grades.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (unique.Length <= 1) return new SequenceResult(true, unique, 0m);
        return unique.Length <= 15
            ? ExactSequence(unique, rules)
            : GreedySequence(unique, rules);
    }

    private static SequenceResult ExactSequence(string[] grades, IReadOnlyCollection<TransitionRule> rules)
    {
        var count = grades.Length;
        var fullMask = (1 << count) - 1;
        var states = new Dictionary<(int Mask, int Last), SequenceNode>();
        for (var i = 0; i < count; i++) states[(1 << i, i)] = new SequenceNode(0m, -1);

        for (var mask = 1; mask <= fullMask; mask++)
        {
            for (var last = 0; last < count; last++)
            {
                if (!states.TryGetValue((mask, last), out var node)) continue;
                for (var next = 0; next < count; next++)
                {
                    if ((mask & (1 << next)) != 0) continue;
                    var edge = Transition(grades[last], grades[next], rules);
                    if (!edge.IsAllowed) continue;
                    var key = (mask | (1 << next), next);
                    var cost = node.Cost + edge.Cost;
                    if (!states.TryGetValue(key, out var existing) || cost < existing.Cost)
                        states[key] = new SequenceNode(cost, last);
                }
            }
        }

        var terminals = Enumerable.Range(0, count)
            .Where(last => states.ContainsKey((fullMask, last)))
            .Select(last => (Last: last, Node: states[(fullMask, last)]))
            .OrderBy(x => x.Node.Cost)
            .ThenBy(x => grades[x.Last], StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (terminals.Length == 0)
            return new SequenceResult(false, Array.Empty<string>(), 0m, "No allowed grade-transition path covers all grades in the campaign.");

        var terminal = terminals[0];
        var path = new int[count];
        var currentMask = fullMask;
        var current = terminal.Last;
        for (var position = count - 1; position >= 0; position--)
        {
            path[position] = current;
            var node = states[(currentMask, current)];
            currentMask &= ~(1 << current);
            current = node.Previous;
        }

        return new SequenceResult(true, path.Select(x => grades[x]).ToArray(), terminal.Node.Cost);
    }

    private static SequenceResult GreedySequence(string[] grades, IReadOnlyCollection<TransitionRule> rules)
    {
        SequenceResult? best = null;
        for (var start = 0; start < grades.Length; start++)
        {
            var remaining = new HashSet<int>(Enumerable.Range(0, grades.Length));
            remaining.Remove(start);
            var path = new List<string> { grades[start] };
            var current = start;
            var cost = 0m;
            var feasible = true;

            while (remaining.Count > 0)
            {
                var candidates = remaining
                    .Select(index => (Index: index, Edge: Transition(grades[current], grades[index], rules)))
                    .Where(x => x.Edge.IsAllowed)
                    .OrderBy(x => x.Edge.Cost)
                    .ThenBy(x => grades[x.Index], StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (candidates.Length == 0)
                {
                    feasible = false;
                    break;
                }

                var next = candidates[0];
                cost += next.Edge.Cost;
                current = next.Index;
                remaining.Remove(current);
                path.Add(grades[current]);
            }

            if (!feasible) continue;
            var result = new SequenceResult(true, path, cost);
            if (best is null || result.Cost < best.Cost) best = result;
        }

        return best ?? new SequenceResult(false, Array.Empty<string>(), 0m,
            "No allowed grade-transition path covers all grades in the campaign.");
    }

    private static TransitionEdge Transition(string from, string to, IReadOnlyCollection<TransitionRule> rules)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return new TransitionEdge(true, 0m);

        var matching = rules.Where(rule =>
                rule.Dimension == TransitionDimension.Grade &&
                (!rule.ProcessOperationType.HasValue || rule.ProcessOperationType == ProcessOperationType.Ccm) &&
                string.Equals(rule.FromCode, from, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rule.ToCode, to, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matching.Length == 0) return new TransitionEdge(true, 0m);

        var allowed = matching.Where(x => x.IsAllowed).ToArray();
        if (allowed.Length == 0) return new TransitionEdge(false, 0m);

        var cost = allowed.Min(rule =>
            (decimal)Math.Max(0, rule.Penalty) +
            (decimal)Math.Max(0d, rule.TransitionTime.TotalMinutes) +
            (rule.RequiresSequenceBreak ? 1m : 0m));
        return new TransitionEdge(true, cost);
    }

    private static CampaignTechnicalEvaluation Reject(string reason) =>
        new(false, 0m, 0m, Array.Empty<string>(), reason);

    private static bool Matches(string? configured, string? actual) =>
        string.IsNullOrWhiteSpace(configured) || string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);

    private sealed record SequenceNode(decimal Cost, int Previous);
    private sealed record SequenceResult(bool IsFeasible, IReadOnlyList<string> Grades, decimal Cost, string? Reason = null);
    private sealed record TransitionEdge(bool IsAllowed, decimal Cost);
}
