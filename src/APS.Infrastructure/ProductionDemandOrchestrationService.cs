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

        var created = 0;
        var updated = 0;
        var unchanged = 0;
        var closed = 0;
        var ids = new List<Guid>();

        foreach (var input in salesOrders
                     .OrderBy(x => x.CustomerRequiredDate)
                     .ThenBy(x => x.SalesOrderNumber, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.ItemNumber, StringComparer.OrdinalIgnoreCase))
        {
            var order = await db.SalesOrders
                .SingleOrDefaultAsync(x =>
                    x.SalesOrderNumber == input.SalesOrderNumber && x.ItemNumber == input.ItemNumber,
                    cancellationToken);

            var isNew = order is null;
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

            var state = await db.SalesOrderDemandStates
                .SingleOrDefaultAsync(x => x.SalesOrderId == order.Id, cancellationToken);
            if (state is null)
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
            }

            state.OpenDemandQuantityMt = normalizedOpen;
            state.CustomerRequiredDate = input.CustomerRequiredDate;
            state.ConfirmedDeliveryDate = input.ConfirmedDeliveryDate;
            state.Priority = Math.Max(0, input.Priority);
            state.CalculatedOnUtc = DateTime.UtcNow;
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

        // Active MTOs are deliberately included even when current SO open quantity is now zero or the due
        // date moved outside the current horizon. That is the only safe way to cancel a Planned PO or flag
        // a Firmed/Released PO for planner attention after an SAP demand change.
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

        var existingMto = salesOrderIds.Length == 0
            ? new List<ProductionOrder>()
            : await db.ProductionOrders
                .Include(x => x.Requirement)!.ThenInclude(x => x.ChemistryOverrides)
                .Include(x => x.Requirement)!.ThenInclude(x => x.ProcessOverrides)
                .Include(x => x.SalesOrder)
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

            var serviceDate = ServiceDate(so, state);
            var productionRequiredBy = servicePolicy.ProductionRequiredBy(serviceDate);
            var coverage = AllocateFinishedGoods(so, serviceDate, fgPool);
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
                items.Add(ToItem(so, state));
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
                var resolved = ResolveManufacturingDefinition(so, masters, issues);
                if (resolved is not null)
                {
                    po = CreateProductionOrder(so, resolved, manufacturingRequirement, productionRequiredBy, priority, existingMto);
                    db.ProductionOrders.Add(po);
                    existingMto.Add(po);
                    state.ProductionOrderId = po.Id;
                    state.ProductionOrder = po;
                    state.Disposition = DemandReconciliationDisposition.ProductionOrderCreated;
                    state.ReasonCode = "MTO_CREATED_FROM_UNCOVERED_SO_DEMAND";
                    activeMto.Add(po);
                }
            }
            else if (po.Status == ProductionOrderStatus.Planned)
            {
                var resolved = ResolveManufacturingDefinition(so, masters, issues);
                if (resolved is not null)
                {
                    UpdatePlannedProductionOrder(po, so, resolved, manufacturingRequirement, productionRequiredBy, priority);
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
                               !Same(po.FinalCrossSectionCode, so.FinalCrossSectionCode);
                if (mismatch)
                    ProtectCommittedPo(state, po, "COMMITTED_MTO_DIFFERS_FROM_CURRENT_SO_OR_FG_DERIVATION");
                else
                {
                    state.ProductionOrderId = po.Id;
                    state.ProductionOrder = po;
                    state.Disposition = DemandReconciliationDisposition.CommittedProductionOrderProtected;
                    state.ReasonCode = "COMMITTED_MTO_MATCHES_CURRENT_DEMAND";
                }
                activeMto.Add(po);
            }

            items.Add(ToItem(so, state));
        }

        await db.SaveChangesAsync(cancellationToken);

        var mts = selection.IncludeMakeToStock
            ? await db.ProductionOrders
                .Include(x => x.Requirement)!.ThenInclude(x => x.ChemistryOverrides)
                .Include(x => x.Requirement)!.ThenInclude(x => x.ProcessOverrides)
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
            "Prepared manufacturing demand at {ReferenceTimeUtc}. SOs={SalesOrderCount} MTO={MtoCount} MTS={MtsCount} FGCoveredMt={FgCoveredMt} ManufacturingMt={ManufacturingMt} Attention={AttentionCount}",
            referenceTimeUtc,
            items.Count,
            activeMto.DistinctBy(x => x.Id).Count(),
            mts.Count,
            items.Sum(x => x.FinishedGoodsCoveredQuantityMt),
            items.Sum(x => x.ManufacturingRequirementQuantityMt),
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

        return states
            .Where(x => x.SalesOrder is not null)
            .Select(x => ToItem(x.SalesOrder!, x))
            .ToArray();
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
        IReadOnlyCollection<FinishedGoodsPoolRow> pool)
    {
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

    private static void ReplaceCoverage(SalesOrderDemandState state, IReadOnlyCollection<DemandCoverageEvidence> coverage)
    {
        state.FinishedGoodsCoverage.Clear();
        foreach (var item in coverage)
        {
            state.FinishedGoodsCoverage.Add(new SalesOrderFinishedGoodsCoverage
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
            });
        }
    }

    private static ManufacturingDefinition? ResolveManufacturingDefinition(
        SalesOrder so,
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
        var routeCode = Normalize(grade.DefaultRouteCode);

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
                $"SO {so.SalesOrderNumber}/{so.ItemNumber} cannot derive an MTO PO because grade {so.GradeCode} has no default manufacturing route.",
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

        return new ManufacturingDefinition(
            grade,
            casterSection!,
            routeCode!,
            material?.ProductFamilyCode);
    }

    private static ProductionOrder CreateProductionOrder(
        SalesOrder so,
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
            SteelGradeId = definition.Grade.Id,
            SteelGrade = definition.Grade,
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
        po.Requirement = new ProductionOrderRequirement
        {
            ProductionOrderId = po.Id,
            ProductionOrder = po,
            CustomerCode = so.CustomerCode,
            CustomerGroupCode = so.CustomerGroupCode,
            RequirementReference = $"SO:{so.SalesOrderNumber}/{so.ItemNumber}",
            RequiredRouteCode = definition.RouteCode
        };
        return po;
    }

    private static void UpdatePlannedProductionOrder(
        ProductionOrder po,
        SalesOrder so,
        ManufacturingDefinition definition,
        decimal manufacturingRequirement,
        DateTime productionRequiredBy,
        int priority)
    {
        po.MaterialCode = so.MaterialCode;
        po.GradeCode = so.GradeCode;
        po.SteelGradeId = definition.Grade.Id;
        po.SteelGrade = definition.Grade;
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
        po.Requirement.CustomerCode = so.CustomerCode;
        po.Requirement.CustomerGroupCode = so.CustomerGroupCode;
        po.Requirement.RequirementReference ??= $"SO:{so.SalesOrderNumber}/{so.ItemNumber}";
        po.Requirement.RequiredRouteCode ??= definition.RouteCode;
    }

    private static void ProtectCommittedPo(SalesOrderDemandState state, ProductionOrder po, string reasonCode)
    {
        state.ProductionOrderId = po.Id;
        state.ProductionOrder = po;
        state.Disposition = DemandReconciliationDisposition.CommittedProductionOrderProtected;
        state.PlannerAttentionRequired = true;
        state.ReasonCode = reasonCode;
    }

    private static DemandOrchestrationItem ToItem(SalesOrder so, SalesOrderDemandState state) => new(
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
            x.QuantityMt)).ToArray());

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
