using APS.Application;
using APS.Domain;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace APS.Infrastructure;

public sealed class ProductionDemandOrchestrationService(
    ApsDbContext db,
    ILogger<ProductionDemandOrchestrationService> logger) : IProductionDemandOrchestrationService
{
    private const decimal QuantityToleranceMt = 0.0001m;
    private static readonly HashSet<string> ClosedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "CANCELLED", "CANCELED", "CLOSED", "COMPLETE", "COMPLETED", "DELETED", "REJECTED"
    };

    public async Task<SalesOrderReconciliationResult> ReconcileSalesOrdersAsync(
        IReadOnlyCollection<SalesOrderDemandInput> salesOrders,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(salesOrders);
        var validator = new SalesOrderDemandInputValidator();
        foreach (var row in salesOrders)
            await validator.ValidateAndThrowAsync(row, cancellationToken);

        var orderedInputs = salesOrders
            .OrderBy(x => x.CustomerRequiredDate)
            .ThenBy(x => x.SalesOrderNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ItemNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var salesOrderNumbers = orderedInputs
            .Select(x => x.SalesOrderNumber.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var itemNumbers = orderedInputs
            .Select(x => x.ItemNumber.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var existingOrders = salesOrderNumbers.Length == 0
            ? new List<SalesOrder>()
            : await db.SalesOrders
                .Where(x => salesOrderNumbers.Contains(x.SalesOrderNumber) && itemNumbers.Contains(x.ItemNumber))
                .ToListAsync(cancellationToken);
        var orderByKey = existingOrders.ToDictionary(
            x => SalesOrderKey(x.SalesOrderNumber, x.ItemNumber),
            StringComparer.Ordinal);
        var existingOrderIds = existingOrders.Select(x => x.Id).ToArray();

        var existingStates = existingOrderIds.Length == 0
            ? new List<SalesOrderDemandState>()
            : await db.SalesOrderDemandStates
                .Where(x => existingOrderIds.Contains(x.SalesOrderId))
                .ToListAsync(cancellationToken);
        var stateBySalesOrderId = existingStates.ToDictionary(x => x.SalesOrderId);

        var existingProfiles = existingOrderIds.Length == 0
            ? new List<SalesOrderRequirementProfile>()
            : await db.SalesOrderRequirementProfiles
                .Include(x => x.ChemistryOverrides)
                .Include(x => x.ProcessOverrides)
                .AsSplitQuery()
                .Where(x => existingOrderIds.Contains(x.SalesOrderId))
                .ToListAsync(cancellationToken);
        var profileBySalesOrderId = existingProfiles.ToDictionary(x => x.SalesOrderId);

        var created = 0;
        var updated = 0;
        var unchanged = 0;
        var closed = 0;
        var ids = new List<Guid>(orderedInputs.Length);

        foreach (var input in orderedInputs)
        {
            var key = SalesOrderKey(input.SalesOrderNumber, input.ItemNumber);
            var isNew = !orderByKey.TryGetValue(key, out var order);
            order ??= new SalesOrder
            {
                SalesOrderNumber = input.SalesOrderNumber.Trim(),
                ItemNumber = input.ItemNumber.Trim(),
                MaterialCode = input.MaterialCode.Trim(),
                GradeCode = input.GradeCode.Trim(),
                FinalCrossSectionCode = input.FinalCrossSectionCode.Trim(),
                RequiredDate = input.CustomerRequiredDate
            };

            var normalizedOpen = IsClosed(input.ExternalStatus) ? 0m : Math.Max(0m, input.OpenQuantityMt);
            var changed = isNew ||
                          !Same(order.MaterialCode, input.MaterialCode) ||
                          !Same(order.GradeCode, input.GradeCode) ||
                          !Same(order.FinalCrossSectionCode, input.FinalCrossSectionCode) ||
                          order.OrderQuantityMt != input.OrderQuantityMt ||
                          order.OpenQuantityMt != normalizedOpen ||
                          order.RequiredDate != input.CustomerRequiredDate ||
                          !Same(order.CustomerCode, input.CustomerCode) ||
                          !Same(order.CustomerGroupCode, input.CustomerGroupCode) ||
                          !Same(order.ExternalStatus, input.ExternalStatus);

            order.MaterialCode = input.MaterialCode.Trim();
            order.GradeCode = input.GradeCode.Trim();
            order.FinalCrossSectionCode = input.FinalCrossSectionCode.Trim();
            order.OrderQuantityMt = input.OrderQuantityMt;
            order.OpenQuantityMt = normalizedOpen;
            order.RequiredDate = input.CustomerRequiredDate;
            order.CustomerCode = Normalize(input.CustomerCode);
            order.CustomerGroupCode = Normalize(input.CustomerGroupCode);
            order.ExternalStatus = Normalize(input.ExternalStatus);

            if (isNew)
            {
                db.SalesOrders.Add(order);
                orderByKey[key] = order;
                created++;
            }
            else if (changed)
            {
                updated++;
            }
            else
            {
                unchanged++;
            }

            if (!stateBySalesOrderId.TryGetValue(order.Id, out var state))
            {
                state = new SalesOrderDemandState
                {
                    SalesOrderId = order.Id,
                    SalesOrder = order,
                    CustomerRequiredDate = input.CustomerRequiredDate,
                    ProductionRequiredByDate = input.CustomerRequiredDate,
                    Disposition = DemandReconciliationDisposition.Unchanged
                };
                db.SalesOrderDemandStates.Add(state);
                stateBySalesOrderId[order.Id] = state;
            }

            state.OpenDemandQuantityMt = normalizedOpen;
            state.CustomerRequiredDate = input.CustomerRequiredDate;
            state.ConfirmedDeliveryDate = input.ConfirmedDeliveryDate;
            state.Priority = Math.Max(0, input.Priority);
            state.CalculatedOnUtc = DateTime.UtcNow;

            profileBySalesOrderId.TryGetValue(order.Id, out var profile);
            profile = ReconcileRequirementProfile(order, input, profile);
            if (profile is null)
                profileBySalesOrderId.Remove(order.Id);
            else
                profileBySalesOrderId[order.Id] = profile;

            if (IsClosed(input.ExternalStatus)) closed++;
            ids.Add(order.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Reconciled {SalesOrderCount} sales-order items. Created={Created} Updated={Updated} Unchanged={Unchanged} Closed={Closed}",
            salesOrders.Count, created, updated, unchanged, closed);

        return new SalesOrderReconciliationResult(created, updated, unchanged, closed, ids);
    }

    public async Task<DemandOrchestrationResult> PrepareAsync(
        PlanningDemandSelection selection,
        IReadOnlyCollection<InventoryPosition> inventory,
        PlanningMasterDataSnapshot masters,
        DateTime referenceTimeUtc,
        DateTime horizonEndUtc,
        CancellationToken cancellationToken = default)
    {
        await new PlanningDemandSelectionValidator().ValidateAndThrowAsync(selection, cancellationToken);
        var requiredThrough = selection.RequiredThroughUtc ?? horizonEndUtc;
        var servicePolicy = selection.ServiceDatePolicy ?? new DemandServiceDatePolicy();
        var selectedIds = selection.SalesOrderIds is { Count: > 0 }
            ? selection.SalesOrderIds.ToHashSet()
            : null;

        var activeMtoQuery = db.ProductionOrders
            .Where(x => x.DemandSource == DemandSourceType.MakeToOrder &&
                        x.SalesOrderId.HasValue &&
                        x.Status != ProductionOrderStatus.Completed &&
                        x.Status != ProductionOrderStatus.Cancelled);
        if (selectedIds is not null)
            activeMtoQuery = activeMtoQuery.Where(x => selectedIds.Contains(x.SalesOrderId!.Value));

        var activeMtoSalesOrderIds = await activeMtoQuery
            .Select(x => x.SalesOrderId!.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        var query = db.SalesOrders.AsQueryable();
        if (selectedIds is not null) query = query.Where(x => selectedIds.Contains(x.Id));
        query = query.Where(x =>
            (x.OpenQuantityMt > 0m && x.RequiredDate <= requiredThrough) ||
            activeMtoSalesOrderIds.Contains(x.Id));

        var salesOrders = await query
            .OrderBy(x => x.RequiredDate)
            .ThenBy(x => x.SalesOrderNumber)
            .ThenBy(x => x.ItemNumber)
            .ToListAsync(cancellationToken);

        var salesOrderIds = salesOrders.Select(x => x.Id).ToArray();
        var states = salesOrderIds.Length == 0
            ? new List<SalesOrderDemandState>()
            : await db.SalesOrderDemandStates
                .Include(x => x.FinishedGoodsCoverage)
                .Where(x => salesOrderIds.Contains(x.SalesOrderId))
                .ToListAsync(cancellationToken);
        var stateBySalesOrder = states.ToDictionary(x => x.SalesOrderId);

        var requirementProfiles = salesOrderIds.Length == 0
            ? new List<SalesOrderRequirementProfile>()
            : await db.SalesOrderRequirementProfiles
                .Include(x => x.ChemistryOverrides)
                .Include(x => x.ProcessOverrides)
                .AsSplitQuery()
                .Where(x => salesOrderIds.Contains(x.SalesOrderId))
                .ToListAsync(cancellationToken);
        var requirementBySalesOrder = requirementProfiles.ToDictionary(x => x.SalesOrderId);

        var existingMto = salesOrderIds.Length == 0
            ? new List<ProductionOrder>()
            : await db.ProductionOrders
                .Include(x => x.Requirement!).ThenInclude(x => x.ChemistryOverrides)
                .Include(x => x.Requirement!).ThenInclude(x => x.ProcessOverrides)
                .Include(x => x.SalesOrder)
                .AsSplitQuery()
                .Where(x => x.DemandSource == DemandSourceType.MakeToOrder &&
                            x.SalesOrderId.HasValue && salesOrderIds.Contains(x.SalesOrderId.Value))
                .ToListAsync(cancellationToken);
        var poBySalesOrder = existingMto
            .GroupBy(x => x.SalesOrderId!.Value)
            .ToDictionary(x => x.Key, x => x.ToArray());

        var fgPool = BuildFinishedGoodsPool(inventory);
        var issues = new List<PlanningIssue>();
        var items = new List<DemandOrchestrationItem>();
        var activeMto = new List<ProductionOrder>();

        var ordered = salesOrders
            .OrderByDescending(so => stateBySalesOrder.TryGetValue(so.Id, out var state) ? state.Priority : 0)
            .ThenBy(so => ServiceDate(so, stateBySalesOrder.GetValueOrDefault(so.Id)))
            .ThenBy(so => so.SalesOrderNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(so => so.ItemNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var so in ordered)
        {
            if (!stateBySalesOrder.TryGetValue(so.Id, out var state))
            {
                state = new SalesOrderDemandState
                {
                    SalesOrderId = so.Id,
                    SalesOrder = so,
                    CustomerRequiredDate = so.RequiredDate,
                    ProductionRequiredByDate = so.RequiredDate,
                    Disposition = DemandReconciliationDisposition.Unchanged
                };
                db.SalesOrderDemandStates.Add(state);
                stateBySalesOrder[so.Id] = state;
            }

            requirementBySalesOrder.TryGetValue(so.Id, out var requirementProfile);
            var serviceDate = ServiceDate(so, state);
            var productionRequiredBy = servicePolicy.ProductionRequiredBy(serviceDate);
            var coverage = AllocateFinishedGoods(so, serviceDate, fgPool, requirementProfile);
            var openDemand = Math.Max(0m, so.OpenQuantityMt);
            var covered = coverage.Sum(x => x.QuantityMt);
            var manufacturingRequirement = Math.Max(0m, openDemand - covered);
            var priority = Math.Max(0, state.Priority);

            state.OpenDemandQuantityMt = openDemand;
            state.FinishedGoodsCoveredQuantityMt = covered;
            state.ManufacturingRequirementQuantityMt = manufacturingRequirement;
            state.CustomerRequiredDate = so.RequiredDate;
            state.ProductionRequiredByDate = productionRequiredBy;
            state.CalculatedOnUtc = referenceTimeUtc;
            state.PlannerAttentionRequired = false;
            state.ReasonCode = null;
            ReplaceCoverage(state, coverage);

            var active = poBySalesOrder.GetValueOrDefault(so.Id)?
                .Where(x => x.Status is not ProductionOrderStatus.Completed and not ProductionOrderStatus.Cancelled)
                .OrderByDescending(x => x.Status == ProductionOrderStatus.Released)
                .ThenByDescending(x => x.Status == ProductionOrderStatus.Firmed)
                .ToArray() ?? Array.Empty<ProductionOrder>();

            if (active.Length > 1)
            {
                state.Disposition = DemandReconciliationDisposition.PlannerAttentionRequired;
                state.PlannerAttentionRequired = true;
                state.ReasonCode = "MULTIPLE_ACTIVE_MTO_PRODUCTION_ORDERS";
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "MULTIPLE_ACTIVE_MTO_PRODUCTION_ORDERS",
                    $"SO {so.SalesOrderNumber}/{so.ItemNumber} has {active.Length} active MTO Production Orders; automatic reconciliation is unsafe.",
                    so.Id));
                activeMto.AddRange(active);
                items.Add(ToItem(so, state, requirementProfile));
                continue;
            }

            var po = active.SingleOrDefault();
            if (manufacturingRequirement <= QuantityToleranceMt)
            {
                if (po is null)
                {
                    state.ProductionOrderId = null;
                    state.ProductionOrder = null;
                    state.Disposition = DemandReconciliationDisposition.FullyCoveredByFinishedGoods;
                    state.ReasonCode = openDemand <= QuantityToleranceMt
                        ? "SO_HAS_NO_OPEN_DEMAND"
                        : "FG_COVERS_OPEN_DEMAND";
                }
                else if (po.Status == ProductionOrderStatus.Planned)
                {
                    po.Status = ProductionOrderStatus.Cancelled;
                    po.RemainingQuantityMt = 0m;
                    state.ProductionOrderId = po.Id;
                    state.ProductionOrder = po;
                    state.Disposition = DemandReconciliationDisposition.ProductionOrderCancelled;
                    state.ReasonCode = openDemand <= QuantityToleranceMt
                        ? "PLANNED_MTO_CANCELLED_AFTER_SO_CLOSED"
                        : "PLANNED_MTO_NO_LONGER_REQUIRED";
                }
                else
                {
                    ProtectCommittedPo(state, po, openDemand <= QuantityToleranceMt
                        ? "COMMITTED_MTO_REMAINS_AFTER_SO_CLOSED"
                        : "COMMITTED_MTO_NOW_EXCEEDS_CURRENT_MANUFACTURING_REQUIREMENT");
                    activeMto.Add(po);
                }
            }
            else if (po is null)
            {
                var resolved = ResolveManufacturingDefinition(so, requirementProfile, masters, issues);
                if (resolved is not null)
                {
                    po = CreateProductionOrder(so, requirementProfile, resolved, manufacturingRequirement, productionRequiredBy, priority, existingMto);
                    db.ProductionOrders.Add(po);
                    existingMto.Add(po);
                    state.ProductionOrderId = po.Id;
                    state.ProductionOrder = po;
                    state.Disposition = DemandReconciliationDisposition.ProductionOrderCreated;
                    state.ReasonCode = requirementProfile?.QualificationFingerprint is not null
                        ? "MTO_CREATED_AFTER_CONSERVATIVE_CERTIFIED_FG_QUALIFICATION"
                        : "MTO_CREATED_FROM_UNCOVERED_SO_DEMAND";
                    activeMto.Add(po);
                }
            }
            else if (po.Status == ProductionOrderStatus.Planned)
            {
                var resolved = ResolveManufacturingDefinition(so, requirementProfile, masters, issues);
                if (resolved is not null)
                {
                    UpdatePlannedProductionOrder(po, so, requirementProfile, resolved, manufacturingRequirement, productionRequiredBy, priority);
                    state.ProductionOrderId = po.Id;
                    state.ProductionOrder = po;
                    state.Disposition = DemandReconciliationDisposition.ProductionOrderUpdated;
                    state.ReasonCode = "PLANNED_MTO_RECONCILED_TO_CURRENT_SO_AND_FG_COVERAGE";
                    activeMto.Add(po);
                }
            }
            else
            {
                var mismatch = Math.Abs(po.RemainingQuantityMt - manufacturingRequirement) > QuantityToleranceMt ||
                               po.RequiredDate != productionRequiredBy ||
                               !Same(po.MaterialCode, so.MaterialCode) ||
                               !Same(po.GradeCode, so.GradeCode) ||
                               !Same(po.FinalCrossSectionCode, so.FinalCrossSectionCode) ||
                               !RequirementsEquivalent(po.Requirement, requirementProfile);
                if (mismatch)
                    ProtectCommittedPo(state, po, "COMMITTED_MTO_DIFFERS_FROM_CURRENT_SO_FG_OR_REQUIREMENT_DERIVATION");
                else
                {
                    state.ProductionOrderId = po.Id;
                    state.ProductionOrder = po;
                    state.Disposition = DemandReconciliationDisposition.CommittedProductionOrderProtected;
                    state.ReasonCode = "COMMITTED_MTO_MATCHES_CURRENT_DEMAND";
                }
                activeMto.Add(po);
            }

            items.Add(ToItem(so, state, requirementProfile));
        }

        await db.SaveChangesAsync(cancellationToken);

        var mts = selection.IncludeMakeToStock
            ? await db.ProductionOrders
                .Include(x => x.Requirement!).ThenInclude(x => x.ChemistryOverrides)
                .Include(x => x.Requirement!).ThenInclude(x => x.ProcessOverrides)
                .AsSplitQuery()
                .Where(x => x.DemandSource == DemandSourceType.MakeToStock &&
                            x.Status != ProductionOrderStatus.Cancelled &&
                            x.Status != ProductionOrderStatus.Completed &&
                            x.RemainingQuantityMt > 0m &&
                            x.RequiredDate <= requiredThrough)
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.RequiredDate)
                .ToListAsync(cancellationToken)
            : new List<ProductionOrder>();

        var productionOrders = activeMto
            .Concat(mts)
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.DemandSource == DemandSourceType.MakeToOrder ? 0 : 1)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.RequiredDate)
            .ThenBy(x => x.ProductionOrderNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        logger.LogInformation(
            "Prepared manufacturing demand at {ReferenceTimeUtc}. SOs={SalesOrderCount} MTO={MtoCount} MTS={MtsCount} FGCoveredMt={FgCoveredMt} ManufacturingMt={ManufacturingMt} CertifiedDemand={CertifiedDemandCount} Attention={AttentionCount}",
            referenceTimeUtc,
            items.Count,
            activeMto.DistinctBy(x => x.Id).Count(),
            mts.Count,
            items.Sum(x => x.FinishedGoodsCoveredQuantityMt),
            items.Sum(x => x.ManufacturingRequirementQuantityMt),
            items.Count(x => x.RequiresCertifiedFinishedGoodsMatch),
            items.Count(x => x.PlannerAttentionRequired));

        return new DemandOrchestrationResult(productionOrders, items, mts, issues);
    }

    public async Task<IReadOnlyCollection<DemandOrchestrationItem>> GetCurrentMtoDemandAsync(
        CancellationToken cancellationToken = default)
    {
        var states = await db.SalesOrderDemandStates
            .AsNoTracking()
            .Include(x => x.SalesOrder)
            .Include(x => x.ProductionOrder)
            .Include(x => x.FinishedGoodsCoverage)
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.ProductionRequiredByDate)
            .ToListAsync(cancellationToken);
        var ids = states.Select(x => x.SalesOrderId).ToArray();
        var profiles = ids.Length == 0
            ? new List<SalesOrderRequirementProfile>()
            : await db.SalesOrderRequirementProfiles.AsNoTracking()
                .Where(x => ids.Contains(x.SalesOrderId))
                .ToListAsync(cancellationToken);
        var profileBySalesOrder = profiles.ToDictionary(x => x.SalesOrderId);

        return states
            .Where(x => x.SalesOrder is not null)
            .Select(x =>
            {
                profileBySalesOrder.TryGetValue(x.SalesOrderId, out var profile);
                return ToItem(x.SalesOrder!, x, profile);
            })
            .ToArray();
    }

    private SalesOrderRequirementProfile? ReconcileRequirementProfile(
        SalesOrder order,
        SalesOrderDemandInput input,
        SalesOrderRequirementProfile? profile)
    {
        if (input.Requirement is null)
        {
            if (profile is not null) db.SalesOrderRequirementProfiles.Remove(profile);
            return null;
        }

        profile ??= new SalesOrderRequirementProfile
        {
            SalesOrderId = order.Id,
            SalesOrder = order
        };
        if (db.Entry(profile).State == EntityState.Detached) db.SalesOrderRequirementProfiles.Add(profile);

        var requirement = input.Requirement;
        profile.QualityClassCode = Normalize(requirement.QualityClassCode);
        profile.SegregationPolicy = requirement.SegregationPolicy;
        profile.RequireVd = requirement.RequireVd;
        profile.ForbidVd = requirement.ForbidVd;
        profile.RequireReheating = requirement.RequireReheating;
        profile.ForbidHotCharge = requirement.ForbidHotCharge;
        profile.RequireTmt = requirement.RequireTmt;
        profile.RequiredRouteCode = Normalize(requirement.RequiredRouteCode);
        profile.RequiredResourceId = requirement.RequiredResourceId;
        profile.RequiredResourceGroupCode = Normalize(requirement.RequiredResourceGroupCode);
        profile.MinimumSuperheatC = requirement.MinimumSuperheatC;
        profile.TargetSuperheatC = requirement.TargetSuperheatC;
        profile.MaximumSuperheatC = requirement.MaximumSuperheatC;
        profile.MinimumCastingTemperatureC = requirement.MinimumCastingTemperatureC;
        profile.MaximumCastingTemperatureC = requirement.MaximumCastingTemperatureC;
        profile.CutLengthM = requirement.CutLengthM;
        profile.TargetBundleWeightMt = requirement.TargetBundleWeightMt;
        profile.MinimumBundleWeightMt = requirement.MinimumBundleWeightMt;
        profile.MaximumBundleWeightMt = requirement.MaximumBundleWeightMt;
        profile.TargetCoilWeightMt = requirement.TargetCoilWeightMt;
        profile.MinimumCoilWeightMt = requirement.MinimumCoilWeightMt;
        profile.MaximumCoilWeightMt = requirement.MaximumCoilWeightMt;
        profile.AllowMixedHeatBundle = requirement.AllowMixedHeatBundle;
        profile.MarkingRequirementCode = Normalize(requirement.MarkingRequirementCode);
        profile.InspectionRequirementCode = Normalize(requirement.InspectionRequirementCode);
        profile.QualificationFingerprint = SalesOrderRequirementFingerprint.Compute(input, requirement);

        profile.ChemistryOverrides.Clear();
        foreach (var chemistry in requirement.ChemistryOverrides ?? Array.Empty<SalesOrderChemistryRequirementInput>())
        {
            profile.ChemistryOverrides.Add(new SalesOrderChemistryRequirement
            {
                SalesOrderRequirementProfileId = profile.Id,
                SalesOrderRequirementProfile = profile,
                ElementCode = chemistry.ElementCode.Trim(),
                MinimumPct = chemistry.MinimumPct,
                TargetPct = chemistry.TargetPct,
                MaximumPct = chemistry.MaximumPct
            });
        }

        profile.ProcessOverrides.Clear();
        foreach (var process in requirement.ProcessOverrides ?? Array.Empty<SalesOrderProcessRequirementInput>())
        {
            profile.ProcessOverrides.Add(new SalesOrderProcessRequirement
            {
                SalesOrderRequirementProfileId = profile.Id,
                SalesOrderRequirementProfile = profile,
                ProcessOperationType = process.ProcessOperationType,
                Requirement = process.Requirement,
                CapabilityClassCode = Normalize(process.CapabilityClassCode),
                RequiredResourceId = process.RequiredResourceId,
                MaximumQueueMinutes = process.MaximumQueueMinutes
            });
        }

        return profile;
    }

    private static List<FinishedGoodsPoolRow> BuildFinishedGoodsPool(IReadOnlyCollection<InventoryPosition> inventory) =>
        inventory
            .Where(x => x.Stage == InventoryStage.FinishedGoods &&
                        x.QualityStatus is MaterialQualityStatus.Available or MaterialQualityStatus.Released &&
                        x.ProjectedAvailableQuantityMt > QuantityToleranceMt)
            .Select(x => new FinishedGoodsPoolRow(x, x.ProjectedAvailableQuantityMt))
            .OrderBy(x => x.Position.AvailableFromUtc ?? DateTime.MinValue)
            .ThenBy(x => x.Position.LocationCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyCollection<DemandCoverageEvidence> AllocateFinishedGoods(
        SalesOrder so,
        DateTime serviceDate,
        IReadOnlyCollection<FinishedGoodsPoolRow> pool,
        SalesOrderRequirementProfile? requirementProfile)
    {
        if (!string.IsNullOrWhiteSpace(requirementProfile?.QualificationFingerprint))
            return Array.Empty<DemandCoverageEvidence>();

        var remaining = Math.Max(0m, so.OpenQuantityMt);
        var result = new List<DemandCoverageEvidence>();
        foreach (var row in pool)
        {
            if (remaining <= QuantityToleranceMt) break;
            if (row.RemainingQuantityMt <= QuantityToleranceMt) continue;
            var position = row.Position;
            if (!Same(position.MaterialCode, so.MaterialCode) ||
                !Same(position.GradeCode, so.GradeCode) ||
                !Same(position.CrossSectionCode, so.FinalCrossSectionCode)) continue;
            if (position.AvailableFromUtc.HasValue && position.AvailableFromUtc.Value > serviceDate) continue;

            var quantity = Math.Min(remaining, row.RemainingQuantityMt);
            row.RemainingQuantityMt -= quantity;
            remaining -= quantity;
            result.Add(new DemandCoverageEvidence(
                position.MaterialCode,
                position.GradeCode,
                position.CrossSectionCode,
                position.LocationCode,
                position.AvailableFromUtc,
                position.QualityStatus,
                quantity));
        }
        return result;
    }

    private void ReplaceCoverage(SalesOrderDemandState state, IReadOnlyCollection<DemandCoverageEvidence> coverage)
    {
        // Entity.Id is assigned client-side (Guid.NewGuid() in the property initializer), so a brand
        // new SalesOrderFinishedGoodsCoverage already carries a "real" key by the time EF sees it.
        // Appending it only to the navigation collection lets EF's graph-fixup heuristic decide the
        // tracking state, and with a pre-set key it infers Modified (an update to an existing row)
        // rather than Added - which then fails as a concurrency exception because that row was never
        // inserted. Track additions/removals through the DbContext explicitly instead of relying on
        // collection-navigation fixup to guess correctly.
        foreach (var existing in state.FinishedGoodsCoverage.ToArray())
        {
            state.FinishedGoodsCoverage.Remove(existing);
            db.SalesOrderFinishedGoodsCoverage.Remove(existing);
        }

        foreach (var item in coverage)
        {
            var row = new SalesOrderFinishedGoodsCoverage
            {
                SalesOrderDemandStateId = state.Id,
                SalesOrderDemandState = state,
                MaterialCode = item.MaterialCode,
                GradeCode = item.GradeCode,
                CrossSectionCode = item.CrossSectionCode,
                LocationCode = item.LocationCode,
                AvailableFromUtc = item.AvailableFromUtc,
                QualityStatus = item.QualityStatus,
                QuantityMt = item.QuantityMt
            };
            state.FinishedGoodsCoverage.Add(row);
            db.SalesOrderFinishedGoodsCoverage.Add(row);
        }
    }

    private static ManufacturingDefinition? ResolveManufacturingDefinition(
        SalesOrder so,
        SalesOrderRequirementProfile? requirementProfile,
        PlanningMasterDataSnapshot masters,
        ICollection<PlanningIssue> issues)
    {
        var grade = masters.EffectiveSteelGrades
            .FirstOrDefault(x => x.IsActive && Same(x.GradeCode, so.GradeCode));
        if (grade is null)
        {
            issues.Add(new PlanningIssue(
                PlanningIssueSeverity.Error,
                "SO_GRADE_MASTER_MISSING",
                $"SO {so.SalesOrderNumber}/{so.ItemNumber} references grade {so.GradeCode}, but no active steel-grade master exists.",
                so.Id));
            return null;
        }

        var material = masters.EffectiveMaterialSpecifications.FirstOrDefault(x =>
            x.IsActive && (Same(x.MaterialSpecificationCode, so.MaterialCode) || Same(x.SapMaterialCode, so.MaterialCode)));
        var casterSection = Normalize(grade.DefaultCasterSectionCode);
        var routeCode = Normalize(requirementProfile?.RequiredRouteCode) ?? Normalize(grade.DefaultRouteCode);

        if (string.IsNullOrWhiteSpace(casterSection))
        {
            issues.Add(new PlanningIssue(
                PlanningIssueSeverity.Error,
                "SO_CASTER_SECTION_UNRESOLVED",
                $"SO {so.SalesOrderNumber}/{so.ItemNumber} cannot derive an MTO PO because grade {so.GradeCode} has no default caster section.",
                so.Id));
        }
        if (string.IsNullOrWhiteSpace(routeCode))
        {
            issues.Add(new PlanningIssue(
                PlanningIssueSeverity.Error,
                "SO_ROUTE_UNRESOLVED",
                $"SO {so.SalesOrderNumber}/{so.ItemNumber} cannot derive an MTO PO because neither the SO requirement nor grade {so.GradeCode} resolves a manufacturing route.",
                so.Id));
        }
        if (!string.IsNullOrWhiteSpace(routeCode) &&
            !masters.RouteOperations.Any(x => Same(x.RouteCode, routeCode)))
        {
            issues.Add(new PlanningIssue(
                PlanningIssueSeverity.Error,
                "SO_ROUTE_MASTER_MISSING",
                $"SO {so.SalesOrderNumber}/{so.ItemNumber} resolved route {routeCode}, but no configured route operations exist for it.",
                so.Id));
        }
        if (string.IsNullOrWhiteSpace(casterSection) || string.IsNullOrWhiteSpace(routeCode)) return null;

        return new ManufacturingDefinition(grade, casterSection!, routeCode!, material?.ProductFamilyCode);
    }

    private static ProductionOrder CreateProductionOrder(
        SalesOrder so,
        SalesOrderRequirementProfile? sourceRequirement,
        ManufacturingDefinition definition,
        decimal manufacturingRequirement,
        DateTime productionRequiredBy,
        int priority,
        IReadOnlyCollection<ProductionOrder> historical)
    {
        var po = new ProductionOrder
        {
            ProductionOrderNumber = NextMtoNumber(so, historical),
            DemandSource = DemandSourceType.MakeToOrder,
            MaterialCode = so.MaterialCode,
            GradeCode = so.GradeCode,
            // FK only, deliberately no SteelGrade navigation assignment: definition.Grade comes from the
            // PlanningMasterDataSnapshot, which isn't tracked by this DbContext. Assigning the full
            // navigation reference pulls that untracked master-data object into the change tracker via
            // graph fixup, where EF's "does this look like an existing row" heuristic marks it Modified
            // rather than Added or Unchanged - and since it was never actually inserted, SaveChanges then
            // fails with a concurrency exception on read-only reference data that was never meant to be
            // written by demand orchestration at all.
            SteelGradeId = definition.Grade.Id,
            GradeFamilyCode = definition.Grade.GradeFamilyCode,
            GradeSequenceClassCode = definition.Grade.SequenceClassCode,
            FinalCrossSectionCode = so.FinalCrossSectionCode,
            CasterSectionCode = definition.CasterSectionCode,
            RouteCode = definition.RouteCode,
            ProductFamilyCode = definition.ProductFamilyCode,
            PlannedQuantityMt = manufacturingRequirement,
            RemainingQuantityMt = manufacturingRequirement,
            RequiredDate = productionRequiredBy,
            Priority = priority,
            Status = ProductionOrderStatus.Planned,
            SalesOrderId = so.Id,
            SalesOrder = so
        };
        po.Requirement = BuildProductionRequirement(po, so, sourceRequirement, definition.RouteCode);
        return po;
    }

    private static void UpdatePlannedProductionOrder(
        ProductionOrder po,
        SalesOrder so,
        SalesOrderRequirementProfile? sourceRequirement,
        ManufacturingDefinition definition,
        decimal manufacturingRequirement,
        DateTime productionRequiredBy,
        int priority)
    {
        po.MaterialCode = so.MaterialCode;
        po.GradeCode = so.GradeCode;
        po.SteelGradeId = definition.Grade.Id; // no navigation assignment - see CreateProductionOrder
        po.GradeFamilyCode = definition.Grade.GradeFamilyCode;
        po.GradeSequenceClassCode = definition.Grade.SequenceClassCode;
        po.FinalCrossSectionCode = so.FinalCrossSectionCode;
        po.CasterSectionCode = definition.CasterSectionCode;
        po.RouteCode = definition.RouteCode;
        po.ProductFamilyCode = definition.ProductFamilyCode;
        po.PlannedQuantityMt = manufacturingRequirement;
        po.RemainingQuantityMt = manufacturingRequirement;
        po.RequiredDate = productionRequiredBy;
        po.Priority = priority;
        po.SalesOrderId = so.Id;
        po.SalesOrder = so;

        po.Requirement ??= new ProductionOrderRequirement
        {
            ProductionOrderId = po.Id,
            ProductionOrder = po
        };
        ApplyProductionRequirement(po.Requirement, po, so, sourceRequirement, definition.RouteCode);
    }

    private static ProductionOrderRequirement BuildProductionRequirement(
        ProductionOrder po,
        SalesOrder so,
        SalesOrderRequirementProfile? source,
        string routeCode)
    {
        var requirement = new ProductionOrderRequirement
        {
            ProductionOrderId = po.Id,
            ProductionOrder = po
        };
        ApplyProductionRequirement(requirement, po, so, source, routeCode);
        return requirement;
    }

    private static void ApplyProductionRequirement(
        ProductionOrderRequirement requirement,
        ProductionOrder po,
        SalesOrder so,
        SalesOrderRequirementProfile? source,
        string routeCode)
    {
        requirement.ProductionOrderId = po.Id;
        requirement.ProductionOrder = po;
        requirement.CustomerCode = so.CustomerCode;
        requirement.CustomerGroupCode = so.CustomerGroupCode;
        requirement.RequirementReference = $"SO:{so.SalesOrderNumber}/{so.ItemNumber}";
        requirement.QualityClassCode = source?.QualityClassCode;
        requirement.SegregationPolicy = source?.SegregationPolicy ?? SegregationPolicy.None;
        requirement.RequireVd = source?.RequireVd;
        requirement.ForbidVd = source?.ForbidVd;
        requirement.RequireReheating = source?.RequireReheating;
        requirement.ForbidHotCharge = source?.ForbidHotCharge;
        requirement.RequireTmt = source?.RequireTmt;
        requirement.RequiredRouteCode = source?.RequiredRouteCode ?? routeCode;
        requirement.RequiredResourceId = source?.RequiredResourceId;
        requirement.RequiredResourceGroupCode = source?.RequiredResourceGroupCode;
        requirement.MinimumSuperheatC = source?.MinimumSuperheatC;
        requirement.TargetSuperheatC = source?.TargetSuperheatC;
        requirement.MaximumSuperheatC = source?.MaximumSuperheatC;
        requirement.MinimumCastingTemperatureC = source?.MinimumCastingTemperatureC;
        requirement.MaximumCastingTemperatureC = source?.MaximumCastingTemperatureC;
        requirement.CutLengthM = source?.CutLengthM;
        requirement.TargetBundleWeightMt = source?.TargetBundleWeightMt;
        requirement.MinimumBundleWeightMt = source?.MinimumBundleWeightMt;
        requirement.MaximumBundleWeightMt = source?.MaximumBundleWeightMt;
        requirement.TargetCoilWeightMt = source?.TargetCoilWeightMt;
        requirement.MinimumCoilWeightMt = source?.MinimumCoilWeightMt;
        requirement.MaximumCoilWeightMt = source?.MaximumCoilWeightMt;
        requirement.AllowMixedHeatBundle = source?.AllowMixedHeatBundle;
        requirement.MarkingRequirementCode = source?.MarkingRequirementCode;
        requirement.InspectionRequirementCode = source?.InspectionRequirementCode;

        requirement.ChemistryOverrides.Clear();
        foreach (var chemistry in source?.ChemistryOverrides ?? Array.Empty<SalesOrderChemistryRequirement>())
        {
            requirement.ChemistryOverrides.Add(new OrderChemistryRequirement
            {
                ProductionOrderRequirementId = requirement.Id,
                ElementCode = chemistry.ElementCode,
                MinimumPct = chemistry.MinimumPct,
                TargetPct = chemistry.TargetPct,
                MaximumPct = chemistry.MaximumPct
            });
        }
        requirement.ProcessOverrides.Clear();
        foreach (var process in source?.ProcessOverrides ?? Array.Empty<SalesOrderProcessRequirement>())
        {
            requirement.ProcessOverrides.Add(new OrderProcessRequirement
            {
                ProductionOrderRequirementId = requirement.Id,
                ProcessOperationType = process.ProcessOperationType,
                Requirement = process.Requirement,
                CapabilityClassCode = process.CapabilityClassCode,
                RequiredResourceId = process.RequiredResourceId,
                MaximumQueueMinutes = process.MaximumQueueMinutes
            });
        }
    }

    private static bool RequirementsEquivalent(ProductionOrderRequirement? po, SalesOrderRequirementProfile? source)
    {
        if (po is null && source is null) return true;
        if (po is null) return false;
        if (source is null)
        {
            return po.SegregationPolicy == SegregationPolicy.None &&
                   string.IsNullOrWhiteSpace(po.QualityClassCode) &&
                   po.RequireVd is null && po.ForbidVd is null && po.RequireReheating is null &&
                   po.ForbidHotCharge is null && po.RequireTmt is null &&
                   po.RequiredResourceId is null && string.IsNullOrWhiteSpace(po.RequiredResourceGroupCode) &&
                   po.ChemistryOverrides.Count == 0 && po.ProcessOverrides.Count == 0;
        }

        // RequiredRouteCode on a Production Order is the resolved manufacturing route. When the SO does
        // not explicitly override the route, the PO legitimately stores the grade/master-data default.
        // A null SO route override therefore must not make an otherwise unchanged firm/released PO look stale.
        var explicitRouteMismatch = !string.IsNullOrWhiteSpace(source.RequiredRouteCode) &&
                                    !Same(po.RequiredRouteCode, source.RequiredRouteCode);

        if (!Same(po.QualityClassCode, source.QualityClassCode) ||
            po.SegregationPolicy != source.SegregationPolicy ||
            po.RequireVd != source.RequireVd || po.ForbidVd != source.ForbidVd ||
            po.RequireReheating != source.RequireReheating || po.ForbidHotCharge != source.ForbidHotCharge ||
            po.RequireTmt != source.RequireTmt || explicitRouteMismatch ||
            po.RequiredResourceId != source.RequiredResourceId ||
            !Same(po.RequiredResourceGroupCode, source.RequiredResourceGroupCode) ||
            po.MinimumSuperheatC != source.MinimumSuperheatC || po.TargetSuperheatC != source.TargetSuperheatC || po.MaximumSuperheatC != source.MaximumSuperheatC ||
            po.MinimumCastingTemperatureC != source.MinimumCastingTemperatureC || po.MaximumCastingTemperatureC != source.MaximumCastingTemperatureC ||
            po.CutLengthM != source.CutLengthM || po.TargetBundleWeightMt != source.TargetBundleWeightMt ||
            po.MinimumBundleWeightMt != source.MinimumBundleWeightMt || po.MaximumBundleWeightMt != source.MaximumBundleWeightMt ||
            po.TargetCoilWeightMt != source.TargetCoilWeightMt || po.MinimumCoilWeightMt != source.MinimumCoilWeightMt ||
            po.MaximumCoilWeightMt != source.MaximumCoilWeightMt || po.AllowMixedHeatBundle != source.AllowMixedHeatBundle ||
            !Same(po.MarkingRequirementCode, source.MarkingRequirementCode) ||
            !Same(po.InspectionRequirementCode, source.InspectionRequirementCode))
            return false;

        var poChem = po.ChemistryOverrides.OrderBy(x => x.ElementCode, StringComparer.OrdinalIgnoreCase).ToArray();
        var soChem = source.ChemistryOverrides.OrderBy(x => x.ElementCode, StringComparer.OrdinalIgnoreCase).ToArray();
        if (poChem.Length != soChem.Length) return false;
        for (var i = 0; i < poChem.Length; i++)
            if (!Same(poChem[i].ElementCode, soChem[i].ElementCode) || poChem[i].MinimumPct != soChem[i].MinimumPct ||
                poChem[i].TargetPct != soChem[i].TargetPct || poChem[i].MaximumPct != soChem[i].MaximumPct)
                return false;

        var poProc = po.ProcessOverrides.OrderBy(x => x.ProcessOperationType).ThenBy(x => x.RequiredResourceId).ToArray();
        var soProc = source.ProcessOverrides.OrderBy(x => x.ProcessOperationType).ThenBy(x => x.RequiredResourceId).ToArray();
        if (poProc.Length != soProc.Length) return false;
        for (var i = 0; i < poProc.Length; i++)
            if (poProc[i].ProcessOperationType != soProc[i].ProcessOperationType ||
                poProc[i].Requirement != soProc[i].Requirement ||
                !Same(poProc[i].CapabilityClassCode, soProc[i].CapabilityClassCode) ||
                poProc[i].RequiredResourceId != soProc[i].RequiredResourceId ||
                poProc[i].MaximumQueueMinutes != soProc[i].MaximumQueueMinutes)
                return false;
        return true;
    }

    private static void ProtectCommittedPo(SalesOrderDemandState state, ProductionOrder po, string reasonCode)
    {
        state.ProductionOrderId = po.Id;
        state.ProductionOrder = po;
        state.Disposition = DemandReconciliationDisposition.CommittedProductionOrderProtected;
        state.PlannerAttentionRequired = true;
        state.ReasonCode = reasonCode;
    }

    private static DemandOrchestrationItem ToItem(
        SalesOrder so,
        SalesOrderDemandState state,
        SalesOrderRequirementProfile? requirement) => new(
        so.Id,
        so.SalesOrderNumber,
        so.ItemNumber,
        so.MaterialCode,
        so.GradeCode,
        so.FinalCrossSectionCode,
        so.CustomerCode,
        so.CustomerGroupCode,
        state.ProductionOrderId,
        state.ProductionOrder?.ProductionOrderNumber,
        state.OpenDemandQuantityMt,
        state.FinishedGoodsCoveredQuantityMt,
        state.ManufacturingRequirementQuantityMt,
        state.CustomerRequiredDate,
        state.ConfirmedDeliveryDate,
        state.ProductionRequiredByDate,
        state.Priority,
        state.Disposition,
        state.PlannerAttentionRequired,
        state.ReasonCode,
        state.FinishedGoodsCoverage.Select(x => new DemandCoverageEvidence(
            x.MaterialCode,
            x.GradeCode,
            x.CrossSectionCode,
            x.LocationCode,
            x.AvailableFromUtc,
            x.QualityStatus,
            x.QuantityMt)).ToArray(),
        requirement?.QualificationFingerprint,
        !string.IsNullOrWhiteSpace(requirement?.QualificationFingerprint));

    private static DateTime ServiceDate(SalesOrder so, SalesOrderDemandState? state) =>
        state?.ConfirmedDeliveryDate ?? so.RequiredDate;

    private static bool IsClosed(string? status) =>
        !string.IsNullOrWhiteSpace(status) && ClosedStatuses.Contains(status.Trim());

    private static string NextMtoNumber(SalesOrder so, IReadOnlyCollection<ProductionOrder> historical)
    {
        var baseNumber = $"MTO-{SafeToken(so.SalesOrderNumber)}-{SafeToken(so.ItemNumber)}";
        var same = historical.Where(x => x.SalesOrderId == so.Id).Select(x => x.ProductionOrderNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!same.Contains(baseNumber)) return baseNumber;
        var revision = 2;
        while (same.Contains($"{baseNumber}-R{revision}")) revision++;
        return $"{baseNumber}-R{revision}";
    }

    private static string SalesOrderKey(string salesOrderNumber, string itemNumber) =>
        $"{salesOrderNumber.Trim()}\u001f{itemNumber.Trim()}";

    private static string SafeToken(string value) =>
        new(value.Trim().Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class FinishedGoodsPoolRow(InventoryPosition position, decimal remainingQuantityMt)
    {
        public InventoryPosition Position { get; } = position;
        public decimal RemainingQuantityMt { get; set; } = remainingQuantityMt;
    }

    private sealed record ManufacturingDefinition(
        SteelGrade Grade,
        string CasterSectionCode,
        string RouteCode,
        string? ProductFamilyCode);
}
