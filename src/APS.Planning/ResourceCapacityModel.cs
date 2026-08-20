using APS.Domain;

namespace APS.Planning;

/// <summary>
/// The single place that turns cumulative-resource capacity master data into the integer units
/// CP-SAT needs. Both the optimizer and any projector that wants to pre-compute a demand go through
/// here, so capacity and demand are always expressed on the same scale (#35 canonical ownership).
/// </summary>
public static class ResourceCapacityModel
{
    /// <summary>
    /// Capacity and demand are scaled to hundredths so a fractional MT-equivalent basis survives the
    /// conversion to CP-SAT's integer domain without inventing precision the master data doesn't have.
    /// </summary>
    public const int CapacityScale = 100;

    /// <summary>
    /// Capacity the resource actually offers over the whole horizon, after its standing capacity
    /// factor. Returns 0 when the resource declares Cumulative but no usable capacity, which the
    /// optimizer reports as a master-data error rather than silently treating as unconstrained.
    /// </summary>
    public static long EffectiveCapacityUnits(Resource resource) =>
        EffectiveCapacityUnits(resource, resource.CapacityFactorPct);

    /// <summary>
    /// Capacity under an explicit factor - used for a calendar window that derates the resource
    /// instead of taking it out of service entirely.
    /// </summary>
    public static long EffectiveCapacityUnits(Resource resource, decimal capacityFactorPct)
    {
        var nominal = resource.NominalConcurrentCapacity ?? 0m;
        if (nominal <= 0m) return 0;
        var factor = Math.Clamp(capacityFactorPct, 0m, 100m) / 100m;
        // Floor: never claim more capacity than the master data supports.
        return (long)Math.Floor(nominal * factor * CapacityScale);
    }

    /// <summary>
    /// How much capacity one task occupies on a cumulative resource. An explicit option demand wins;
    /// otherwise the basis decides - a mass-equivalent unit consumes the task's tonnage, and a
    /// slot/position unit consumes one place regardless of tonnage.
    /// </summary>
    public static long DemandUnits(Resource resource, decimal quantityMt, decimal? explicitDemand)
    {
        var demand = explicitDemand ?? resource.CapacityBasis switch
        {
            ResourceCapacityBasis.MassEquivalentMt => quantityMt,
            _ => 1m
        };
        // Ceiling: a task never under-declares what it occupies.
        return Math.Max(1L, (long)Math.Ceiling(Math.Max(0m, demand) * CapacityScale));
    }

    /// <summary>
    /// Wall-clock hours during which the resource was running at least one operation, counting
    /// overlapping work once. For a disjunctive resource this equals the sum of its operation
    /// durations; for a cumulative one it does not, which is exactly why utilization must not be
    /// reported as that sum (#35).
    /// </summary>
    public static double OccupiedHours(IEnumerable<(DateTime Start, DateTime End)> intervals)
    {
        var total = TimeSpan.Zero;
        DateTime? runStart = null;
        var runEnd = DateTime.MinValue;

        foreach (var interval in intervals.Where(x => x.End > x.Start).OrderBy(x => x.Start))
        {
            if (runStart is null)
            {
                runStart = interval.Start;
                runEnd = interval.End;
                continue;
            }

            if (interval.Start <= runEnd)
            {
                if (interval.End > runEnd) runEnd = interval.End;
                continue;
            }

            total += runEnd - runStart.Value;
            runStart = interval.Start;
            runEnd = interval.End;
        }

        if (runStart is not null) total += runEnd - runStart.Value;
        return total.TotalHours;
    }

    /// <summary>
    /// Largest number of operations the resource held simultaneously. Always 1 on a correctly
    /// scheduled disjunctive resource; on a cumulative one this is the number to compare against
    /// capacity when judging how loaded the unit really was.
    /// </summary>
    public static int PeakConcurrency(IEnumerable<(DateTime Start, DateTime End)> intervals)
    {
        var edges = intervals
            .Where(x => x.End > x.Start)
            .SelectMany(x => new[] { (Time: x.Start, Delta: 1), (Time: x.End, Delta: -1) })
            // Ends are applied before starts at the same instant: back-to-back work is not concurrent.
            .OrderBy(x => x.Time).ThenBy(x => x.Delta)
            .ToArray();

        var peak = 0;
        var current = 0;
        foreach (var edge in edges)
        {
            current += edge.Delta;
            if (current > peak) peak = current;
        }

        return peak;
    }
}
