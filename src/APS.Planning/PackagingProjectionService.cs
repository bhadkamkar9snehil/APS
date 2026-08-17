using APS.Application;
using APS.Domain;

namespace APS.Planning;

internal static class PackagingProjectionService
{
    public static IReadOnlyCollection<PlannedPackagingUnit> Build(
        IReadOnlyCollection<ProductionOrder> productionOrders,
        CampaignPlanningResult campaignPlan,
        IReadOnlyCollection<MaterialSpecification>? materialSpecifications,
        IReadOnlyCollection<PackagingSpecification>? packagingSpecifications,
        IReadOnlyCollection<CrossSectionSpecification>? crossSections)
    {
        var materials = materialSpecifications ?? Array.Empty<MaterialSpecification>();
        var packaging = packagingSpecifications ?? Array.Empty<PackagingSpecification>();
        var sections = (crossSections ?? Array.Empty<CrossSectionSpecification>())
            .ToDictionary(x => x.CrossSectionCode, StringComparer.OrdinalIgnoreCase);
        var result = new List<PlannedPackagingUnit>();

        foreach (var order in productionOrders)
        {
            if (!campaignPlan.RollingRequirementsMt.TryGetValue(order.Id, out var finishedQuantityMt) || finishedQuantityMt <= 0m)
                continue;

            var material = materials.FirstOrDefault(x =>
                x.IsActive &&
                (string.Equals(x.SapMaterialCode, order.MaterialCode, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(x.MaterialSpecificationCode, order.MaterialCode, StringComparison.OrdinalIgnoreCase)));
            if (material is null) continue;

            var specification = packaging.FirstOrDefault(x =>
                string.Equals(x.MaterialSpecificationCode, material.MaterialSpecificationCode, StringComparison.OrdinalIgnoreCase));

            var unitType = ResolveUnitType(material, specification);
            if (!unitType.HasValue) continue;

            var target = unitType == PackagingUnitType.Bundle
                ? order.Requirement?.TargetBundleWeightMt ?? specification?.TargetUnitWeightMt
                : order.Requirement?.TargetCoilWeightMt ?? specification?.TargetUnitWeightMt;
            var minimum = unitType == PackagingUnitType.Bundle
                ? order.Requirement?.MinimumBundleWeightMt ?? specification?.MinimumUnitWeightMt
                : order.Requirement?.MinimumCoilWeightMt ?? specification?.MinimumUnitWeightMt;
            var maximum = unitType == PackagingUnitType.Bundle
                ? order.Requirement?.MaximumBundleWeightMt ?? specification?.MaximumUnitWeightMt
                : order.Requirement?.MaximumCoilWeightMt ?? specification?.MaximumUnitWeightMt;

            if (!target.HasValue || target.Value <= 0m) continue;
            var unitWeights = DistributeUnits(finishedQuantityMt, target.Value, minimum, maximum);
            var cutLength = order.Requirement?.CutLengthM ?? specification?.StandardCutLengthM ?? material.StandardCutLengthM;
            var sectionCode = material.CrossSectionCode ?? order.FinalCrossSectionCode;
            sections.TryGetValue(sectionCode, out var section);

            for (var index = 0; index < unitWeights.Count; index++)
            {
                var weight = unitWeights[index];
                var pieceCount = unitType == PackagingUnitType.Bundle
                    ? PieceCount(weight, cutLength, section?.TheoreticalKgPerM, specification?.TargetPiecesPerUnit)
                    : null;

                result.Add(new PlannedPackagingUnit
                {
                    ProductionOrderId = order.Id,
                    PackagingUnitType = unitType.Value,
                    SequenceNumber = index + 1,
                    PlannedWeightMt = weight,
                    PlannedPieceCount = pieceCount,
                    CutLengthM = cutLength,
                    PackagingCode = specification?.PackagingCode,
                    PlannedIdentifier = $"PLAN-{order.ProductionOrderNumber}-{(unitType == PackagingUnitType.Bundle ? "B" : "C")}{index + 1:0000}"
                });
            }
        }

        return result;
    }

    private static PackagingUnitType? ResolveUnitType(MaterialSpecification material, PackagingSpecification? packaging)
    {
        if (packaging is not null) return packaging.PackagingUnitType;
        return material.ProductForm switch
        {
            SteelProductForm.Bar or SteelProductForm.Rod or SteelProductForm.Bundle => PackagingUnitType.Bundle,
            SteelProductForm.Coil => PackagingUnitType.Coil,
            _ => null
        };
    }

    private static IReadOnlyList<decimal> DistributeUnits(
        decimal quantityMt,
        decimal targetMt,
        decimal? minimumMt,
        decimal? maximumMt)
    {
        var minimum = minimumMt.GetValueOrDefault(Math.Max(0.0001m, targetMt * 0.75m));
        var maximum = maximumMt.GetValueOrDefault(targetMt * 1.25m);
        if (minimum <= 0m || maximum < minimum || targetMt < minimum || targetMt > maximum)
            throw new InvalidOperationException($"Packaging weight envelope {minimum:0.####}/{targetMt:0.####}/{maximum:0.####} MT is invalid.");

        var minCount = Math.Max(1, (int)Math.Ceiling(quantityMt / maximum));
        var maxCount = Math.Max(minCount, (int)Math.Floor(quantityMt / minimum));
        var preferred = Math.Max(1, (int)Math.Round(quantityMt / targetMt, MidpointRounding.AwayFromZero));
        var count = Math.Clamp(preferred, minCount, maxCount);
        var average = quantityMt / count;

        if (average < minimum || average > maximum)
            throw new InvalidOperationException($"Quantity {quantityMt:0.####} MT cannot be unitized within packaging envelope {minimum:0.####}-{maximum:0.####} MT.");

        var rounded = decimal.Round(average, 4, MidpointRounding.AwayFromZero);
        var result = new List<decimal>(count);
        var allocated = 0m;
        for (var i = 0; i < count; i++)
        {
            var weight = i == count - 1 ? quantityMt - allocated : rounded;
            result.Add(weight);
            allocated += weight;
        }
        return result;
    }

    private static int? PieceCount(
        decimal unitWeightMt,
        decimal? cutLengthM,
        decimal? kgPerM,
        int? configuredPieces)
    {
        if (configuredPieces.HasValue && configuredPieces.Value > 0) return configuredPieces;
        if (!cutLengthM.HasValue || cutLengthM.Value <= 0m || !kgPerM.HasValue || kgPerM.Value <= 0m) return null;
        var pieceKg = cutLengthM.Value * kgPerM.Value;
        return Math.Max(1, (int)Math.Round(unitWeightMt * 1000m / pieceKg, MidpointRounding.AwayFromZero));
    }
}
