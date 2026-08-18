using APS.Application;
using APS.Domain;

namespace APS.Planning;

/// <summary>
/// Furnace-feasible heat construction for Campaign material quantities that were precomputed by the canonical
/// material engine. This mirrors CampaignPlanningService's physical heat-envelope rules so changing material
/// authority does not weaken EAF feasibility, grade/customer segregation or casting-yield behavior.
/// </summary>
public static class CanonicalCampaignHeatBuilder
{
    public static void Rebuild(Campaign campaign, CampaignPlanningRequest request)
    {
        campaign.GradeSequence.Clear();
        campaign.Heats.Clear();

        var allocations = campaign.Allocations.ToList();
        var heatGroups = allocations
            .Where(a => a.ProductionOrder is not null && a.FreshSteelQuantityMt > 0m)
            .GroupBy(a => new HeatCompatibilityKey(a.ProductionOrder!.GradeCode, HeatRequirementSignature(a.ProductionOrder)))
            .Select(g => new
            {
                Key = g.Key,
                RequiredOutputQuantityMt = g.Sum(x => x.FreshSteelQuantityMt),
                ProductionOrders = g.Select(x => x.ProductionOrder!).DistinctBy(x => x.Id).ToArray(),
                FirstIndex = allocations.FindIndex(x => ReferenceEquals(x, g.First()))
            })
            .OrderBy(x => x.FirstIndex)
            .ToArray();

        var gradeSequenceNo = 1;
        var heatSequenceNo = 1;
        foreach (var group in heatGroups)
        {
            var grade = group.ProductionOrders.Select(x => x.SteelGrade).FirstOrDefault(x => x is not null);
            var yieldPct = grade?.ProcessRequirements
                               .FirstOrDefault(x => x.ProcessOperationType == ProcessOperationType.Ccm)
                               ?.ExpectedYieldPct
                           ?? request.Policy.ExpectedCastingYieldPct;
            if (yieldPct <= 0m || yieldPct > 100m)
                throw new InvalidOperationException($"Grade {group.Key.GradeCode} has invalid casting yield {yieldPct}.");

            var plannedInputQuantity = decimal.Round(group.RequiredOutputQuantityMt / (yieldPct / 100m), 4, MidpointRounding.AwayFromZero);
            var gradeSequence = new CampaignGradeSequence
            {
                CampaignId = campaign.Id,
                Campaign = campaign,
                SequenceNumber = gradeSequenceNo++,
                GradeCode = group.Key.GradeCode,
                PlannedQuantityMt = plannedInputQuantity
            };
            campaign.GradeSequence.Add(gradeSequence);

            var heatPlans = BuildFurnaceFeasibleHeatPlan(plannedInputQuantity, group.ProductionOrders, request);
            foreach (var heatPlan in heatPlans)
            {
                campaign.Heats.Add(new CampaignHeat
                {
                    CampaignId = campaign.Id,
                    Campaign = campaign,
                    CampaignGradeSequenceId = gradeSequence.Id,
                    CampaignGradeSequence = gradeSequence,
                    SequenceNumber = heatSequenceNo++,
                    GradeCode = group.Key.GradeCode,
                    PlannedQuantityMt = heatPlan.QuantityMt,
                    MinimumFeasibleQuantityMt = heatPlan.MinimumMt,
                    TargetQuantityMt = heatPlan.TargetMt,
                    MaximumFeasibleQuantityMt = heatPlan.MaximumMt
                });
            }
        }
    }

    private static IReadOnlyList<HeatQuantityPlan> BuildFurnaceFeasibleHeatPlan(
        decimal totalQuantityMt,
        IReadOnlyCollection<ProductionOrder> productionOrders,
        CampaignPlanningRequest request)
    {
        if (totalQuantityMt <= 0m) return Array.Empty<HeatQuantityPlan>();
        var envelopes = BuildFurnaceEnvelopes(productionOrders, request);
        if (envelopes.Count == 0)
        {
            if (request.Resources is { Count: > 0 })
                throw new InvalidOperationException($"No eligible EAF heat-capacity envelope exists for {productionOrders.First().GradeCode} on route {productionOrders.First().RouteCode}.");

            return DistributeLegacyHeatQuantities(totalQuantityMt, request.Policy)
                .Select(x => new HeatQuantityPlan(x, request.Policy.MinimumHeatSizeMt, request.Policy.NominalHeatSizeMt, request.Policy.MaximumHeatSizeMt))
                .ToArray();
        }

        var globalMin = envelopes.Min(x => x.MinimumMt);
        var globalMax = envelopes.Max(x => x.MaximumMt);
        var minimumCount = Math.Max(1, (int)Math.Ceiling(totalQuantityMt / globalMax));
        var maximumCount = Math.Max(minimumCount, (int)Math.Floor(totalQuantityMt / globalMin));
        HeatPlanCandidate? best = null;

        for (var heatCount = minimumCount; heatCount <= maximumCount; heatCount++)
        {
            foreach (var counts in EnumerateEnvelopeCounts(envelopes.Count, heatCount))
            {
                var minimumTotal = counts.Select((count, index) => count * envelopes[index].MinimumMt).Sum();
                var maximumTotal = counts.Select((count, index) => count * envelopes[index].MaximumMt).Sum();
                if (totalQuantityMt < minimumTotal || totalQuantityMt > maximumTotal) continue;

                var items = new List<MutableHeatPlan>();
                for (var envelopeIndex = 0; envelopeIndex < envelopes.Count; envelopeIndex++)
                for (var i = 0; i < counts[envelopeIndex]; i++)
                {
                    var envelope = envelopes[envelopeIndex];
                    items.Add(new MutableHeatPlan(envelope, Math.Clamp(envelope.TargetMt, envelope.MinimumMt, envelope.MaximumMt)));
                }

                var delta = totalQuantityMt - items.Sum(x => x.QuantityMt);
                if (delta > 0m)
                {
                    foreach (var item in items.OrderByDescending(x => x.Envelope.MaximumMt - x.QuantityMt))
                    {
                        if (delta <= 0m) break;
                        var add = Math.Min(delta, item.Envelope.MaximumMt - item.QuantityMt);
                        item.QuantityMt += add;
                        delta -= add;
                    }
                }
                else if (delta < 0m)
                {
                    var reduce = -delta;
                    foreach (var item in items.OrderByDescending(x => x.QuantityMt - x.Envelope.MinimumMt))
                    {
                        if (reduce <= 0m) break;
                        var take = Math.Min(reduce, item.QuantityMt - item.Envelope.MinimumMt);
                        item.QuantityMt -= take;
                        reduce -= take;
                    }
                    delta = -reduce;
                }

                if (Math.Abs(delta) > 0.0001m) continue;
                var score = items.Sum(x => Math.Abs(x.QuantityMt - x.Envelope.TargetMt));
                var candidate = new HeatPlanCandidate(
                    items.Select(x => new HeatQuantityPlan(
                            decimal.Round(x.QuantityMt, 4, MidpointRounding.AwayFromZero),
                            x.Envelope.MinimumMt,
                            x.Envelope.TargetMt,
                            x.Envelope.MaximumMt))
                        .ToArray(),
                    score);
                if (best is null || candidate.Score < best.Score) best = candidate;
            }
        }

        return best?.Heats
               ?? throw new InvalidOperationException(
                   $"Fresh steel requirement {totalQuantityMt:0.####} MT for grade {productionOrders.First().GradeCode} cannot be split into furnace-feasible heats with the configured EAF capacities.");
    }

    private static IReadOnlyList<FurnaceEnvelope> BuildFurnaceEnvelopes(
        IReadOnlyCollection<ProductionOrder> productionOrders,
        CampaignPlanningRequest request)
    {
        if (request.Resources is null || request.Resources.Count == 0) return Array.Empty<FurnaceEnvelope>();

        var explicitEafExists = request.Resources.Any(x => x.ProcessUnitType == ProcessUnitType.Eaf);
        var resources = request.Resources.Where(x =>
            x.IsActive &&
            x.OperatingState is not ResourceOperatingState.Breakdown and not ResourceOperatingState.Disabled &&
            (x.ProcessUnitType == ProcessUnitType.Eaf || (!explicitEafExists && x.ResourceType == ResourceType.Furnace)));
        var capabilities = request.ResourceCapabilities ?? Array.Empty<ResourceCapability>();
        var representative = productionOrders.First();
        var grade = representative.SteelGrade;
        var gradeRequirement = grade?.ProcessRequirements.FirstOrDefault(x => x.ProcessOperationType == ProcessOperationType.Eaf);
        var requiredResourceIds = productionOrders.SelectMany(RequiredEafResources).Distinct().ToArray();

        if (requiredResourceIds.Length > 1)
            throw new InvalidOperationException($"Orders grouped into one heat require different physical EAF resources for grade {representative.GradeCode}.");

        var result = new List<FurnaceEnvelope>();
        foreach (var resource in resources)
        {
            if (requiredResourceIds.Length == 1 && resource.Id != requiredResourceIds[0]) continue;
            if (!resource.MinimumHeatWeightMt.HasValue || !resource.NominalHeatWeightMt.HasValue || !resource.MaximumHeatWeightMt.HasValue)
                throw new InvalidOperationException($"EAF resource {resource.Code} is missing Minimum/Nominal/Maximum heat-weight master data.");

            var requiredCapabilityClass = representative.Requirement?.ProcessOverrides
                .FirstOrDefault(x => x.ProcessOperationType == ProcessOperationType.Eaf)?.CapabilityClassCode
                ?? gradeRequirement?.CapabilityClassCode;
            var matchingCapabilities = capabilities.Where(c =>
                    c.ResourceId == resource.Id &&
                    (!c.ProcessOperationType.HasValue || c.ProcessOperationType == ProcessOperationType.Eaf) &&
                    Matches(c.RouteCode, representative.RouteCode) &&
                    Matches(c.GradeCode, representative.GradeCode) &&
                    Matches(c.GradeFamilyCode, representative.GradeFamilyCode) &&
                    (string.IsNullOrWhiteSpace(requiredCapabilityClass) || Same(c.CapabilityClassCode, requiredCapabilityClass)))
                .ToArray();
            if (capabilities.Any(c => c.ResourceId == resource.Id) && matchingCapabilities.Length == 0) continue;
            if (!string.IsNullOrWhiteSpace(requiredCapabilityClass) && matchingCapabilities.Length == 0) continue;

            var minimum = resource.MinimumHeatWeightMt.Value;
            var target = resource.NominalHeatWeightMt.Value;
            var maximum = resource.MaximumHeatWeightMt.Value * Math.Clamp(resource.CapacityFactorPct, 0m, 100m) / 100m;
            if (gradeRequirement?.MinimumHeatWeightMt is { } gradeMin) minimum = Math.Max(minimum, gradeMin);
            if (gradeRequirement?.TargetHeatWeightMt is { } gradeTarget) target = gradeTarget;
            if (gradeRequirement?.MaximumHeatWeightMt is { } gradeMax) maximum = Math.Min(maximum, gradeMax);

            var capMinimum = matchingCapabilities.Where(x => x.MinimumQuantityMt.HasValue).Select(x => x.MinimumQuantityMt!.Value).DefaultIfEmpty(minimum).Max();
            var capMaximum = matchingCapabilities.Where(x => x.MaximumQuantityMt.HasValue).Select(x => x.MaximumQuantityMt!.Value).DefaultIfEmpty(maximum).Min();
            minimum = Math.Max(minimum, capMinimum);
            maximum = Math.Min(maximum, capMaximum);
            target = Math.Clamp(target, minimum, maximum);

            if (minimum <= 0m || maximum < minimum) continue;
            result.Add(new FurnaceEnvelope(resource.Id, minimum, target, maximum));
        }
        return result;
    }

    private static IEnumerable<Guid> RequiredEafResources(ProductionOrder po)
    {
        if (po.Requirement?.RequiredResourceId is { } general) yield return general;
        foreach (var id in po.Requirement?.ProcessOverrides
                     .Where(x => x.ProcessOperationType == ProcessOperationType.Eaf && x.RequiredResourceId.HasValue)
                     .Select(x => x.RequiredResourceId!.Value)
                 ?? Enumerable.Empty<Guid>())
            yield return id;
    }

    private static IEnumerable<int[]> EnumerateEnvelopeCounts(int envelopeCount, int totalCount)
    {
        var current = new int[envelopeCount];
        foreach (var result in Enumerate(0, totalCount)) yield return result;

        IEnumerable<int[]> Enumerate(int index, int remaining)
        {
            if (index == envelopeCount - 1)
            {
                current[index] = remaining;
                yield return (int[])current.Clone();
                yield break;
            }
            for (var count = 0; count <= remaining; count++)
            {
                current[index] = count;
                foreach (var result in Enumerate(index + 1, remaining - count)) yield return result;
            }
        }
    }

    private static IReadOnlyList<decimal> DistributeLegacyHeatQuantities(decimal totalQuantityMt, CampaignPlanningPolicy policy)
    {
        if (totalQuantityMt <= 0m) return Array.Empty<decimal>();
        var preferredCount = Math.Max(1, (int)Math.Round(totalQuantityMt / policy.NominalHeatSizeMt, MidpointRounding.AwayFromZero));
        var minimumCount = Math.Max(1, (int)Math.Ceiling(totalQuantityMt / policy.MaximumHeatSizeMt));
        var maximumCount = totalQuantityMt >= policy.MinimumHeatSizeMt
            ? Math.Max(1, (int)Math.Floor(totalQuantityMt / policy.MinimumHeatSizeMt))
            : 1;
        var heatCount = Math.Clamp(preferredCount, minimumCount, maximumCount);
        var average = decimal.Round(totalQuantityMt / heatCount, 4, MidpointRounding.AwayFromZero);
        var result = new List<decimal>(heatCount);
        var allocated = 0m;
        for (var i = 0; i < heatCount; i++)
        {
            var quantity = i == heatCount - 1 ? totalQuantityMt - allocated : average;
            result.Add(quantity);
            allocated += quantity;
        }
        return result;
    }

    private static string HeatRequirementSignature(ProductionOrder po)
    {
        var requirement = po.Requirement;
        if (requirement is null) return "*";
        var chemistry = string.Join(';', requirement.ChemistryOverrides
            .OrderBy(x => x.ElementCode, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.ElementCode}:{x.MinimumPct}:{x.TargetPct}:{x.MaximumPct}"));
        var processes = string.Join(';', requirement.ProcessOverrides
            .OrderBy(x => x.ProcessOperationType)
            .ThenBy(x => x.RequiredResourceId)
            .Select(x => $"{x.ProcessOperationType}:{x.Requirement}:{x.CapabilityClassCode}:{x.RequiredResourceId}:{x.MaximumQueueMinutes}"));
        return string.Join('|',
            requirement.QualityClassCode ?? "", requirement.SegregationPolicy,
            requirement.RequireVd, requirement.ForbidVd, requirement.RequireReheating,
            requirement.ForbidHotCharge, requirement.RequireTmt,
            requirement.RequiredRouteCode ?? "", requirement.RequiredResourceId,
            requirement.MinimumSuperheatC, requirement.TargetSuperheatC, requirement.MaximumSuperheatC,
            chemistry, processes);
    }

    private static bool Matches(string? configured, string? actual) =>
        string.IsNullOrWhiteSpace(configured) || string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);
    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private sealed record HeatCompatibilityKey(string GradeCode, string RequirementSignature);
    private sealed record FurnaceEnvelope(Guid ResourceId, decimal MinimumMt, decimal TargetMt, decimal MaximumMt);
    private sealed record HeatQuantityPlan(decimal QuantityMt, decimal MinimumMt, decimal TargetMt, decimal MaximumMt);
    private sealed record HeatPlanCandidate(IReadOnlyList<HeatQuantityPlan> Heats, decimal Score);
    private sealed class MutableHeatPlan(FurnaceEnvelope envelope, decimal quantityMt)
    {
        public FurnaceEnvelope Envelope { get; } = envelope;
        public decimal QuantityMt { get; set; } = quantityMt;
    }
}
