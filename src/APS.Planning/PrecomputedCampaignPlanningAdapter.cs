using APS.Application;
using APS.Domain;

namespace APS.Planning;

/// <summary>
/// Makes Campaign a consumer of canonical material facts when the upstream BOM/time-phased pass has already
/// decided what each PO can cover from qualified steel feed and what billet/bloom/slab output must be made internally.
/// Compatibility callers without precomputed material continue through CampaignPlanningService's legacy material path.
/// </summary>
public static class PrecomputedCampaignPlanningAdapter
{
    private const decimal QuantityToleranceMt = 0.0001m;

    public static CampaignPlanningResult FormCampaigns(
        ICampaignPlanningService campaignPlanning,
        CampaignPlanningRequest request)
    {
        var precomputed = request.PrecomputedMaterialDemand;
        if (precomputed is null)
            return campaignPlanning.FormCampaigns(request);

        var rows = precomputed.ToDictionary(x => x.ProductionOrderId);
        var activeOrders = request.ProductionOrders
            .Where(x => x.Status is ProductionOrderStatus.Planned or ProductionOrderStatus.Firmed)
            .ToArray();

        foreach (var po in activeOrders)
        {
            if (!rows.TryGetValue(po.Id, out var row))
                throw new InvalidOperationException($"Canonical material demand is missing for Production Order {po.ProductionOrderNumber}.");

            var feedBalance = row.CoveredIntermediateMt + row.FreshSteelRequirementMt + row.UncoveredSteelFeedShortfallMt;
            if (Math.Abs(row.SteelFeedRequirementMt - feedBalance) > QuantityToleranceMt)
                throw new InvalidOperationException(
                    $"Canonical steel-feed demand for Production Order {po.ProductionOrderNumber} does not conserve quantity: " +
                    $"required={row.SteelFeedRequirementMt:0.####} MT, covered={row.CoveredIntermediateMt:0.####} MT, " +
                    $"fresh={row.FreshSteelRequirementMt:0.####} MT, shortfall={row.UncoveredSteelFeedShortfallMt:0.####} MT.");
        }

        // Run only Campaign compatibility/grouping logic. All supply pools are removed so the legacy service cannot
        // reserve inventory, committed receipts or external supply a second time. The legacy material answer is then
        // replaced by the canonical answer below; heat construction still sees FreshSteelQuantityMt and therefore
        // applies configured casting yield/furnace envelopes exactly once.
        var groupingRequest = request with
        {
            Inventory = Array.Empty<InventoryPosition>(),
            ExternalMaterialSupplies = Array.Empty<ExternalMaterialSupply>(),
            CommittedMaterialSupplies = Array.Empty<CommittedMaterialSupply>(),
            MaterialSupplyPolicy = new MaterialSupplyPlanningPolicy(
                AllowInternalMake: true,
                AllowExternalBuy: false,
                AllowTransfer: false,
                AllowManualSupply: false),
            MaterialSourcingRules = Array.Empty<MaterialSourcingRule>(),
            PrecomputedMaterialDemand = null
        };

        var grouped = campaignPlanning.FormCampaigns(groupingRequest);

        foreach (var campaign in grouped.Campaigns)
        {
            campaign.ExistingIntermediateInventoryMt = 0m;
            campaign.FreshSteelRequirementMt = 0m;

            foreach (var poGroup in campaign.Allocations.GroupBy(x => x.ProductionOrderId))
            {
                var row = rows[poGroup.Key];
                var allocationsForPo = poGroup.ToArray();
                var totalRolling = allocationsForPo.Sum(x => x.PlannedQuantityMt);
                if (totalRolling <= QuantityToleranceMt) continue;

                decimal assignedCovered = 0m;
                decimal assignedFresh = 0m;
                for (var index = 0; index < allocationsForPo.Length; index++)
                {
                    var allocation = allocationsForPo[index];
                    var isLast = index == allocationsForPo.Length - 1;
                    var ratio = allocation.PlannedQuantityMt / totalRolling;
                    var covered = isLast
                        ? row.CoveredIntermediateMt - assignedCovered
                        : decimal.Round(row.CoveredIntermediateMt * ratio, 4, MidpointRounding.AwayFromZero);
                    var fresh = isLast
                        ? row.FreshSteelRequirementMt - assignedFresh
                        : decimal.Round(row.FreshSteelRequirementMt * ratio, 4, MidpointRounding.AwayFromZero);

                    covered = Math.Max(0m, covered);
                    fresh = Math.Max(0m, fresh);
                    allocation.ExistingIntermediateInventoryMt = covered;
                    allocation.FreshSteelQuantityMt = fresh;
                    assignedCovered += covered;
                    assignedFresh += fresh;
                    campaign.ExistingIntermediateInventoryMt += covered;
                    campaign.FreshSteelRequirementMt += fresh;
                }
            }

            RebuildGradeSequenceAndHeats(campaign, request);
        }

        var rolling = rows.ToDictionary(x => x.Key, x => x.Value.RollingRequirementMt);
        var freshSteel = rows.ToDictionary(x => x.Key, x => x.Value.FreshSteelRequirementMt);
        var allocations = rows.Values
            .SelectMany(x => x.CoverageAllocations)
            .OrderBy(x => x.ProductionOrderId)
            .ThenBy(x => x.AvailableFromUtc ?? DateTime.MinValue)
            .ThenBy(x => x.MaterialCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var onHand = activeOrders.ToDictionary(
            x => x.Id,
            x => allocations
                .Where(a => a.ProductionOrderId == x.Id && a.Use == PlanningInventoryUse.IntermediateFeed)
                .Sum(a => a.QuantityMt));
        var external = activeOrders.ToDictionary(
            x => x.Id,
            x => allocations
                .Where(a => a.ProductionOrderId == x.Id && a.Use == PlanningInventoryUse.ExternalIntermediateFeed)
                .Sum(a => a.QuantityMt));

        return new CampaignPlanningResult(
            grouped.Campaigns,
            Array.Empty<ProductionOrder>(),
            rolling,
            freshSteel,
            onHand,
            allocations,
            external,
            grouped.HeatAllocations,
            Array.Empty<PlanningSupplyAllocation>(),
            activeOrders.ToDictionary(x => x.Id, _ => 0m),
            activeOrders.ToDictionary(x => x.Id, _ => 0m),
            Array.Empty<PlanningSupplyAlternative>());
    }

    private static void RebuildGradeSequenceAndHeats(Campaign campaign, CampaignPlanningRequest request)
    {
        // CampaignPlanningService's heat builder is private. Rebuild only the quantity-dependent projection here,
        // preserving the same grade grouping semantics. Production EAF envelopes are enforced later by configured
        // structure/resource feasibility; this adapter does not invent alternate furnace capacities.
        campaign.GradeSequence.Clear();
        campaign.Heats.Clear();

        var groups = campaign.Allocations
            .Where(x => x.ProductionOrder is not null && x.FreshSteelQuantityMt > QuantityToleranceMt)
            .GroupBy(x => x.ProductionOrder!.GradeCode)
            .OrderBy(x => x.Min(a => a.ProductionOrder!.RequiredDate))
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var gradeSequenceNo = 1;
        var heatSequenceNo = 1;
        foreach (var group in groups)
        {
            var productionOrders = group.Select(x => x.ProductionOrder!).DistinctBy(x => x.Id).ToArray();
            var grade = productionOrders.Select(x => x.SteelGrade).FirstOrDefault(x => x is not null);
            var yieldPct = grade?.ProcessRequirements
                               .FirstOrDefault(x => x.ProcessOperationType == ProcessOperationType.Ccm)
                               ?.ExpectedYieldPct
                           ?? request.Policy.ExpectedCastingYieldPct;
            if (yieldPct <= 0m || yieldPct > 100m)
                throw new InvalidOperationException($"Grade {group.Key} has invalid casting yield {yieldPct}.");

            var requiredCastOutput = group.Sum(x => x.FreshSteelQuantityMt);
            var liquidInput = decimal.Round(requiredCastOutput / (yieldPct / 100m), 4, MidpointRounding.AwayFromZero);
            var gradeSequence = new CampaignGradeSequence
            {
                CampaignId = campaign.Id,
                Campaign = campaign,
                SequenceNumber = gradeSequenceNo++,
                GradeCode = group.Key,
                PlannedQuantityMt = liquidInput
            };
            campaign.GradeSequence.Add(gradeSequence);

            foreach (var heatQty in DistributeHeatQuantities(liquidInput, request.Policy))
            {
                campaign.Heats.Add(new CampaignHeat
                {
                    CampaignId = campaign.Id,
                    Campaign = campaign,
                    CampaignGradeSequenceId = gradeSequence.Id,
                    CampaignGradeSequence = gradeSequence,
                    SequenceNumber = heatSequenceNo++,
                    GradeCode = group.Key,
                    PlannedQuantityMt = heatQty,
                    MinimumFeasibleQuantityMt = request.Policy.MinimumHeatSizeMt,
                    TargetQuantityMt = request.Policy.NominalHeatSizeMt,
                    MaximumFeasibleQuantityMt = request.Policy.MaximumHeatSizeMt
                });
            }
        }
    }

    private static IReadOnlyCollection<decimal> DistributeHeatQuantities(decimal totalMt, CampaignPlanningPolicy policy)
    {
        if (totalMt <= QuantityToleranceMt) return Array.Empty<decimal>();
        var max = Math.Max(policy.MaximumHeatSizeMt, QuantityToleranceMt);
        var min = Math.Max(0m, policy.MinimumHeatSizeMt);
        var count = Math.Max(1, (int)Math.Ceiling(totalMt / max));
        while (count > 1 && totalMt / count < min) count--;

        var baseQty = decimal.Round(totalMt / count, 4, MidpointRounding.AwayFromZero);
        var result = Enumerable.Repeat(baseQty, count).ToArray();
        result[^1] += totalMt - result.Sum();
        if (result.Any(x => x < min - QuantityToleranceMt || x > max + QuantityToleranceMt))
            throw new InvalidOperationException($"Canonical fresh-steel requirement {totalMt:0.####} MT cannot be split within configured heat range {min:0.####}-{max:0.####} MT.");
        return result;
    }
}
