using APS.Application;
using APS.Domain;

namespace APS.Planning;

public sealed class ProductionStructurePlanningService : IProductionStructurePlanningService
{
    public ProductionStructurePlanningResult Build(ProductionStructurePlanningRequest request)
    {
        var issues = new List<PlanningIssue>();
        var castSequences = new List<CastSequence>();
        var billetSupplies = new List<PlannedBilletSupply>();
        var rollingPlans = new List<RollingPlan>();
        var castDurations = new Dictionary<Guid, int>();
        var rollingDurations = new Dictionary<Guid, int>();

        var resources = request.Resources.Where(r => r.IsActive).ToDictionary(r => r.Id);
        var capabilities = request.Capabilities.GroupBy(c => c.ResourceId).ToDictionary(g => g.Key, g => g.ToArray());

        BuildCasterPlan(request, resources, capabilities, castSequences, billetSupplies, castDurations, issues);
        BuildRollingPlan(request, resources, capabilities, castSequences, rollingPlans, rollingDurations, issues);

        var tasks = BuildSchedulingTasks(
            request,
            resources,
            castSequences,
            rollingPlans,
            castDurations,
            rollingDurations,
            issues);

        return new ProductionStructurePlanningResult(castSequences, rollingPlans, billetSupplies, tasks, issues);
    }

    private static void BuildCasterPlan(
        ProductionStructurePlanningRequest request,
        IReadOnlyDictionary<Guid, Resource> resources,
        IReadOnlyDictionary<Guid, ResourceCapability[]> capabilities,
        List<CastSequence> castSequences,
        List<PlannedBilletSupply> billetSupplies,
        Dictionary<Guid, int> castDurations,
        List<PlanningIssue> issues)
    {
        var casterStates = resources.Values
            .Where(r => r.ResourceType == ResourceType.Caster)
            .ToDictionary(r => r.Id, r => new CasterState(r));

        var campaigns = request.Campaigns.ToDictionary(c => c.Id);
        var orderedHeats = request.Campaigns
            .OrderBy(c => c.RequiredDate)
            .ThenBy(c => c.CampaignNumber)
            .SelectMany(c => c.Heats.OrderBy(h => h.SequenceNumber).Select(h => (Campaign: c, Heat: h)))
            .ToArray();

        foreach (var item in orderedHeats)
        {
            var campaign = item.Campaign;
            var heat = item.Heat;
            var gradeFamily = GradeFamilyFor(campaign, heat.GradeCode);

            var candidates = casterStates.Values
                .Select(state =>
                {
                    var matching = MatchingCapabilities(
                        state.Resource,
                        capabilities,
                        campaign.RouteCode,
                        heat.GradeCode,
                        gradeFamily,
                        null,
                        campaign.CasterSectionCode,
                        null);
                    if (matching.Count == 0) return null;

                    var duration = DurationMinutes(heat.PlannedQuantityMt, matching, request.Policy.DefaultCastingMinutesPerHeat);
                    var append = CanAppendToCastSequence(state, campaign, heat, request);
                    var transitionPenalty = TransitionPenalty(
                        request.TransitionRules,
                        state.Resource,
                        TransitionDimension.Grade,
                        state.LastGradeCode,
                        heat.GradeCode);
                    var score = state.LoadMinutes + transitionPenalty + (append ? 0 : request.Policy.SequenceBreakPenalty);
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
                    $"No eligible caster can process heat {heat.SequenceNumber} of campaign {campaign.CampaignNumber} ({heat.GradeCode}, {campaign.CasterSectionCode}).",
                    heat.Id));
                continue;
            }

            var selected = candidates[0];
            var state = selected.State;

            if (!selected.AppendToCurrent || state.CurrentSequence is null)
            {
                state.CurrentSequence = new CastSequence
                {
                    CampaignId = campaign.Id,
                    CasterResourceId = state.Resource.Id,
                    SequenceNumber = ++state.SequenceNumber,
                    CasterSectionCode = campaign.CasterSectionCode,
                    RouteCode = campaign.RouteCode
                };
                castSequences.Add(state.CurrentSequence);
                castDurations[state.CurrentSequence.Id] = 0;
            }
            else if (state.CurrentSequence.CampaignId != campaign.Id)
            {
                state.CurrentSequence.CampaignId = null;
            }

            state.CurrentSequence.Heats.Add(new CastSequenceHeat
            {
                CastSequenceId = state.CurrentSequence.Id,
                CastSequence = state.CurrentSequence,
                CampaignHeatId = heat.Id,
                CampaignHeat = heat,
                Position = state.CurrentSequence.Heats.Count + 1
            });

            heat.PreferredCasterResourceId = state.Resource.Id;
            state.LoadMinutes += selected.DurationMinutes;
            state.LastGradeCode = heat.GradeCode;
            castDurations[state.CurrentSequence.Id] += selected.DurationMinutes;

            var yield = Math.Clamp(request.Policy.CastingYieldPct, 0m, 100m) / 100m;
            billetSupplies.Add(new PlannedBilletSupply(
                campaign.Id,
                heat.Id,
                state.CurrentSequence.Id,
                state.Resource.Id,
                heat.GradeCode,
                campaign.CasterSectionCode,
                decimal.Round(heat.PlannedQuantityMt * yield, 4)));
        }
    }

    private static void BuildRollingPlan(
        ProductionStructurePlanningRequest request,
        IReadOnlyDictionary<Guid, Resource> resources,
        IReadOnlyDictionary<Guid, ResourceCapability[]> capabilities,
        IReadOnlyCollection<CastSequence> castSequences,
        List<RollingPlan> rollingPlans,
        Dictionary<Guid, int> rollingDurations,
        List<PlanningIssue> issues)
    {
        var millStates = resources.Values
            .Where(r => r.ResourceType == ResourceType.RollingMill)
            .ToDictionary(r => r.Id, r => new MillState(r));

        var lines = new List<RollingDemandLine>();
        foreach (var campaign in request.Campaigns)
        {
            foreach (var allocation in campaign.Allocations.Where(a => a.ProductionOrder is not null))
            {
                var po = allocation.ProductionOrder!;

                if (allocation.ExistingIntermediateInventoryMt > 0m)
                {
                    lines.Add(new RollingDemandLine(
                        campaign,
                        po,
                        allocation.ExistingIntermediateInventoryMt,
                        allocation.ExistingIntermediateInventoryMt,
                        0m,
                        RequiresFreshSteel: false));
                }

                if (allocation.FreshSteelQuantityMt > 0m)
                {
                    lines.Add(new RollingDemandLine(
                        campaign,
                        po,
                        allocation.FreshSteelQuantityMt,
                        0m,
                        allocation.FreshSteelQuantityMt,
                        RequiresFreshSteel: true));
                }
            }
        }

        var groups = lines
            .GroupBy(line => new RollingGroupKey(
                line.ProductionOrder.GradeCode,
                line.ProductionOrder.CasterSectionCode,
                line.ProductionOrder.FinalCrossSectionCode,
                line.ProductionOrder.RouteCode,
                line.ProductionOrder.ProductFamilyCode,
                line.RequiresFreshSteel,
                request.Policy.AllowCrossCampaignRollingPlans ? null : line.Campaign.Id))
            .OrderBy(g => g.Min(x => x.ProductionOrder.RequiredDate))
            .ThenBy(g => g.Key.GradeCode)
            .ThenBy(g => g.Key.OutputCrossSectionCode)
            .ToArray();

        foreach (var group in groups)
        {
            var groupLines = group
                .OrderByDescending(x => x.ProductionOrder.Priority)
                .ThenBy(x => x.ProductionOrder.RequiredDate)
                .ThenBy(x => x.ProductionOrder.ProductionOrderNumber)
                .ToArray();
            var quantity = groupLines.Sum(x => x.QuantityMt);
            var representative = groupLines[0].ProductionOrder;

            var candidates = millStates.Values
                .Select(state =>
                {
                    var matching = MatchingCapabilities(
                        state.Resource,
                        capabilities,
                        representative.RouteCode,
                        representative.GradeCode,
                        representative.GradeFamilyCode,
                        representative.CasterSectionCode,
                        representative.FinalCrossSectionCode,
                        representative.ProductFamilyCode);
                    if (matching.Count == 0) return null;

                    var fallback = Math.Max(1, (int)Math.Ceiling((double)(quantity / 100m) * request.Policy.DefaultRollingMinutesPer100Mt));
                    var duration = DurationMinutes(quantity, matching, fallback);
                    var gradePenalty = TransitionPenalty(
                        request.TransitionRules,
                        state.Resource,
                        TransitionDimension.Grade,
                        state.LastGradeCode,
                        representative.GradeCode);
                    var sectionPenalty = TransitionPenalty(
                        request.TransitionRules,
                        state.Resource,
                        TransitionDimension.CrossSection,
                        state.LastOutputSectionCode,
                        representative.FinalCrossSectionCode);
                    var score = state.LoadMinutes + gradePenalty + sectionPenalty;
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
                    "MILL_NOT_ELIGIBLE",
                    $"No eligible rolling mill can process {representative.GradeCode} from {representative.CasterSectionCode} to {representative.FinalCrossSectionCode}.",
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
                InputCrossSectionCode = representative.CasterSectionCode,
                OutputCrossSectionCode = representative.FinalCrossSectionCode,
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
            rollingDurations[plan.Id] = selected.DurationMinutes;
            selected.State.LoadMinutes += selected.DurationMinutes;
            selected.State.LastGradeCode = representative.GradeCode;
            selected.State.LastOutputSectionCode = representative.FinalCrossSectionCode;

            if (group.Key.RequiresFreshSteel)
            {
                var sourceCampaignGrades = groupLines
                    .Select(x => (x.Campaign.Id, x.ProductionOrder.GradeCode))
                    .Distinct()
                    .ToArray();

                foreach (var source in sourceCampaignGrades)
                {
                    var hasCastSupply = castSequences.Any(sequence => sequence.Heats.Any(h =>
                        h.CampaignHeat?.CampaignId == source.Id &&
                        h.CampaignHeat.GradeCode == source.GradeCode));

                    if (!hasCastSupply)
                    {
                        issues.Add(new PlanningIssue(
                            PlanningIssueSeverity.Error,
                            "ROLLING_WITHOUT_CAST_SUPPLY",
                            $"Rolling plan {plan.Id} requires fresh {source.GradeCode} material but no caster sequence produced it.",
                            plan.Id));
                    }
                }
            }
        }
    }

    private static IReadOnlyCollection<FiniteScheduleTask> BuildSchedulingTasks(
        ProductionStructurePlanningRequest request,
        IReadOnlyDictionary<Guid, Resource> resources,
        IReadOnlyCollection<CastSequence> castSequences,
        IReadOnlyCollection<RollingPlan> rollingPlans,
        IReadOnlyDictionary<Guid, int> castDurations,
        IReadOnlyDictionary<Guid, int> rollingDurations,
        List<PlanningIssue> issues)
    {
        var tasks = new List<FiniteScheduleTask>();
        var campaignById = request.Campaigns.ToDictionary(c => c.Id);
        var castTaskIds = new Dictionary<Guid, Guid>();

        foreach (var sequence in castSequences)
        {
            var campaigns = sequence.Heats
                .Select(h => h.CampaignHeat?.CampaignId)
                .Where(id => id.HasValue)
                .Select(id => campaignById[id!.Value])
                .DistinctBy(c => c.Id)
                .ToArray();
            var grades = sequence.Heats
                .Select(h => h.CampaignHeat?.GradeCode)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct()
                .ToArray();
            var quantity = sequence.Heats.Sum(h => h.CampaignHeat?.PlannedQuantityMt ?? 0m);
            var taskId = Guid.NewGuid();
            castTaskIds[sequence.Id] = taskId;

            tasks.Add(new FiniteScheduleTask(
                taskId,
                sequence.Id,
                FiniteScheduleTaskType.Casting,
                $"Cast {sequence.SequenceNumber}",
                grades.Length == 1 ? grades[0]! : "MIXED",
                sequence.CasterSectionCode,
                quantity,
                null,
                campaigns.Length == 0 ? null : campaigns.Min(c => c.RequiredDate),
                campaigns.SelectMany(c => c.Allocations).Select(a => a.ProductionOrder?.Priority ?? 0).DefaultIfEmpty(0).Max(),
                new[] { new FiniteScheduleResourceOption(sequence.CasterResourceId, castDurations[sequence.Id]) },
                Array.Empty<FiniteScheduleDependency>()));
        }

        foreach (var plan in rollingPlans)
        {
            if (!plan.RollingMillResourceId.HasValue) continue;

            var dependencies = new List<FiniteScheduleDependency>();
            foreach (var allocation in plan.Allocations.Where(a => a.FreshSteelQuantityMt > 0m))
            {
                var sourceSequences = castSequences
                    .Where(sequence => sequence.Heats.Any(h =>
                        h.CampaignHeat?.CampaignId == allocation.CampaignId &&
                        h.CampaignHeat.GradeCode == plan.GradeCode))
                    .ToArray();

                foreach (var sourceSequence in sourceSequences)
                {
                    if (!castTaskIds.TryGetValue(sourceSequence.Id, out var predecessorTaskId)) continue;

                    var link = request.FlowLinks.FirstOrDefault(l =>
                        l.IsEnabled &&
                        l.FromResourceId == sourceSequence.CasterResourceId &&
                        l.ToResourceId == plan.RollingMillResourceId.Value);

                    var minLag = link is null ? 0 : Minutes(link.MinimumTransferTime);
                    int? maxLag = link?.MaximumTransferTime is null ? null : Minutes(link.MaximumTransferTime.Value);
                    dependencies.Add(new FiniteScheduleDependency(predecessorTaskId, minLag, maxLag));
                }
            }

            var due = plan.Allocations
                .Select(a => a.ProductionOrder?.RequiredDate)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .DefaultIfEmpty()
                .Min();
            var priority = plan.Allocations.Select(a => a.ProductionOrder?.Priority ?? 0).DefaultIfEmpty(0).Max();

            tasks.Add(new FiniteScheduleTask(
                Guid.NewGuid(),
                plan.Id,
                FiniteScheduleTaskType.HotRolling,
                $"Roll {plan.SequenceNumber} - {plan.GradeCode}/{plan.OutputCrossSectionCode}",
                plan.GradeCode,
                plan.OutputCrossSectionCode,
                plan.PlannedQuantityMt,
                null,
                due == default ? null : due,
                priority,
                new[] { new FiniteScheduleResourceOption(plan.RollingMillResourceId.Value, rollingDurations[plan.Id]) },
                dependencies.DistinctBy(d => d.PredecessorTaskId).ToArray()));
        }

        return tasks;
    }

    private static bool CanAppendToCastSequence(
        CasterState state,
        Campaign campaign,
        CampaignHeat heat,
        ProductionStructurePlanningRequest request)
    {
        var current = state.CurrentSequence;
        if (current is null) return false;
        if (current.Heats.Count >= request.Policy.MaximumHeatsPerCastSequence) return false;
        if (!string.Equals(current.CasterSectionCode, campaign.CasterSectionCode, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(current.RouteCode, campaign.RouteCode, StringComparison.OrdinalIgnoreCase)) return false;
        if (!request.Policy.AllowCrossCampaignCastSequences && current.CampaignId != campaign.Id) return false;

        return TransitionAllowed(
            request.TransitionRules,
            state.Resource,
            TransitionDimension.Grade,
            state.LastGradeCode,
            heat.GradeCode);
    }

    private static IReadOnlyList<ResourceCapability> MatchingCapabilities(
        Resource resource,
        IReadOnlyDictionary<Guid, ResourceCapability[]> capabilities,
        string routeCode,
        string gradeCode,
        string? gradeFamilyCode,
        string? inputSection,
        string? outputSection,
        string? productFamilyCode)
    {
        if (!capabilities.TryGetValue(resource.Id, out var resourceCapabilities)) return Array.Empty<ResourceCapability>();

        return resourceCapabilities.Where(c =>
            Matches(c.RouteCode, routeCode) &&
            (string.IsNullOrWhiteSpace(c.GradeCode) || Matches(c.GradeCode, gradeCode)) &&
            (string.IsNullOrWhiteSpace(c.GradeFamilyCode) || Matches(c.GradeFamilyCode, gradeFamilyCode)) &&
            (string.IsNullOrWhiteSpace(c.InputCrossSectionCode) || Matches(c.InputCrossSectionCode, inputSection)) &&
            (string.IsNullOrWhiteSpace(c.OutputCrossSectionCode) || Matches(c.OutputCrossSectionCode, outputSection)) &&
            (string.IsNullOrWhiteSpace(c.ProductFamilyCode) || Matches(c.ProductFamilyCode, productFamilyCode)))
            .ToArray();
    }

    private static int DurationMinutes(decimal quantityMt, IReadOnlyCollection<ResourceCapability> capabilities, int fallbackMinutes)
    {
        var throughput = capabilities
            .Where(c => c.ThroughputMtPerHour.HasValue && c.ThroughputMtPerHour.Value > 0m)
            .Select(c => c.ThroughputMtPerHour!.Value)
            .DefaultIfEmpty(0m)
            .Max();

        if (throughput <= 0m) return Math.Max(1, fallbackMinutes);
        return Math.Max(1, (int)Math.Ceiling((double)(quantityMt / throughput * 60m)));
    }

    private static string? GradeFamilyFor(Campaign campaign, string gradeCode) =>
        campaign.Allocations
            .Select(a => a.ProductionOrder)
            .FirstOrDefault(po => po is not null && string.Equals(po.GradeCode, gradeCode, StringComparison.OrdinalIgnoreCase))
            ?.GradeFamilyCode;

    private static bool TransitionAllowed(
        IReadOnlyCollection<TransitionRule> rules,
        Resource resource,
        TransitionDimension dimension,
        string? from,
        string to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return true;
        var rule = FindTransitionRule(rules, resource, dimension, from, to);
        return rule?.IsAllowed ?? true;
    }

    private static int TransitionPenalty(
        IReadOnlyCollection<TransitionRule> rules,
        Resource resource,
        TransitionDimension dimension,
        string? from,
        string to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return 0;
        return FindTransitionRule(rules, resource, dimension, from, to)?.Penalty ?? 0;
    }

    private static TransitionRule? FindTransitionRule(
        IReadOnlyCollection<TransitionRule> rules,
        Resource resource,
        TransitionDimension dimension,
        string from,
        string to) =>
        rules
            .Where(r => r.Dimension == dimension && Matches(r.FromCode, from) && Matches(r.ToCode, to))
            .OrderByDescending(r => r.ResourceId == resource.Id)
            .ThenByDescending(r => r.ResourceType == resource.ResourceType)
            .FirstOrDefault(r => r.ResourceId == resource.Id || r.ResourceType == resource.ResourceType || (!r.ResourceId.HasValue && !r.ResourceType.HasValue));

    private static bool Matches(string? configured, string? actual) =>
        string.IsNullOrWhiteSpace(configured) ||
        string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);

    private static int Minutes(TimeSpan value) => Math.Max(0, (int)Math.Ceiling(value.TotalMinutes));

    private sealed class CasterState(Resource resource)
    {
        public Resource Resource { get; } = resource;
        public int LoadMinutes { get; set; }
        public int SequenceNumber { get; set; }
        public string? LastGradeCode { get; set; }
        public CastSequence? CurrentSequence { get; set; }
    }

    private sealed record CasterCandidate(CasterState State, int DurationMinutes, bool AppendToCurrent, int Score);

    private sealed class MillState(Resource resource)
    {
        public Resource Resource { get; } = resource;
        public int LoadMinutes { get; set; }
        public int SequenceNumber { get; set; }
        public string? LastGradeCode { get; set; }
        public string? LastOutputSectionCode { get; set; }
    }

    private sealed record MillCandidate(MillState State, int DurationMinutes, int Score);

    private sealed record RollingDemandLine(
        Campaign Campaign,
        ProductionOrder ProductionOrder,
        decimal QuantityMt,
        decimal ExistingIntermediateInventoryMt,
        decimal FreshSteelQuantityMt,
        bool RequiresFreshSteel);

    private sealed record RollingGroupKey(
        string GradeCode,
        string InputCrossSectionCode,
        string OutputCrossSectionCode,
        string RouteCode,
        string? ProductFamilyCode,
        bool RequiresFreshSteel,
        Guid? CampaignPartition);
}
