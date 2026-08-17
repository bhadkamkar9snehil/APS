using APS.Domain;

namespace APS.Planning;

internal static class CampaignHeatAllocationBuilder
{
    public static IReadOnlyCollection<CampaignHeatAllocation> Build(IReadOnlyCollection<Campaign> campaigns)
    {
        var result = new List<CampaignHeatAllocation>();
        foreach (var campaign in campaigns)
        {
            var source = campaign.Allocations.ToList();
            var groups = source
                .Where(x => x.ProductionOrder is not null && x.FreshSteelQuantityMt > 0m)
                .GroupBy(x => new HeatDemandKey(x.ProductionOrder!.GradeCode, Signature(x.ProductionOrder)))
                .Select(g => new HeatDemandGroup(
                    g.Key,
                    source.FindIndex(x => ReferenceEquals(x, g.First())),
                    g.GroupBy(x => x.ProductionOrderId)
                        .Select(o => new OrderDemand(o.Key, o.First().ProductionOrder!, o.Sum(x => x.FreshSteelQuantityMt)))
                        .OrderBy(x => x.ProductionOrder.DemandSource == DemandSourceType.MakeToOrder ? 0 : 1)
                        .ThenByDescending(x => x.ProductionOrder.Priority)
                        .ThenBy(x => x.ProductionOrder.RequiredDate)
                        .ThenBy(x => x.ProductionOrder.ProductionOrderNumber)
                        .ToArray()))
                .OrderBy(x => x.FirstAllocationIndex)
                .ToArray();

            var sequences = campaign.GradeSequence.OrderBy(x => x.SequenceNumber).ToArray();
            if (sequences.Length != groups.Length)
                throw new InvalidOperationException($"Campaign {campaign.CampaignNumber} heat-demand partition mismatch.");

            for (var i = 0; i < groups.Length; i++)
                AllocateSequence(campaign, sequences[i], groups[i], result);
        }
        return result;
    }

    private static void AllocateSequence(Campaign campaign, CampaignGradeSequence sequence, HeatDemandGroup group, ICollection<CampaignHeatAllocation> result)
    {
        if (!string.Equals(sequence.GradeCode, group.Key.GradeCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Campaign {campaign.CampaignNumber} grade-sequence allocation mismatch.");

        var heats = campaign.Heats.Where(x => x.CampaignGradeSequenceId == sequence.Id).OrderBy(x => x.SequenceNumber).ToArray();
        var totalInput = heats.Sum(x => x.PlannedQuantityMt);
        var totalOutput = group.Orders.Sum(x => x.OutputQuantityMt);
        if (heats.Length == 0 || totalInput <= 0m || totalOutput <= 0m) return;

        var remaining = group.Orders.ToDictionary(x => x.ProductionOrderId, x => x.OutputQuantityMt);
        var emittedOutput = 0m;
        for (var h = 0; h < heats.Length; h++)
        {
            var heat = heats[h];
            var heatOutput = h == heats.Length - 1
                ? totalOutput - emittedOutput
                : decimal.Round(heat.PlannedQuantityMt / totalInput * totalOutput, 4, MidpointRounding.AwayFromZero);
            emittedOutput += heatOutput;

            var portions = new List<(OrderDemand Order, decimal OutputMt)>();
            var unallocated = heatOutput;
            foreach (var order in group.Orders)
            {
                if (unallocated <= 0m) break;
                var available = remaining[order.ProductionOrderId];
                if (available <= 0m) continue;
                var output = Math.Min(available, unallocated);
                remaining[order.ProductionOrderId] = available - output;
                unallocated -= output;
                portions.Add((order, output));
            }
            if (unallocated > 0.0001m)
                throw new InvalidOperationException($"Campaign {campaign.CampaignNumber} heat {heat.SequenceNumber} is not fully pegged.");

            var emittedInput = 0m;
            for (var p = 0; p < portions.Count; p++)
            {
                var portion = portions[p];
                var input = p == portions.Count - 1
                    ? heat.PlannedQuantityMt - emittedInput
                    : decimal.Round(portion.OutputMt / heatOutput * heat.PlannedQuantityMt, 4, MidpointRounding.AwayFromZero);
                emittedInput += input;
                result.Add(new CampaignHeatAllocation
                {
                    CampaignHeatId = heat.Id,
                    CampaignHeat = heat,
                    ProductionOrderId = portion.Order.ProductionOrderId,
                    ProductionOrder = portion.Order.ProductionOrder,
                    PlannedOutputQuantityMt = portion.OutputMt,
                    PlannedInputQuantityMt = input
                });
            }
        }
    }

    private static string Signature(ProductionOrder po)
    {
        var r = po.Requirement;
        if (r is null) return "*";
        var chemistry = string.Join(';', r.ChemistryOverrides.OrderBy(x => x.ElementCode).Select(x => $"{x.ElementCode}:{x.MinimumPct}:{x.TargetPct}:{x.MaximumPct}"));
        var processes = string.Join(';', r.ProcessOverrides.OrderBy(x => x.ProcessOperationType).ThenBy(x => x.RequiredResourceId).Select(x => $"{x.ProcessOperationType}:{x.Requirement}:{x.CapabilityClassCode}:{x.RequiredResourceId}:{x.MaximumQueueMinutes}"));
        return string.Join('|', r.QualityClassCode ?? "", r.SegregationPolicy, r.RequireVd, r.ForbidVd, r.RequireReheating, r.ForbidHotCharge, r.RequireTmt, r.RequiredRouteCode ?? "", r.RequiredResourceId, r.MinimumSuperheatC, r.TargetSuperheatC, r.MaximumSuperheatC, chemistry, processes);
    }

    private sealed record HeatDemandKey(string GradeCode, string RequirementSignature);
    private sealed record OrderDemand(Guid ProductionOrderId, ProductionOrder ProductionOrder, decimal OutputQuantityMt);
    private sealed record HeatDemandGroup(HeatDemandKey Key, int FirstAllocationIndex, IReadOnlyList<OrderDemand> Orders);
}
