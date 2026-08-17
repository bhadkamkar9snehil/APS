using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class SqlInventorySnapshotProvider(ApsDbContext db) : IInventorySnapshotProvider
{
    public async Task<IReadOnlyCollection<InventoryPosition>> GetInventoryAsync(
        CancellationToken cancellationToken = default)
    {
        var lots = await db.MaterialLots
            .AsNoTracking()
            .Where(x => x.Status == MaterialLotStatus.Available || x.Status == MaterialLotStatus.Reserved)
            .ToListAsync(cancellationToken);
        if (lots.Count == 0) return Array.Empty<InventoryPosition>();

        var lotIds = lots.Select(x => x.Id).ToArray();
        var reservedByLot = await db.MaterialLotAllocations
            .AsNoTracking()
            .Where(x => lotIds.Contains(x.MaterialLotId) && x.Status == LotAllocationStatus.Reserved)
            .GroupBy(x => x.MaterialLotId)
            .Select(x => new { MaterialLotId = x.Key, QuantityMt = x.Sum(y => y.AllocatedQuantityMt) })
            .ToDictionaryAsync(x => x.MaterialLotId, x => x.QuantityMt, cancellationToken);

        return lots
            .GroupBy(x => new InventoryKey(
                x.MaterialCode,
                x.GradeCode,
                x.CrossSectionCode,
                x.Stage,
                x.LocationCode))
            .Select(group =>
            {
                var available = group.Sum(x => x.QuantityMt);
                var reserved = group.Sum(lot =>
                {
                    if (lot.Status == MaterialLotStatus.Reserved) return lot.QuantityMt;
                    return reservedByLot.TryGetValue(lot.Id, out var quantity)
                        ? Math.Min(quantity, lot.QuantityMt)
                        : 0m;
                });

                return new InventoryPosition
                {
                    MaterialCode = group.Key.MaterialCode,
                    GradeCode = group.Key.GradeCode,
                    CrossSectionCode = group.Key.CrossSectionCode,
                    Stage = group.Key.Stage,
                    LocationCode = group.Key.LocationCode,
                    AvailableQuantityMt = available,
                    ReservedQuantityMt = reserved
                };
            })
            .OrderBy(x => x.Stage)
            .ThenBy(x => x.MaterialCode)
            .ThenBy(x => x.GradeCode)
            .ThenBy(x => x.CrossSectionCode)
            .ThenBy(x => x.LocationCode)
            .ToArray();
    }

    private sealed record InventoryKey(
        string MaterialCode,
        string GradeCode,
        string CrossSectionCode,
        InventoryStage Stage,
        string? LocationCode);
}
