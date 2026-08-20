using APS.Application;
using APS.Domain;

namespace APS.Planning;

public sealed class MtsProductionOrderService : IMtsProductionOrderService
{
    public MtsProductionOrderProposal Propose(StockPolicy policy, InventoryPosition inventory, decimal alreadyFirmedSupplyMt = 0m)
    {
        var projected = inventory.ProjectedAvailableQuantityMt + alreadyFirmedSupplyMt;
        var raw = Math.Max(0m, policy.TargetStockMt - projected);

        if (raw <= 0m)
            return new(null, projected, 0m, "Projected stock already meets or exceeds target stock.");

        var proposed = Math.Max(raw, policy.MinimumReplenishmentMt);
        if (policy.MaximumReplenishmentMt > 0m) proposed = Math.Min(proposed, policy.MaximumReplenishmentMt);

        var po = new ProductionOrder
        {
            ProductionOrderNumber = $"MTS-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
            DemandSource = DemandSourceType.MakeToStock,
            MaterialCode = policy.MaterialCode,
            GradeCode = policy.GradeCode,
            GradeSequenceClassCode = policy.GradeSequenceClassCode,
            FinalCrossSectionCode = policy.FinalCrossSectionCode,
            CasterSectionCode = policy.CasterSectionCode,
            RouteCode = policy.RouteCode,
            PlannedQuantityMt = proposed,
            RemainingQuantityMt = proposed,
            RequiredDate = policy.RequiredDate,
            Priority = policy.Priority,
            TargetStockMt = policy.TargetStockMt,
            ProjectedAvailableStockMt = projected,
            StockPolicyCode = policy.PolicyCode
        };

        return new(po, projected, proposed, "APS-generated MTS Production Order required to restore target stock.");
    }
}

public sealed class CampaignPlanningService : ICampaignPlanningService
{
    public CampaignPlanningResult FormCampaigns(CampaignPlanningRequest request)
    {
        ValidatePolicy(request.Policy);
        ResolveGradeMasters(request);

        var coveredByFinishedGoods = new List<ProductionOrder>();
        var rollingRequirements = new Dictionary<Guid, decimal>();
        var freshSteelRequirements = new Dictionary<Guid, decimal>();
        var intermediateAllocated = new Dictionary<Guid, decimal>();
        var committedInternalAllocated = new Dictionary<Guid, decimal>();
        var externalAllocated = new Dictionary<Guid, decimal>();
        var plannedPurchaseAllocated = new Dictionary<Guid, decimal>();
        var plannedTransferAllocated = new Dictionary<Guid, decimal>();
        var inventoryAllocations = new List<PlanningInventoryAllocation>();
        var plannedSupplyAllocations = new List<PlanningSupplyAllocation>();
        var sourcingAlternatives = new List<PlanningSupplyAlternative>();

        var finishedGoodsPools = request.Inventory
            .Where(i => i.Stage == InventoryStage.FinishedGoods &&
                        i.QualityStatus is MaterialQualityStatus.Available or MaterialQualityStatus.Released &&
                        i.ProjectedAvailableQuantityMt > 0m)
            .Select(i => new InventoryPool(i, i.ProjectedAvailableQuantityMt))
            .ToList();

        var intermediatePools = request.Inventory
            .Where(i => i.Stage is InventoryStage.CastIntermediate or InventoryStage.OtherIntermediate)
            .Where(i => i.QualityStatus is MaterialQualityStatus.Available or MaterialQualityStatus.Released)
            .Where(i => i.ProjectedAvailableQuantityMt > 0m)
            .Select(i => new InventoryPool(i, i.ProjectedAvailableQuantityMt))
            .ToList();

        // Baseline supply from a released/committed/running upstream operation is neither inventory nor
        // a new sourcing choice. It remains PO-pegged and enters material availability only at its ETA.
        var committedPools = (request.CommittedMaterialSupplies ?? Array.Empty<CommittedMaterialSupply>())
            .Where(x => x.QuantityMt > 0m)
            .Select(x => new CommittedSupplyPool(x, x.QuantityMt))
            .ToList();

        // Only genuinely confirmed/firm external supply is netted here. Planned BUY/TRANSFER supply
        // is created separately below and remains distinguishable from material already ordered/available.
        var externalPools = (request.ExternalMaterialSupplies ?? Array.Empty<ExternalMaterialSupply>())
            .Where(x => x.IsFirm &&
                        x.QualityStatus is MaterialQualityStatus.Available or MaterialQualityStatus.Released &&
                        x.QuantityMt - x.ReservedQuantityMt > 0m)
            .Select(x => new ExternalSupplyPool(x, x.QuantityMt - x.ReservedQuantityMt))
            .ToList();

        var ordered = request.ProductionOrders
            .Where(x => x.Status is ProductionOrderStatus.Planned or ProductionOrderStatus.Firmed)
            .OrderBy(x => x.DemandSource == DemandSourceType.MakeToOrder ? 0 : 1)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.RequiredDate)
            .ThenBy(x => x.ProductionOrderNumber)
            .ToArray();

        foreach (var po in ordered)
        {
            ValidateOrderRequirementAgainstGrade(po);
            var remaining = Math.Max(0m, po.RemainingQuantityMt);

            var fgUsed = AllocateInventory(
                po,
                remaining,
                finishedGoodsPools,
                position =>
                    Same(position.MaterialCode, po.MaterialCode) &&
                    Same(position.GradeCode, po.GradeCode) &&
                    Same(position.CrossSectionCode, po.FinalCrossSectionCode) &&
                    (!position.AvailableFromUtc.HasValue || position.AvailableFromUtc.Value <= po.RequiredDate),
                PlanningInventoryUse.FinishedGoodsFulfilment,
                inventoryAllocations);

            var rollingRequirement = remaining - fgUsed;
            rollingRequirements[po.Id] = rollingRequirement;

            if (rollingRequirement <= 0m)
            {
                coveredByFinishedGoods.Add(po);
                freshSteelRequirements[po.Id] = 0m;
                intermediateAllocated[po.Id] = 0m;
                committedInternalAllocated[po.Id] = 0m;
                externalAllocated[po.Id] = 0m;
                plannedPurchaseAllocated[po.Id] = 0m;
                plannedTransferAllocated[po.Id] = 0m;
                continue;
            }

            var intermediateUsed = AllocateInventory(
                po,
                rollingRequirement,
                intermediatePools,
                position =>
                    Same(position.GradeCode, po.GradeCode) &&
                    Same(position.CrossSectionCode, po.CasterSectionCode) &&
                    (!position.AvailableFromUtc.HasValue || position.AvailableFromUtc.Value <= po.RequiredDate),
                PlanningInventoryUse.IntermediateFeed,
                inventoryAllocations);

            var afterOnHand = rollingRequirement - intermediateUsed;
            var committedUsed = AllocateCommittedSupply(po, afterOnHand, committedPools, inventoryAllocations);
            var afterCommitted = afterOnHand - committedUsed;
            var externalUsed = AllocateExternalSupply(po, afterCommitted, externalPools, inventoryAllocations);
            var unresolved = Math.Max(0m, afterCommitted - externalUsed);
            var sourcing = SourceResidual(po, unresolved, request);

            intermediateAllocated[po.Id] = intermediateUsed;
            committedInternalAllocated[po.Id] = committedUsed;
            externalAllocated[po.Id] = externalUsed;
            freshSteelRequirements[po.Id] = sourcing.MakeQuantityMt;
            plannedPurchaseAllocated[po.Id] = sourcing.BuyQuantityMt;
            plannedTransferAllocated[po.Id] = sourcing.TransferQuantityMt;
            sourcingAlternatives.AddRange(sourcing.Alternatives);

            foreach (var allocation in sourcing.Allocations)
            {
                plannedSupplyAllocations.Add(allocation);
                if (allocation.ActionType is not (MaterialSupplyActionType.Buy or MaterialSupplyActionType.Transfer or MaterialSupplyActionType.Manual))
                    continue;

                var use = allocation.ActionType switch
                {
                    MaterialSupplyActionType.Buy => PlanningInventoryUse.PlannedPurchaseFeed,
                    MaterialSupplyActionType.Transfer => PlanningInventoryUse.PlannedTransferFeed,
                    _ => PlanningInventoryUse.ManualPlannedFeed
                };
                inventoryAllocations.Add(new PlanningInventoryAllocation(
                    po.Id,
                    InventoryStage.InTransit,
                    po.MaterialCode,
                    po.GradeCode,
                    po.CasterSectionCode,
                    allocation.DestinationLocationCode,
                    allocation.QuantityMt,
                    use,
                    allocation.SupplyReference,
                    allocation.ExpectedReceiptUtc));
            }
        }

        var campaignInputs = ordered
            .Where(po => rollingRequirements[po.Id] > 0m)
            .GroupBy(po => CampaignKey(po, request.Policy))
            .OrderBy(g => g.Min(x => x.RequiredDate));

        var campaigns = new List<Campaign>();
        var compositionDecisions = new List<CampaignCompositionDecision>();
        var weights = request.Policy.ObjectiveWeights ?? CampaignObjectiveWeights.Default;
        var sequence = 1;

        foreach (var group in campaignInputs)
        {
            var ordersById = group.ToDictionary(x => x.Id);
            var requirements = group
                .Select(po => new CampaignRequirement(
                    po.Id,
                    po.ProductionOrderNumber,
                    rollingRequirements[po.Id],
                    po.RequiredDate,
                    po.Priority,
                    po.DemandSource == DemandSourceType.MakeToOrder))
                .ToArray();

            // Which requirements share a campaign is a decision with a cost, not a consequence of the
            // order they happen to arrive in (#15). Candidate compositions are scored and the best is
            // taken; service risk dominates efficiency lexicographically.
            var composition = CampaignCandidateOptimizer.Choose(requirements, request.Policy, weights);
            compositionDecisions.Add(new CampaignCompositionDecision(
                group.Key.ToString(),
                composition.Score,
                CampaignCandidateOptimizer.Considered(requirements, request.Policy, weights)));

            // Coverage already netted per PO is drawn down across that PO's slices in campaign order,
            // so a PO split over two campaigns consumes its existing intermediate stock in the first.
            var remainingIntermediateByOrder = group.ToDictionary(
                po => po.Id,
                po => intermediateAllocated[po.Id] + committedInternalAllocated[po.Id] + externalAllocated[po.Id] +
                      plannedPurchaseAllocated[po.Id] + plannedTransferAllocated[po.Id] +
                      plannedSupplyAllocations
                          .Where(x => x.ProductionOrderId == po.Id && x.ActionType == MaterialSupplyActionType.Manual)
                          .Sum(x => x.QuantityMt));
            var remainingFreshByOrder = group.ToDictionary(po => po.Id, po => freshSteelRequirements[po.Id]);

            foreach (var candidate in composition.Campaigns)
            {
                var current = NewCampaign(request.CampaignNumberPrefix, sequence++, group.Key, candidate.RequiredDate);

                foreach (var slice in candidate.Slices)
                {
                    var po = ordersById[slice.Requirement.ProductionOrderId];
                    var allocationQty = slice.QuantityMt;
                    var intermediateQty = Math.Min(remainingIntermediateByOrder[po.Id], allocationQty);
                    var freshQty = Math.Min(remainingFreshByOrder[po.Id], Math.Max(0m, allocationQty - intermediateQty));

                    current.Allocations.Add(new CampaignAllocation
                    {
                        CampaignId = current.Id,
                        Campaign = current,
                        ProductionOrderId = po.Id,
                        ProductionOrder = po,
                        PlannedQuantityMt = allocationQty,
                        ExistingIntermediateInventoryMt = intermediateQty,
                        FreshSteelQuantityMt = freshQty
                    });

                    current.PlannedQuantityMt += allocationQty;
                    current.ExistingIntermediateInventoryMt += intermediateQty;
                    current.FreshSteelRequirementMt += freshQty;
                    current.RequiredDate = current.RequiredDate <= po.RequiredDate ? current.RequiredDate : po.RequiredDate;

                    remainingIntermediateByOrder[po.Id] = Math.Max(0m, remainingIntermediateByOrder[po.Id] - intermediateQty);
                    remainingFreshByOrder[po.Id] = Math.Max(0m, remainingFreshByOrder[po.Id] - freshQty);
                }

                if (current.PlannedQuantityMt <= 0m) continue;
                BuildGradeSequenceAndHeats(current, request);
                campaigns.Add(current);
            }
        }

        return new CampaignPlanningResult(
            campaigns,
            coveredByFinishedGoods,
            rollingRequirements,
            freshSteelRequirements,
            intermediateAllocated,
            inventoryAllocations,
            externalAllocated,
            null,
            plannedSupplyAllocations,
            plannedPurchaseAllocated,
            plannedTransferAllocated,
            sourcingAlternatives,
            compositionDecisions);
    }

    private static void ResolveGradeMasters(CampaignPlanningRequest request)
    {
        var gradeByCode = (request.SteelGrades ?? Array.Empty<SteelGrade>())
            .ToDictionary(x => x.GradeCode, StringComparer.OrdinalIgnoreCase);

        foreach (var po in request.ProductionOrders)
        {
            if (po.SteelGrade is null && gradeByCode.TryGetValue(po.GradeCode, out var grade))
            {
                po.SteelGrade = grade;
                po.SteelGradeId = grade.Id;
            }

            if (po.SteelGrade is null) continue;
            po.GradeFamilyCode ??= po.SteelGrade.GradeFamilyCode;
            po.GradeSequenceClassCode ??= po.SteelGrade.SequenceClassCode;
        }
    }

    private static void ValidateOrderRequirementAgainstGrade(ProductionOrder po)
    {
        if (po.SteelGrade is null || po.Requirement is null) return;

        var gradeVd = po.SteelGrade.ProcessRequirements
            .FirstOrDefault(x => x.ProcessOperationType == ProcessOperationType.Vd)?.Requirement;

        if (gradeVd == RequirementDisposition.Forbidden && po.Requirement.RequireVd == true)
            throw new InvalidOperationException($"Production Order {po.ProductionOrderNumber} requires VD but grade {po.GradeCode} forbids VD.");

        if (gradeVd == RequirementDisposition.Required && po.Requirement.ForbidVd == true)
            throw new InvalidOperationException($"Production Order {po.ProductionOrderNumber} forbids VD but grade {po.GradeCode} requires VD.");

        if (!string.IsNullOrWhiteSpace(po.Requirement.RequiredRouteCode) && !Same(po.Requirement.RequiredRouteCode, po.RouteCode))
            throw new InvalidOperationException($"Production Order {po.ProductionOrderNumber} requires route {po.Requirement.RequiredRouteCode} but is assigned route {po.RouteCode}.");
    }

    private static decimal AllocateInventory(
        ProductionOrder productionOrder,
        decimal requiredQuantityMt,
        IEnumerable<InventoryPool> pools,
        Func<InventoryPosition, bool> matches,
        PlanningInventoryUse use,
        ICollection<PlanningInventoryAllocation> allocations)
    {
        var allocated = 0m;
        foreach (var pool in pools
                     .Where(pool => pool.RemainingQuantityMt > 0m && matches(pool.Position))
                     .OrderBy(pool => pool.Position.AvailableFromUtc)
                     .ThenBy(pool => pool.Position.LocationCode)
                     .ThenBy(pool => pool.Position.MaterialCode))
        {
            var stillRequired = requiredQuantityMt - allocated;
            if (stillRequired <= 0m) break;
            var quantity = Math.Min(stillRequired, pool.RemainingQuantityMt);
            if (quantity <= 0m) continue;

            pool.RemainingQuantityMt -= quantity;
            allocated += quantity;
            allocations.Add(new PlanningInventoryAllocation(
                productionOrder.Id,
                pool.Position.Stage,
                pool.Position.MaterialCode,
                pool.Position.GradeCode,
                pool.Position.CrossSectionCode,
                pool.Position.LocationCode,
                quantity,
                use,
                null,
                pool.Position.AvailableFromUtc));
        }
        return allocated;
    }

    private static decimal AllocateCommittedSupply(
        ProductionOrder po,
        decimal requiredQuantityMt,
        IEnumerable<CommittedSupplyPool> pools,
        ICollection<PlanningInventoryAllocation> allocations)
    {
        var allocated = 0m;
        foreach (var pool in pools
                     .Where(x => x.RemainingQuantityMt > 0m &&
                                 x.Supply.ProductionOrderId == po.Id &&
                                 x.Supply.AvailableFromUtc <= po.RequiredDate &&
                                 Same(x.Supply.GradeCode, po.GradeCode) &&
                                 Same(x.Supply.CrossSectionCode, po.CasterSectionCode))
                     .OrderBy(x => x.Supply.AvailableFromUtc)
                     .ThenBy(x => x.Supply.SupplyReference))
        {
            var remaining = requiredQuantityMt - allocated;
            if (remaining <= 0m) break;
            var quantity = Math.Min(remaining, pool.RemainingQuantityMt);
            if (quantity <= 0m) continue;

            pool.RemainingQuantityMt -= quantity;
            allocated += quantity;
            allocations.Add(new PlanningInventoryAllocation(
                po.Id,
                InventoryStage.InTransit,
                pool.Supply.MaterialSpecificationCode ?? po.MaterialCode,
                pool.Supply.GradeCode,
                pool.Supply.CrossSectionCode,
                pool.Supply.LocationCode,
                quantity,
                PlanningInventoryUse.CommittedInternalProductionFeed,
                pool.Supply.SupplyReference,
                pool.Supply.AvailableFromUtc));
        }
        return allocated;
    }

    private static decimal AllocateExternalSupply(
        ProductionOrder po,
        decimal requiredQuantityMt,
        IEnumerable<ExternalSupplyPool> pools,
        ICollection<PlanningInventoryAllocation> allocations)
    {
        var allocated = 0m;
        foreach (var pool in pools
                     .Where(x => x.RemainingQuantityMt > 0m &&
                                 x.Supply.AvailableFromUtc <= po.RequiredDate &&
                                 Same(x.Supply.GradeCode, po.GradeCode) &&
                                 Same(x.Supply.CrossSectionCode, po.CasterSectionCode))
                     .OrderBy(x => x.Supply.UsagePenalty)
                     .ThenBy(x => x.Supply.AvailableFromUtc)
                     .ThenBy(x => x.Supply.SupplyReference))
        {
            var remaining = requiredQuantityMt - allocated;
            if (remaining <= 0m) break;
            var quantity = Math.Min(remaining, pool.RemainingQuantityMt);
            if (quantity <= 0m) continue;

            pool.RemainingQuantityMt -= quantity;
            allocated += quantity;
            allocations.Add(new PlanningInventoryAllocation(
                po.Id,
                InventoryStage.InTransit,
                pool.Supply.MaterialSpecificationCode ?? "EXTERNAL-BILLET",
                pool.Supply.GradeCode,
                pool.Supply.CrossSectionCode,
                pool.Supply.LocationCode,
                quantity,
                PlanningInventoryUse.ExternalIntermediateFeed,
                pool.Supply.SupplyReference,
                pool.Supply.AvailableFromUtc));
        }
        return allocated;
    }

    private static ResidualSourcingDecision SourceResidual(
        ProductionOrder po,
        decimal quantityMt,
        CampaignPlanningRequest request)
    {
        if (quantityMt <= 0m) return ResidualSourcingDecision.Empty;

        var policy = request.MaterialSupplyPolicy ?? new MaterialSupplyPlanningPolicy();
        var rule = SelectSourcingRule(po, request.MaterialSourcingRules);
        var allowMake = rule?.AllowMake ?? policy.AllowInternalMake;
        var allowBuy = rule?.AllowBuy ?? policy.AllowExternalBuy;
        var allowTransfer = rule?.AllowTransfer ?? policy.AllowTransfer;
        var allowManual = rule?.AllowManualSupply ?? policy.AllowManualSupply;
        var makePath = allowMake
            ? SteelmakingMakeFeasibilityEvaluator.Evaluate(po, request)
            : new MakePathFeasibility(false, "Internal MAKE is not approved by the sourcing rule/policy.", new Dictionary<ProcessOperationType, IReadOnlyCollection<Guid>>());
        var makeFeasible = allowMake && makePath.IsFeasible;
        var reference = request.PlanningReferenceTimeUtc ?? DateTime.UtcNow;

        var buyReceipt = CommercialReceiptQuantity(quantityMt, rule?.MinimumBuyQuantityMt, rule?.BuyOrderMultipleMt);
        var transferReceipt = Math.Max(quantityMt, rule?.MinimumTransferQuantityMt ?? quantityMt);
        var buyEta = reference + (rule?.PurchaseLeadTime ?? policy.DefaultExternalLeadTime ?? TimeSpan.Zero);
        var transferEta = reference + (rule?.TransferLeadTime ?? TimeSpan.Zero);

        var candidates = new List<SourcingCandidate>
        {
            new(
                MaterialSupplyActionType.Make,
                allowMake,
                makeFeasible,
                rule?.MakePenalty ?? 0,
                quantityMt,
                null,
                makeFeasible ? null : makePath.Explanation),
            new(
                MaterialSupplyActionType.Buy,
                allowBuy,
                allowBuy,
                rule?.BuyPenalty ?? 100,
                buyReceipt,
                buyEta,
                allowBuy ? null : "External BUY is not approved by the sourcing rule/policy."),
            new(
                MaterialSupplyActionType.Transfer,
                allowTransfer,
                allowTransfer,
                rule?.TransferPenalty ?? 50,
                transferReceipt,
                transferEta,
                allowTransfer ? null : "TRANSFER is not approved by the sourcing rule/policy."),
            new(
                MaterialSupplyActionType.Manual,
                allowManual,
                allowManual,
                1000,
                quantityMt,
                reference,
                allowManual ? null : "Manual/planner supply is not approved by the sourcing rule/policy.")
        };

        var preferred = rule?.PreferredAction ?? (makeFeasible ? MaterialSupplyActionType.Make : MaterialSupplyActionType.Buy);
        var feasibleCandidates = candidates.Where(x => x.IsAllowed && x.IsFeasible).ToArray();
        var serviceViable = feasibleCandidates
            .Where(x => !x.ExpectedReceiptUtc.HasValue || x.ExpectedReceiptUtc.Value <= po.RequiredDate)
            .ToArray();
        var candidatePool = serviceViable.Length > 0 ? serviceViable : feasibleCandidates;
        var selected = candidatePool.FirstOrDefault(x => x.Action == preferred)
                       ?? candidatePool
                           .OrderBy(x => x.Penalty)
                           .ThenBy(x => x.ExpectedReceiptUtc ?? DateTime.MinValue)
                           .ThenBy(x => x.Action)
                           .FirstOrDefault();

        if (selected is null)
        {
            var unsourced = new PlanningSupplyAllocation(
                po.Id,
                MaterialSupplyActionType.Unsourced,
                quantityMt,
                po.RequiredDate,
                null,
                null,
                null,
                null,
                rule?.DestinationLocationCode,
                false,
                rule?.RuleCode,
                0m,
                0m,
                int.MaxValue);

            var alternatives = candidates.Select(x => ToAlternative(po, quantityMt, rule, x, false)).Append(
                new PlanningSupplyAlternative(
                    po.Id,
                    MaterialSupplyActionType.Unsourced,
                    true,
                    true,
                    true,
                    quantityMt,
                    0m,
                    0m,
                    po.RequiredDate,
                    null,
                    int.MaxValue,
                    rule?.RuleCode,
                    DestinationLocationCode: rule?.DestinationLocationCode,
                    RejectionReason: "No approved feasible MAKE/BUY/TRANSFER/MANUAL path exists."))
                .ToArray();
            return new ResidualSourcingDecision(0m, 0m, 0m, new[] { unsourced }, alternatives);
        }

        var supplyReference = selected.Action switch
        {
            MaterialSupplyActionType.Buy => $"PLAN-BUY:{po.Id:N}",
            MaterialSupplyActionType.Transfer => $"PLAN-XFER:{po.Id:N}",
            MaterialSupplyActionType.Manual => $"PLAN-MANUAL:{po.Id:N}",
            _ => null
        };
        var excess = Math.Max(0m, selected.PlannedReceiptQuantityMt - quantityMt);
        var allocation = new PlanningSupplyAllocation(
            po.Id,
            selected.Action,
            quantityMt,
            po.RequiredDate,
            selected.ExpectedReceiptUtc,
            supplyReference,
            selected.Action == MaterialSupplyActionType.Buy ? rule?.PreferredSupplierCode : null,
            selected.Action == MaterialSupplyActionType.Transfer ? rule?.TransferSourceLocationCode : null,
            rule?.DestinationLocationCode,
            false,
            rule?.RuleCode,
            selected.PlannedReceiptQuantityMt,
            excess,
            selected.Penalty);

        var projectedAlternatives = candidates
            .Select(x => ToAlternative(po, quantityMt, rule, x, x.Action == selected.Action))
            .ToArray();

        return new ResidualSourcingDecision(
            selected.Action == MaterialSupplyActionType.Make ? quantityMt : 0m,
            selected.Action == MaterialSupplyActionType.Buy ? quantityMt : 0m,
            selected.Action == MaterialSupplyActionType.Transfer ? quantityMt : 0m,
            new[] { allocation },
            projectedAlternatives);
    }

    private static PlanningSupplyAlternative ToAlternative(
        ProductionOrder po,
        decimal requiredQuantityMt,
        MaterialSourcingRule? rule,
        SourcingCandidate candidate,
        bool selected)
    {
        var excess = Math.Max(0m, candidate.PlannedReceiptQuantityMt - requiredQuantityMt);
        return new PlanningSupplyAlternative(
            po.Id,
            candidate.Action,
            candidate.IsAllowed,
            candidate.IsFeasible,
            selected,
            requiredQuantityMt,
            candidate.PlannedReceiptQuantityMt,
            excess,
            po.RequiredDate,
            candidate.ExpectedReceiptUtc,
            candidate.Penalty,
            rule?.RuleCode,
            candidate.Action == MaterialSupplyActionType.Buy ? rule?.PreferredSupplierCode : null,
            candidate.Action == MaterialSupplyActionType.Transfer ? rule?.TransferSourceLocationCode : null,
            rule?.DestinationLocationCode,
            candidate.RejectionReason);
    }

    private static decimal CommercialReceiptQuantity(decimal required, decimal? minimum, decimal? multiple)
    {
        var quantity = Math.Max(required, minimum ?? required);
        if (multiple is > 0m) quantity = Math.Ceiling(quantity / multiple.Value) * multiple.Value;
        return decimal.Round(quantity, 4, MidpointRounding.AwayFromZero);
    }

    private static MaterialSourcingRule? SelectSourcingRule(
        ProductionOrder po,
        IReadOnlyCollection<MaterialSourcingRule>? rules)
    {
        return (rules ?? Array.Empty<MaterialSourcingRule>())
            .Where(x => x.IsActive &&
                        Matches(x.MaterialCode, po.MaterialCode) &&
                        Matches(x.GradeCode, po.GradeCode) &&
                        Matches(x.GradeFamilyCode, po.GradeFamilyCode) &&
                        Matches(x.CrossSectionCode, po.CasterSectionCode))
            .OrderByDescending(x => Specificity(x, po))
            .ThenBy(x => x.RuleCode, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static int Specificity(MaterialSourcingRule rule, ProductionOrder po)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(rule.MaterialCode) && Same(rule.MaterialCode, po.MaterialCode)) score += 16;
        if (!string.IsNullOrWhiteSpace(rule.GradeCode) && Same(rule.GradeCode, po.GradeCode)) score += 8;
        if (!string.IsNullOrWhiteSpace(rule.GradeFamilyCode) && Same(rule.GradeFamilyCode, po.GradeFamilyCode ?? string.Empty)) score += 4;
        if (!string.IsNullOrWhiteSpace(rule.CrossSectionCode) && Same(rule.CrossSectionCode, po.CasterSectionCode)) score += 2;
        if (rule.ProductForm == SteelProductForm.Billet) score += 1;
        return score;
    }

    private static CampaignCompatibilityKey CampaignKey(ProductionOrder po, CampaignPlanningPolicy policy)
    {
        var sequenceClass = SequenceClass(po);
        var gradePartition = policy.AllowMixedGradesWithinSequenceClass ? "*" : po.GradeCode;
        var demandPartition = policy.AllowMtoMtsMixing ? "*" : po.DemandSource.ToString();
        return new(sequenceClass, po.CasterSectionCode, po.RouteCode, gradePartition, demandPartition, CampaignSegregationPartition(po));
    }

    private static string CampaignSegregationPartition(ProductionOrder po)
    {
        var requirement = po.Requirement;
        if (requirement is null) return "*";
        return requirement.SegregationPolicy switch
        {
            SegregationPolicy.DedicatedCampaign => $"PO:{po.Id:N}",
            SegregationPolicy.SameSalesOrderOnly => $"SO:{po.SalesOrderId?.ToString("N") ?? po.Id.ToString("N")}",
            SegregationPolicy.SameCustomerOnly => $"CUSTOMER:{requirement.CustomerCode ?? po.SalesOrder?.CustomerCode ?? "UNKNOWN"}",
            _ => "*"
        };
    }

    private static Campaign NewCampaign(string prefix, int sequence, CampaignCompatibilityKey key, DateTime requiredDate) => new()
    {
        CampaignNumber = $"{prefix}-{sequence:00000}",
        GradeSequenceClassCode = key.GradeSequenceClassCode,
        CasterSectionCode = key.CasterSectionCode,
        RouteCode = key.RouteCode,
        PlannedQuantityMt = 0m,
        FreshSteelRequirementMt = 0m,
        ExistingIntermediateInventoryMt = 0m,
        RequiredDate = requiredDate,
        Status = CampaignStatus.Draft
    };

    private static void BuildGradeSequenceAndHeats(Campaign campaign, CampaignPlanningRequest request)
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
                {
                    for (var i = 0; i < counts[envelopeIndex]; i++)
                    {
                        var envelope = envelopes[envelopeIndex];
                        items.Add(new MutableHeatPlan(envelope, Math.Clamp(envelope.TargetMt, envelope.MinimumMt, envelope.MaximumMt)));
                    }
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
        var requiredResourceIds = productionOrders
            .SelectMany(x => RequiredEafResources(x))
            .Distinct()
            .ToArray();

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
        // A quantity can fall in a dead band where no heat count satisfies both ends of the envelope -
        // 70 MT against 40/55 min/max needs more than one heat but cannot fill two. Math.Clamp throws
        // when its bounds cross, which crashed the whole plan with an arithmetic message. The furnace
        // maximum is a physical limit and the minimum is an economic one, so the maximum wins and the
        // campaign runs an under-filled heat.
        var heatCount = minimumCount > maximumCount
            ? minimumCount
            : Math.Clamp(preferredCount, minimumCount, maximumCount);
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
            requirement.QualityClassCode ?? "",
            requirement.SegregationPolicy,
            requirement.RequireVd,
            requirement.ForbidVd,
            requirement.RequireReheating,
            requirement.ForbidHotCharge,
            requirement.RequireTmt,
            requirement.RequiredRouteCode ?? "",
            requirement.RequiredResourceId,
            requirement.MinimumSuperheatC,
            requirement.TargetSuperheatC,
            requirement.MaximumSuperheatC,
            chemistry,
            processes);
    }

    private static void ValidatePolicy(CampaignPlanningPolicy policy)
    {
        if (policy.NominalHeatSizeMt <= 0m) throw new ArgumentOutOfRangeException(nameof(policy.NominalHeatSizeMt));
        if (policy.MinimumHeatSizeMt <= 0m || policy.MinimumHeatSizeMt > policy.NominalHeatSizeMt)
            throw new ArgumentOutOfRangeException(nameof(policy.MinimumHeatSizeMt));
        if (policy.MaximumHeatSizeMt < policy.NominalHeatSizeMt)
            throw new ArgumentOutOfRangeException(nameof(policy.MaximumHeatSizeMt));
        if (policy.MaximumCampaignQuantityMt < policy.MaximumHeatSizeMt)
            throw new ArgumentOutOfRangeException(nameof(policy.MaximumCampaignQuantityMt));
        if (policy.ExpectedCastingYieldPct <= 0m || policy.ExpectedCastingYieldPct > 100m)
            throw new ArgumentOutOfRangeException(nameof(policy.ExpectedCastingYieldPct));
    }

    private static string SequenceClass(ProductionOrder po) =>
        po.SteelGrade?.SequenceClassCode
        ?? (string.IsNullOrWhiteSpace(po.GradeSequenceClassCode) ? $"GRADE:{po.GradeCode}" : po.GradeSequenceClassCode);

    private static bool Matches(string? configured, string? actual) =>
        string.IsNullOrWhiteSpace(configured) || string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private sealed class InventoryPool(InventoryPosition position, decimal remainingQuantityMt)
    {
        public InventoryPosition Position { get; } = position;
        public decimal RemainingQuantityMt { get; set; } = remainingQuantityMt;
    }

    private sealed class CommittedSupplyPool(CommittedMaterialSupply supply, decimal remainingQuantityMt)
    {
        public CommittedMaterialSupply Supply { get; } = supply;
        public decimal RemainingQuantityMt { get; set; } = remainingQuantityMt;
    }

    private sealed class ExternalSupplyPool(ExternalMaterialSupply supply, decimal remainingQuantityMt)
    {
        public ExternalMaterialSupply Supply { get; } = supply;
        public decimal RemainingQuantityMt { get; set; } = remainingQuantityMt;
    }

    private sealed record CampaignCompatibilityKey(
        string GradeSequenceClassCode,
        string CasterSectionCode,
        string RouteCode,
        string GradePartition,
        string DemandPartition,
        string SegregationPartition);

    private sealed record HeatCompatibilityKey(string GradeCode, string RequirementSignature);
    private sealed record FurnaceEnvelope(Guid ResourceId, decimal MinimumMt, decimal TargetMt, decimal MaximumMt);
    private sealed record HeatQuantityPlan(decimal QuantityMt, decimal MinimumMt, decimal TargetMt, decimal MaximumMt);
    private sealed record HeatPlanCandidate(IReadOnlyList<HeatQuantityPlan> Heats, decimal Score);
    private sealed record SourcingCandidate(
        MaterialSupplyActionType Action,
        bool IsAllowed,
        bool IsFeasible,
        int Penalty,
        decimal PlannedReceiptQuantityMt,
        DateTime? ExpectedReceiptUtc,
        string? RejectionReason);

    private sealed record ResidualSourcingDecision(
        decimal MakeQuantityMt,
        decimal BuyQuantityMt,
        decimal TransferQuantityMt,
        IReadOnlyCollection<PlanningSupplyAllocation> Allocations,
        IReadOnlyCollection<PlanningSupplyAlternative> Alternatives)
    {
        public static ResidualSourcingDecision Empty { get; } = new(
            0m,
            0m,
            0m,
            Array.Empty<PlanningSupplyAllocation>(),
            Array.Empty<PlanningSupplyAlternative>());
    }

    private sealed class MutableHeatPlan(FurnaceEnvelope envelope, decimal quantityMt)
    {
        public FurnaceEnvelope Envelope { get; } = envelope;
        public decimal QuantityMt { get; set; } = quantityMt;
    }
}
