using System.Security.Cryptography;
using System.Text;
using APS.Domain;

namespace APS.Planning;

internal static class PlanRequirementSnapshotBuilder
{
    public static IReadOnlyCollection<PlanOrderRequirementSnapshot> Build(
        Guid planVersionId,
        IReadOnlyCollection<ProductionOrder> productionOrders)
    {
        return productionOrders
            .DistinctBy(x => x.Id)
            .Select(po => BuildOne(planVersionId, po))
            .ToArray();
    }

    private static PlanOrderRequirementSnapshot BuildOne(Guid planVersionId, ProductionOrder po)
    {
        var grade = po.SteelGrade;
        var order = po.Requirement;
        var snapshot = new PlanOrderRequirementSnapshot
        {
            PlanVersionId = planVersionId,
            ProductionOrderId = po.Id,
            SalesOrderNumber = po.SalesOrder?.SalesOrderNumber,
            SalesOrderItem = po.SalesOrder?.ItemNumber,
            CustomerCode = order?.CustomerCode ?? po.SalesOrder?.CustomerCode,
            CustomerGroupCode = order?.CustomerGroupCode ?? po.SalesOrder?.CustomerGroupCode,
            MaterialCode = po.MaterialCode,
            GradeCode = po.GradeCode,
            GradeFamilyCode = grade?.GradeFamilyCode ?? po.GradeFamilyCode,
            GradeSequenceClassCode = grade?.SequenceClassCode ?? po.GradeSequenceClassCode,
            CastingClassCode = grade?.CastingClassCode,
            QualityClassCode = order?.QualityClassCode ?? grade?.QualityClassCode,
            RouteCode = order?.RequiredRouteCode ?? po.RouteCode,
            CasterSectionCode = po.CasterSectionCode,
            FinalCrossSectionCode = po.FinalCrossSectionCode,
            SegregationPolicy = order?.SegregationPolicy ?? SegregationPolicy.None,
            VdRequirement = ResolveProcess(grade, order, ProcessOperationType.Vd),
            ReheatRequirement = ResolveProcess(grade, order, ProcessOperationType.Reheat),
            TmtRequirement = ResolveProcess(grade, order, ProcessOperationType.Tmt),
            HotChargeAllowed = (grade?.HotChargeEligible ?? true) && order?.ForbidHotCharge != true,
            RequiredResourceId = order?.RequiredResourceId,
            MinimumSuperheatC = Max(grade?.MinimumSuperheatC, order?.MinimumSuperheatC),
            TargetSuperheatC = order?.TargetSuperheatC ?? grade?.TargetSuperheatC,
            MaximumSuperheatC = Min(grade?.MaximumSuperheatC, order?.MaximumSuperheatC),
            MinimumCastingTemperatureC = Max(grade?.MinimumCastingTemperatureC, order?.MinimumCastingTemperatureC),
            TargetCastingTemperatureC = grade?.TargetCastingTemperatureC,
            MaximumCastingTemperatureC = Min(grade?.MaximumCastingTemperatureC, order?.MaximumCastingTemperatureC),
            CutLengthM = order?.CutLengthM,
            MinimumBundleWeightMt = order?.MinimumBundleWeightMt,
            TargetBundleWeightMt = order?.TargetBundleWeightMt,
            MaximumBundleWeightMt = order?.MaximumBundleWeightMt,
            MinimumCoilWeightMt = order?.MinimumCoilWeightMt,
            TargetCoilWeightMt = order?.TargetCoilWeightMt,
            MaximumCoilWeightMt = order?.MaximumCoilWeightMt,
            AllowMixedHeatBundle = order?.AllowMixedHeatBundle,
            MarkingRequirementCode = order?.MarkingRequirementCode,
            InspectionRequirementCode = order?.InspectionRequirementCode,
            RequirementReference = order?.RequirementReference
        };

        foreach (var item in ResolveChemistry(grade, order))
        {
            item.PlanOrderRequirementSnapshotId = snapshot.Id;
            snapshot.Chemistry.Add(item);
        }
        foreach (var item in ResolveProcessRequirements(grade, order))
        {
            item.PlanOrderRequirementSnapshotId = snapshot.Id;
            snapshot.ProcessRequirements.Add(item);
        }

        snapshot.RequirementFingerprint = Fingerprint(snapshot);
        return snapshot;
    }

    private static IReadOnlyCollection<PlanChemistryRequirementSnapshot> ResolveChemistry(
        SteelGrade? grade,
        ProductionOrderRequirement? order)
    {
        var master = grade?.Chemistry.ToDictionary(x => x.ElementCode, StringComparer.OrdinalIgnoreCase)
                     ?? new Dictionary<string, GradeChemistryRequirement>(StringComparer.OrdinalIgnoreCase);
        var overrides = order?.ChemistryOverrides.ToDictionary(x => x.ElementCode, StringComparer.OrdinalIgnoreCase)
                        ?? new Dictionary<string, OrderChemistryRequirement>(StringComparer.OrdinalIgnoreCase);

        return master.Keys.Concat(overrides.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)
            .Select(code =>
            {
                master.TryGetValue(code, out var g);
                overrides.TryGetValue(code, out var o);
                return new PlanChemistryRequirementSnapshot
                {
                    ElementCode = code,
                    MinimumPct = Max(g?.MinimumPct, o?.MinimumPct),
                    TargetPct = o?.TargetPct ?? g?.TargetPct,
                    MaximumPct = Min(g?.MaximumPct, o?.MaximumPct)
                };
            })
            .ToArray();
    }

    private static IReadOnlyCollection<PlanProcessRequirementSnapshot> ResolveProcessRequirements(
        SteelGrade? grade,
        ProductionOrderRequirement? order)
    {
        var master = grade?.ProcessRequirements.ToDictionary(x => x.ProcessOperationType)
                     ?? new Dictionary<ProcessOperationType, GradeProcessRequirement>();
        var overrides = order?.ProcessOverrides
            .GroupBy(x => x.ProcessOperationType)
            .ToDictionary(x => x.Key, x => x.First())
            ?? new Dictionary<ProcessOperationType, OrderProcessRequirement>();

        var operationTypes = master.Keys.Concat(overrides.Keys)
            .Concat(new[] { ProcessOperationType.Vd, ProcessOperationType.Reheat, ProcessOperationType.Tmt })
            .Distinct()
            .OrderBy(x => x);

        return operationTypes.Select(type =>
        {
            master.TryGetValue(type, out var g);
            overrides.TryGetValue(type, out var o);
            return new PlanProcessRequirementSnapshot
            {
                ProcessOperationType = type,
                Requirement = ResolveProcess(grade, order, type),
                CapabilityClassCode = o?.CapabilityClassCode ?? g?.CapabilityClassCode,
                RequiredResourceId = o?.RequiredResourceId ?? order?.RequiredResourceId,
                MinimumProcessMinutes = Max(g?.MinimumProcessMinutes, null),
                MaximumProcessMinutes = Min(g?.MaximumProcessMinutes, null),
                MaximumQueueMinutes = Min(g?.MaximumQueueMinutesAfterOperation, o?.MaximumQueueMinutes),
                MinimumHeatWeightMt = g?.MinimumHeatWeightMt,
                TargetHeatWeightMt = g?.TargetHeatWeightMt,
                MaximumHeatWeightMt = g?.MaximumHeatWeightMt,
                ExpectedYieldPct = g?.ExpectedYieldPct
            };
        }).ToArray();
    }

    private static RequirementDisposition ResolveProcess(
        SteelGrade? grade,
        ProductionOrderRequirement? order,
        ProcessOperationType type)
    {
        var result = grade?.ProcessRequirements.FirstOrDefault(x => x.ProcessOperationType == type)?.Requirement
                     ?? RequirementDisposition.Optional;
        var processOverride = order?.ProcessOverrides.FirstOrDefault(x => x.ProcessOperationType == type);
        if (processOverride is not null) result = processOverride.Requirement;

        if (type == ProcessOperationType.Vd)
        {
            if (order?.RequireVd == true) result = RequirementDisposition.Required;
            if (order?.ForbidVd == true) result = RequirementDisposition.Forbidden;
        }
        if (type == ProcessOperationType.Reheat && order?.RequireReheating == true)
            result = RequirementDisposition.Required;
        if (type == ProcessOperationType.Tmt && order?.RequireTmt == true)
            result = RequirementDisposition.Required;

        return result;
    }

    private static string Fingerprint(PlanOrderRequirementSnapshot snapshot)
    {
        var text = new StringBuilder()
            .Append(snapshot.ProductionOrderId).Append('|')
            .Append(snapshot.GradeCode).Append('|')
            .Append(snapshot.RouteCode).Append('|')
            .Append(snapshot.VdRequirement).Append('|')
            .Append(snapshot.ReheatRequirement).Append('|')
            .Append(snapshot.TmtRequirement).Append('|')
            .Append(snapshot.MinimumSuperheatC).Append('|')
            .Append(snapshot.TargetSuperheatC).Append('|')
            .Append(snapshot.MaximumSuperheatC).Append('|')
            .Append(snapshot.SegregationPolicy).Append('|')
            .Append(snapshot.CutLengthM).Append('|')
            .Append(snapshot.TargetBundleWeightMt).Append('|')
            .Append(snapshot.TargetCoilWeightMt).Append('|');
        foreach (var item in snapshot.Chemistry.OrderBy(x => x.ElementCode))
            text.Append(item.ElementCode).Append(':').Append(item.MinimumPct).Append(':').Append(item.TargetPct).Append(':').Append(item.MaximumPct).Append(';');
        foreach (var item in snapshot.ProcessRequirements.OrderBy(x => x.ProcessOperationType))
            text.Append(item.ProcessOperationType).Append(':').Append(item.Requirement).Append(':').Append(item.CapabilityClassCode).Append(':').Append(item.RequiredResourceId).Append(';');

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static decimal? Max(decimal? a, decimal? b) => a.HasValue && b.HasValue ? Math.Max(a.Value, b.Value) : a ?? b;
    private static decimal? Min(decimal? a, decimal? b) => a.HasValue && b.HasValue ? Math.Min(a.Value, b.Value) : a ?? b;
    private static int? Max(int? a, int? b) => a.HasValue && b.HasValue ? Math.Max(a.Value, b.Value) : a ?? b;
    private static int? Min(int? a, int? b) => a.HasValue && b.HasValue ? Math.Min(a.Value, b.Value) : a ?? b;
}
