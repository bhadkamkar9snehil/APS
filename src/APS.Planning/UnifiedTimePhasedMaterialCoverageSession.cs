using APS.Application;
using APS.Domain;

namespace APS.Planning;

/// <summary>
/// Run-scoped material availability ledger used before Campaign formation.
/// Every pool is mutable inside one planning run, so the same physical/committed quantity can be reserved only once.
/// Only supply available by the requirement time covers that requirement. Future matching supply is reported as
/// late evidence but is not consumed/reserved early, so it remains available to later requirements.
/// </summary>
public sealed class UnifiedTimePhasedMaterialCoverageSession : IMaterialCoverageSession
{
    private const decimal QuantityTolerance = 0.0000001m;
    private readonly List<Pool> _pools;

    public UnifiedTimePhasedMaterialCoverageSession(
        DateTime referenceTimeUtc,
        IReadOnlyCollection<InventoryPosition> inventory,
        IReadOnlyCollection<MaterialSpecification> materialSpecifications,
        IReadOnlyCollection<ExternalMaterialSupply>? externalMaterialSupplies = null,
        IReadOnlyCollection<CommittedMaterialSupply>? committedMaterialSupplies = null)
    {
        var specByCode = materialSpecifications
            .Where(x => x.IsActive)
            .GroupBy(x => x.MaterialSpecificationCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var materialCodeBySpecification = specByCode.ToDictionary(
            x => x.Key,
            x => x.Value.SapMaterialCode ?? x.Value.MaterialSpecificationCode,
            StringComparer.OrdinalIgnoreCase);

        _pools = new List<Pool>();
        foreach (var position in inventory)
        {
            if (position.Stage == InventoryStage.FinishedGoods) continue; // #45 owns FG coverage.
            if (position.QualityStatus is not (MaterialQualityStatus.Available or MaterialQualityStatus.Released)) continue;

            var opening = Math.Max(0m,
                position.AvailableQuantityMt - position.ReservedQuantityMt - position.AllocatedOutgoingQuantityMt);
            if (opening > QuantityTolerance)
            {
                var availableAt = position.AvailableFromUtc ?? referenceTimeUtc;
                _pools.Add(new Pool(
                    null,
                    position.MaterialCode,
                    null,
                    position.GradeCode,
                    position.CrossSectionCode,
                    "MT",
                    position.LocationCode,
                    position.Stage,
                    opening,
                    availableAt,
                    availableAt <= referenceTimeUtc
                        ? MaterialCoverageSourceType.OpeningInventory
                        : MaterialCoverageSourceType.KnownIncoming,
                    position.QualityStatus,
                    null));
            }

            var confirmedIncoming = Math.Max(0m, position.ConfirmedIncomingQuantityMt);
            if (confirmedIncoming > QuantityTolerance)
            {
                _pools.Add(new Pool(
                    null,
                    position.MaterialCode,
                    null,
                    position.GradeCode,
                    position.CrossSectionCode,
                    "MT",
                    position.LocationCode,
                    position.Stage,
                    confirmedIncoming,
                    position.AvailableFromUtc ?? referenceTimeUtc,
                    MaterialCoverageSourceType.KnownIncoming,
                    position.QualityStatus,
                    null));
            }
        }

        foreach (var supply in externalMaterialSupplies ?? Array.Empty<ExternalMaterialSupply>())
        {
            if (!supply.IsFirm || supply.QualityStatus is not (MaterialQualityStatus.Available or MaterialQualityStatus.Released)) continue;
            if (string.IsNullOrWhiteSpace(supply.MaterialSpecificationCode)) continue;
            var quantity = Math.Max(0m, supply.QuantityMt - supply.ReservedQuantityMt);
            if (quantity <= QuantityTolerance) continue;
            materialCodeBySpecification.TryGetValue(supply.MaterialSpecificationCode, out var materialCode);
            specByCode.TryGetValue(supply.MaterialSpecificationCode, out var spec);
            _pools.Add(new Pool(
                null,
                materialCode ?? supply.MaterialSpecificationCode,
                supply.MaterialSpecificationCode,
                supply.GradeCode,
                supply.CrossSectionCode,
                "MT",
                supply.LocationCode,
                SupplyStage(spec?.ProductForm ?? SteelProductForm.Other),
                quantity,
                supply.AvailableFromUtc,
                MaterialCoverageSourceType.KnownIncoming,
                supply.QualityStatus,
                supply.SupplyReference));
        }

        foreach (var supply in committedMaterialSupplies ?? Array.Empty<CommittedMaterialSupply>())
        {
            if (supply.QuantityMt <= QuantityTolerance) continue;
            var materialCode = supply.MaterialSpecificationCode;
            if (!string.IsNullOrWhiteSpace(supply.MaterialSpecificationCode) &&
                materialCodeBySpecification.TryGetValue(supply.MaterialSpecificationCode, out var mapped))
                materialCode = mapped;

            _pools.Add(new Pool(
                supply.ProductionOrderId,
                materialCode ?? string.Empty,
                supply.MaterialSpecificationCode,
                supply.GradeCode,
                supply.CrossSectionCode,
                "MT",
                supply.LocationCode,
                InventoryStage.CastIntermediate,
                supply.QuantityMt,
                supply.AvailableFromUtc,
                MaterialCoverageSourceType.CommittedInternalProduction,
                MaterialQualityStatus.Released,
                supply.SupplyReference));
        }
    }

    public MaterialCoverageResult Cover(MaterialCoverageRequest request)
    {
        if (!SameUom(request.Uom, "MT") || request.RequiredQuantity <= QuantityTolerance)
            return MaterialCoverageResult.None;

        // A generic inventory/supply fact cannot prove a customer-specific qualification fingerprint. BOM component
        // quality classes may be used when the supply integration carries matching quality attributes in future; until
        // then, qualified material is conservatively left uncovered rather than guessed eligible.
        if (!string.IsNullOrWhiteSpace(request.QualificationCode))
            return MaterialCoverageResult.None;

        var remaining = request.RequiredQuantity;
        var allocations = new List<MaterialCoverageAllocation>();
        foreach (var pool in EligiblePools(request)
                     .Where(x => x.AvailableFromUtc <= request.RequiredAtUtc)
                     .OrderBy(x => x.AvailableFromUtc)
                     .ThenBy(x => SourceRank(x.SourceType))
                     .ThenBy(x => x.LocationCode, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.SourceReference, StringComparer.OrdinalIgnoreCase))
        {
            if (remaining <= QuantityTolerance) break;
            var quantity = Math.Min(remaining, pool.RemainingQuantity);
            pool.RemainingQuantity -= quantity;
            remaining -= quantity;
            allocations.Add(new MaterialCoverageAllocation(
                request.RequirementId,
                pool.SourceType,
                pool.SourceReference,
                pool.MaterialCode,
                pool.MaterialSpecificationCode,
                pool.GradeCode,
                pool.CrossSectionCode,
                pool.Uom,
                pool.LocationCode,
                quantity,
                pool.AvailableFromUtc,
                pool.QualityStatus,
                pool.InventoryStage));
        }

        var lateRemaining = remaining;
        decimal lateQuantity = 0m;
        DateTime? earliestLate = null;
        foreach (var pool in EligiblePools(request)
                     .Where(x => x.AvailableFromUtc > request.RequiredAtUtc)
                     .OrderBy(x => x.AvailableFromUtc)
                     .ThenBy(x => SourceRank(x.SourceType))
                     .ThenBy(x => x.LocationCode, StringComparer.OrdinalIgnoreCase))
        {
            if (lateRemaining <= QuantityTolerance) break;
            var quantity = Math.Min(lateRemaining, pool.RemainingQuantity);
            if (quantity <= QuantityTolerance) continue;
            lateQuantity += quantity;
            lateRemaining -= quantity;
            earliestLate ??= pool.AvailableFromUtc;
        }

        return new MaterialCoverageResult(
            allocations.Sum(x => x.Quantity),
            allocations,
            lateQuantity,
            earliestLate);
    }

    private IEnumerable<Pool> EligiblePools(MaterialCoverageRequest request) =>
        _pools
            .Where(x => x.RemainingQuantity > QuantityTolerance)
            .Where(x => !x.ProductionOrderId.HasValue || x.ProductionOrderId == request.ProductionOrderId)
            .Where(x => Matches(x, request));

    private static bool Matches(Pool pool, MaterialCoverageRequest request)
    {
        if (!SameUom(pool.Uom, request.Uom)) return false;
        var materialMatches =
            (!string.IsNullOrWhiteSpace(request.MaterialSpecificationCode) && Same(pool.MaterialSpecificationCode, request.MaterialSpecificationCode)) ||
            Same(pool.MaterialCode, request.MaterialCode);
        if (!materialMatches) return false;
        if (!string.IsNullOrWhiteSpace(request.GradeCode) && !Same(pool.GradeCode, request.GradeCode)) return false;
        if (!string.IsNullOrWhiteSpace(request.CrossSectionCode) && !Same(pool.CrossSectionCode, request.CrossSectionCode)) return false;
        if (!string.IsNullOrWhiteSpace(request.LocationCode) && !Same(pool.LocationCode, request.LocationCode)) return false;
        return true;
    }

    private static int SourceRank(MaterialCoverageSourceType sourceType) => sourceType switch
    {
        MaterialCoverageSourceType.OpeningInventory => 0,
        MaterialCoverageSourceType.ActualProduction => 1,
        MaterialCoverageSourceType.CommittedInternalProduction => 2,
        MaterialCoverageSourceType.KnownIncoming => 3,
        MaterialCoverageSourceType.PlannedInternalProduction => 4,
        _ => 9
    };

    private static InventoryStage SupplyStage(SteelProductForm productForm) => productForm switch
    {
        SteelProductForm.Billet or SteelProductForm.Bloom or SteelProductForm.Slab => InventoryStage.CastIntermediate,
        SteelProductForm.LiquidSteel => InventoryStage.OtherIntermediate,
        SteelProductForm.Bar or SteelProductForm.Rod or SteelProductForm.Coil or SteelProductForm.Section or SteelProductForm.Bundle => InventoryStage.OtherIntermediate,
        _ => InventoryStage.RawMaterial
    };

    private static bool SameUom(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed class Pool(
        Guid? productionOrderId,
        string materialCode,
        string? materialSpecificationCode,
        string gradeCode,
        string crossSectionCode,
        string uom,
        string? locationCode,
        InventoryStage inventoryStage,
        decimal remainingQuantity,
        DateTime availableFromUtc,
        MaterialCoverageSourceType sourceType,
        MaterialQualityStatus qualityStatus,
        string? sourceReference)
    {
        public Guid? ProductionOrderId { get; } = productionOrderId;
        public string MaterialCode { get; } = materialCode;
        public string? MaterialSpecificationCode { get; } = materialSpecificationCode;
        public string GradeCode { get; } = gradeCode;
        public string CrossSectionCode { get; } = crossSectionCode;
        public string Uom { get; } = uom;
        public string? LocationCode { get; } = locationCode;
        public InventoryStage InventoryStage { get; } = inventoryStage;
        public decimal RemainingQuantity { get; set; } = remainingQuantity;
        public DateTime AvailableFromUtc { get; } = availableFromUtc;
        public MaterialCoverageSourceType SourceType { get; } = sourceType;
        public MaterialQualityStatus QualityStatus { get; } = qualityStatus;
        public string? SourceReference { get; } = sourceReference;
    }
}
