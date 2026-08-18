using APS.Application;
using APS.Domain;

namespace APS.Planning;

/// <summary>
/// Run-scoped adapter over the current authoritative inventory snapshot. It exists so recursive BOM
/// planning consumes qualified supply exactly once today while #14 can replace/extend the same
/// IMaterialCoverageSession contract with the full time-phased ledger.
/// </summary>
public sealed class InventorySnapshotMaterialCoverageSession : IMaterialCoverageSession
{
    private const decimal QuantityTolerance = 0.0000001m;
    private readonly List<Pool> _pools;

    public InventorySnapshotMaterialCoverageSession(IReadOnlyCollection<InventoryPosition> inventory)
    {
        _pools = inventory
            .Where(x => x.QualityStatus is MaterialQualityStatus.Available or MaterialQualityStatus.Released)
            .Where(x => x.ProjectedAvailableQuantityMt > QuantityTolerance)
            .Select(x => new Pool(x, x.ProjectedAvailableQuantityMt))
            .ToList();
    }

    public MaterialCoverageResult Cover(MaterialCoverageRequest request)
    {
        if (!SameUom(request.Uom, "MT") || request.RequiredQuantity <= QuantityTolerance)
            return MaterialCoverageResult.None;

        // Current InventoryPosition facts are MT-based and do not carry a customer qualification fingerprint.
        // If a requirement demands qualification that the snapshot cannot prove, coverage is conservative: zero.
        if (!string.IsNullOrWhiteSpace(request.QualificationCode))
            return MaterialCoverageResult.None;

        var remaining = request.RequiredQuantity;
        var allocations = new List<MaterialCoverageAllocation>();
        foreach (var pool in _pools
                     .Where(x => x.RemainingQuantityMt > QuantityTolerance)
                     .Where(x => Matches(x.Position, request))
                     .OrderBy(x => x.Position.AvailableFromUtc ?? DateTime.MinValue)
                     .ThenBy(x => x.Position.LocationCode, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.Position.MaterialCode, StringComparer.OrdinalIgnoreCase))
        {
            if (remaining <= QuantityTolerance) break;
            var quantity = Math.Min(remaining, pool.RemainingQuantityMt);
            pool.RemainingQuantityMt -= quantity;
            remaining -= quantity;
            allocations.Add(new MaterialCoverageAllocation(
                request.RequirementId,
                MaterialCoverageSourceType.OpeningInventory,
                null,
                pool.Position.MaterialCode,
                null,
                pool.Position.GradeCode,
                pool.Position.CrossSectionCode,
                "MT",
                pool.Position.LocationCode,
                quantity,
                pool.Position.AvailableFromUtc,
                pool.Position.QualityStatus,
                pool.Position.Stage));
        }

        return new MaterialCoverageResult(allocations.Sum(x => x.Quantity), allocations);
    }

    private static bool Matches(InventoryPosition position, MaterialCoverageRequest request)
    {
        if (!Same(position.MaterialCode, request.MaterialCode)) return false;
        if (!string.IsNullOrWhiteSpace(request.GradeCode) && !Same(position.GradeCode, request.GradeCode)) return false;
        if (!string.IsNullOrWhiteSpace(request.CrossSectionCode) && !Same(position.CrossSectionCode, request.CrossSectionCode)) return false;
        if (!string.IsNullOrWhiteSpace(request.LocationCode) && !Same(position.LocationCode, request.LocationCode)) return false;
        if (position.AvailableFromUtc.HasValue && position.AvailableFromUtc.Value > request.RequiredAtUtc) return false;
        return true;
    }

    private static bool SameUom(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed class Pool(InventoryPosition position, decimal remainingQuantityMt)
    {
        public InventoryPosition Position { get; } = position;
        public decimal RemainingQuantityMt { get; set; } = remainingQuantityMt;
    }
}

public sealed class NoMaterialCoverageSession : IMaterialCoverageSession
{
    public MaterialCoverageResult Cover(MaterialCoverageRequest request) => MaterialCoverageResult.None;
}
