using APS.Application;
using APS.Domain;
using FluentValidation.Results;

namespace APS.Planning;

/// <summary>
/// Canonical recursive BOM/material-requirement arithmetic.
///
/// The engine owns BOM selection, lineage, yield/scrap propagation, cycle diagnostics and recursion.
/// It deliberately delegates qualified supply allocation to one run-scoped IMaterialCoverageSession so
/// inventory/receipts/reservations can evolve under #14 without creating a second stock engine here.
/// </summary>
public sealed class RecursiveMaterialRequirementEngine : IRecursiveMaterialRequirementEngine
{
    private const decimal QuantityTolerance = 0.0000001m;

    public RecursiveMaterialRequirementResult Explode(RecursiveMaterialRequirementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.CoverageSession);

        var issues = new List<PlanningIssue>();
        var requirements = new List<MaterialRequirement>();
        var allocations = new List<MaterialCoverageAllocation>();
        var specByCode = BuildSpecificationLookup(request.MaterialSpecifications);
        var validBoms = ValidateBoms(request.BillsOfMaterial, issues);
        var counter = 0;

        foreach (var seed in request.Demand
                     .OrderByDescending(x => x.Priority)
                     .ThenBy(x => x.RequiredAtUtc)
                     .ThenBy(x => x.ProductionOrderId)
                     .ThenBy(x => x.MaterialCode, StringComparer.OrdinalIgnoreCase))
        {
            var validation = new MaterialDemandSeedValidator().Validate(seed);
            if (!validation.IsValid)
            {
                AddValidationIssues(issues, "BOM_DEMAND_INVALID", seed.ProductionOrderId, validation);
                continue;
            }

            var context = new RequirementContext(
                seed.ProductionOrderId,
                seed.ProductionOrderId,
                null,
                MaterialRequirementSourceType.ProductionOrder,
                seed.MaterialCode.Trim(),
                Normalize(seed.MaterialSpecificationCode),
                Normalize(seed.GradeCode) ?? string.Empty,
                Normalize(seed.CrossSectionCode) ?? string.Empty,
                NormalizeUom(seed.Uom),
                seed.Quantity,
                seed.RequiredAtUtc,
                seed.Priority,
                Normalize(seed.LocationCode),
                seed.PlantId,
                Normalize(seed.RouteCode),
                Normalize(seed.GradeFamilyCode),
                Normalize(seed.ProductFamilyCode),
                Normalize(seed.QualificationCode),
                BomFlowType.Input,
                null,
                null,
                null,
                null,
                null);

            Expand(
                context,
                request.CoverageSession,
                validBoms,
                specByCode,
                requirements,
                allocations,
                issues,
                Array.Empty<string>(),
                Array.Empty<string>(),
                ref counter);
        }

        return new RecursiveMaterialRequirementResult(requirements, allocations, issues);
    }

    private static void Expand(
        RequirementContext context,
        IMaterialCoverageSession coverageSession,
        IReadOnlyCollection<BillOfMaterial> boms,
        IReadOnlyDictionary<string, MaterialSpecification> specByCode,
        ICollection<MaterialRequirement> requirements,
        ICollection<MaterialCoverageAllocation> coverageAllocations,
        ICollection<PlanningIssue> issues,
        IReadOnlyCollection<string> ancestryKeys,
        IReadOnlyCollection<string> ancestryDisplay,
        ref int counter)
    {
        var identity = RequirementIdentity(context.MaterialSpecificationCode, context.MaterialCode, context.GradeCode, context.CrossSectionCode, context.Uom);
        var display = RequirementDisplay(context.MaterialSpecificationCode, context.MaterialCode, context.Uom);
        var pathDisplay = ancestryDisplay.Concat(new[] { display }).ToArray();
        var path = string.Join(" -> ", pathDisplay);
        var requirement = NewRequirement(context, path, specByCode, ++counter);
        requirements.Add(requirement);

        if (ancestryKeys.Contains(identity, StringComparer.OrdinalIgnoreCase))
        {
            requirement.Status = MaterialRequirementStatus.CycleBlocked;
            requirement.IsInternallyManufacturable = false;
            requirement.NetRequirementQuantity = context.Quantity;
            SetShortfall(requirement, context.Quantity, context.Uom);
            requirement.Explanation = $"BOM cycle detected: {path}.";
            issues.Add(new PlanningIssue(
                PlanningIssueSeverity.Error,
                "BOM_CYCLE_DETECTED",
                requirement.Explanation,
                requirement.Id));
            return;
        }

        var coverage = coverageSession.Cover(new MaterialCoverageRequest(
            requirement.Id,
            context.ProductionOrderId,
            context.MaterialCode,
            context.MaterialSpecificationCode,
            context.GradeCode,
            context.CrossSectionCode,
            context.Quantity,
            context.Uom,
            context.RequiredAtUtc,
            context.LocationCode,
            context.QualificationCode,
            path));

        var reportedCovered = Math.Max(0m, coverage.CoveredQuantity);
        if (reportedCovered > context.Quantity + QuantityTolerance)
        {
            issues.Add(new PlanningIssue(
                PlanningIssueSeverity.Error,
                "MATERIAL_COVERAGE_OVERALLOCATED",
                $"Coverage provider returned {reportedCovered:0.######} {context.Uom} against {context.Quantity:0.######} {context.Uom} for {path}.",
                requirement.Id));
        }
        var covered = Math.Min(context.Quantity, reportedCovered);
        var net = Math.Max(0m, context.Quantity - covered);
        var lateSupply = Math.Min(net, Math.Max(0m, coverage.LateSupplyQuantity));
        ApplyCoverageBreakdown(requirement, coverage.Allocations, covered, context.Uom);
        requirement.NetRequirementQuantity = net;
        requirement.LateSupplyQuantity = lateSupply;
        if (coverage.EarliestLateSupplyUtc.HasValue)
            requirement.ExpectedFullyAvailableAtUtc = coverage.EarliestLateSupplyUtc;
        foreach (var allocation in coverage.Allocations)
            coverageAllocations.Add(allocation);

        if (net <= QuantityTolerance)
        {
            requirement.Status = MaterialRequirementStatus.Covered;
            requirement.IsInternallyManufacturable = false;
            requirement.InternalProductionQuantity = 0m;
            SetShortfall(requirement, 0m, context.Uom);
            requirement.Explanation = $"Qualified supply covers the full {context.Quantity:0.######} {context.Uom} requirement; recursion stops at this node.";
            return;
        }

        var selection = SelectBom(context, boms);
        if (selection.Selected is null)
        {
            requirement.Status = lateSupply > QuantityTolerance
                ? MaterialRequirementStatus.LateSupply
                : MaterialRequirementStatus.NotManufacturableHere;
            requirement.IsInternallyManufacturable = false;
            requirement.InternalProductionQuantity = 0m;
            SetShortfall(requirement, net, context.Uom);
            requirement.Explanation = lateSupply > QuantityTolerance
                ? $"{net:0.######} {context.Uom} is not available by {context.RequiredAtUtc:O}. Matching supply of {lateSupply:0.######} {context.Uom} exists from {coverage.EarliestLateSupplyUtc:O}, but it is late and was not reserved early. No effective internal BOM is configured."
                : $"{net:0.######} {context.Uom} remains uncovered and no effective internal BOM is configured. APS records shortfall; it does not invent BUY/TRANSFER supply.";
            return;
        }

        var bom = selection.Selected;
        if (!SameUom(bom.OutputUom, context.Uom))
        {
            requirement.Status = MaterialRequirementStatus.Shortfall;
            requirement.IsInternallyManufacturable = false;
            SetShortfall(requirement, net, context.Uom);
            requirement.Explanation = $"BOM {bom.BomCode} v{bom.VersionNumber} output UOM {bom.OutputUom} does not match requirement UOM {context.Uom}; no implicit UOM conversion is permitted.";
            issues.Add(new PlanningIssue(
                PlanningIssueSeverity.Error,
                "BOM_OUTPUT_UOM_MISMATCH",
                requirement.Explanation,
                requirement.Id));
            return;
        }

        if (selection.TiedCandidates.Count > 1)
        {
            issues.Add(new PlanningIssue(
                PlanningIssueSeverity.Warning,
                "BOM_PRECEDENCE_TIE_RESOLVED",
                $"Multiple BOM variants had equal business precedence for {path}; deterministic tie-break selected {bom.BomCode} v{bom.VersionNumber} by BOM code.",
                requirement.Id));
        }

        requirement.IsInternallyManufacturable = true;
        requirement.InternalProductionQuantity = net;
        SetShortfall(requirement, 0m, context.Uom);
        requirement.Status = MaterialRequirementStatus.InternalProductionRequired;
        requirement.SelectedBomId = bom.Id;
        requirement.SelectedBomCode = bom.BomCode;
        requirement.SelectedBomVersion = bom.VersionNumber;
        requirement.Explanation = lateSupply > QuantityTolerance
            ? $"{net:0.######} {context.Uom} is uncovered on time and will be manufactured internally using BOM {bom.BomCode} v{bom.VersionNumber}. Matching future supply of {lateSupply:0.######} {context.Uom} from {coverage.EarliestLateSupplyUtc:O} is late and is not used to suppress required production."
            : $"{net:0.######} {context.Uom} uncovered quantity will be manufactured internally using BOM {bom.BomCode} v{bom.VersionNumber}.";

        var nextAncestry = ancestryKeys.Concat(new[] { identity }).ToArray();
        foreach (var component in bom.Components.OrderBy(x => x.SequenceNumber).ThenBy(x => x.ComponentMaterialCode, StringComparer.OrdinalIgnoreCase))
        {
            if (component.QuantityPerOutput <= 0m) continue;
            var requiredAt = context.RequiredAtUtc.AddMinutes(-Math.Max(0, component.RequiredAtOffsetMinutes));

            if (component.FlowType != BomFlowType.Input)
            {
                var produced = net * component.QuantityPerOutput / bom.OutputQuantity;
                requirements.Add(NewProjectedOutputRequirement(
                    context,
                    requirement,
                    bom,
                    component,
                    produced,
                    requiredAt,
                    path,
                    specByCode,
                    ++counter));
                continue;
            }

            var yield = EffectiveYield(component);
            var componentQuantity = net * component.QuantityPerOutput / bom.OutputQuantity / yield;
            var childContext = new RequirementContext(
                context.ProductionOrderId,
                component.Id,
                requirement.Id,
                MaterialRequirementSourceType.BomComponent,
                component.ComponentMaterialCode.Trim(),
                Normalize(component.ComponentMaterialSpecificationCode),
                Normalize(component.ComponentGradeCode) ?? context.GradeCode,
                Normalize(component.ComponentCrossSectionCode) ?? context.CrossSectionCode,
                NormalizeUom(component.Uom),
                componentQuantity,
                requiredAt,
                context.Priority,
                Normalize(component.LocationCode) ?? context.LocationCode,
                context.PlantId,
                context.RouteCode,
                context.GradeFamilyCode,
                context.ProductFamilyCode,
                Normalize(component.QualityClassCode) ?? context.QualificationCode,
                BomFlowType.Input,
                bom.Id,
                bom.BomCode,
                bom.VersionNumber,
                yield * 100m,
                EffectiveScrap(component));

            Expand(
                childContext,
                coverageSession,
                boms,
                specByCode,
                requirements,
                coverageAllocations,
                issues,
                nextAncestry,
                pathDisplay,
                ref counter);
        }
    }

    private static void ApplyCoverageBreakdown(
        MaterialRequirement requirement,
        IReadOnlyCollection<MaterialCoverageAllocation> allocations,
        decimal acceptedCoveredQuantity,
        string uom)
    {
        var remaining = Math.Max(0m, acceptedCoveredQuantity);
        var opening = 0m;
        var incoming = 0m;
        var committed = 0m;
        var planned = 0m;
        var actual = 0m;

        foreach (var allocation in allocations)
        {
            if (remaining <= QuantityTolerance) break;
            var accepted = Math.Min(remaining, Math.Max(0m, allocation.Quantity));
            remaining -= accepted;
            switch (allocation.SourceType)
            {
                case MaterialCoverageSourceType.OpeningInventory:
                    opening += accepted;
                    break;
                case MaterialCoverageSourceType.KnownIncoming:
                    incoming += accepted;
                    break;
                case MaterialCoverageSourceType.CommittedInternalProduction:
                    committed += accepted;
                    break;
                case MaterialCoverageSourceType.PlannedInternalProduction:
                    planned += accepted;
                    break;
                case MaterialCoverageSourceType.ActualProduction:
                    actual += accepted;
                    break;
            }
        }

        requirement.CoveredQuantity = acceptedCoveredQuantity;
        requirement.OpeningInventoryCoveredQuantity = opening;
        requirement.KnownIncomingCoveredQuantity = incoming;
        requirement.CommittedProductionCoveredQuantity = committed;
        requirement.PlannedProductionCoveredQuantity = planned;
        requirement.ActualProductionCoveredQuantity = actual;
        requirement.CoveredQuantityMt = IsMt(uom) ? acceptedCoveredQuantity : 0m;
    }

    private static void SetShortfall(MaterialRequirement requirement, decimal quantity, string uom)
    {
        var normalized = Math.Max(0m, quantity);
        requirement.ShortfallQuantity = normalized;
        requirement.ShortfallQuantityMt = IsMt(uom) ? normalized : 0m;
    }

    private static MaterialRequirement NewRequirement(
        RequirementContext context,
        string path,
        IReadOnlyDictionary<string, MaterialSpecification> specByCode,
        int counter)
    {
        var spec = ResolveSpecification(context.MaterialSpecificationCode, context.MaterialCode, specByCode);
        var requirement = new MaterialRequirement
        {
            ParentRequirementId = context.ParentRequirementId,
            RequirementKey = $"BOMREQ:{context.ProductionOrderId:N}:{counter:D5}",
            RequirementPath = path,
            SourceType = context.SourceType,
            SourceEntityId = context.SourceEntityId,
            ProductionOrderId = context.ProductionOrderId,
            MaterialSpecificationCode = context.MaterialSpecificationCode,
            MaterialCode = context.MaterialCode,
            GradeCode = context.GradeCode,
            CrossSectionCode = context.CrossSectionCode,
            ProductForm = spec?.ProductForm ?? SteelProductForm.Other,
            LocationCode = context.LocationCode,
            MaterialUom = context.Uom,
            GrossQuantity = context.Quantity,
            RequiredQuantityMt = IsMt(context.Uom) ? context.Quantity : 0m,
            RequiredAtUtc = context.RequiredAtUtc,
            TargetRequiredAtUtc = context.RequiredAtUtc,
            Priority = context.Priority,
            FlowType = context.FlowType,
            Status = MaterialRequirementStatus.SupplyActionRequired,
            SelectedBomId = context.OriginBomId,
            SelectedBomCode = context.OriginBomCode,
            SelectedBomVersion = context.OriginBomVersion,
            EffectiveYieldPct = context.EffectiveYieldPct,
            EffectiveScrapPct = context.EffectiveScrapPct,
            QualificationCode = context.QualificationCode,
            TimingBasisCode = context.ParentRequirementId.HasValue ? "BOM_COMPONENT_OFFSET" : "PRODUCTION_ORDER_REQUIRED_DATE"
        };
        return requirement;
    }

    private static MaterialRequirement NewProjectedOutputRequirement(
        RequirementContext parentContext,
        MaterialRequirement parent,
        BillOfMaterial bom,
        BillOfMaterialComponent component,
        decimal producedQuantity,
        DateTime requiredAt,
        string parentPath,
        IReadOnlyDictionary<string, MaterialSpecification> specByCode,
        int counter)
    {
        var materialCode = component.ComponentMaterialCode.Trim();
        var specCode = Normalize(component.ComponentMaterialSpecificationCode);
        var uom = NormalizeUom(component.Uom);
        var spec = ResolveSpecification(specCode, materialCode, specByCode);
        var display = RequirementDisplay(specCode, materialCode, uom);
        return new MaterialRequirement
        {
            ParentRequirementId = parent.Id,
            RequirementKey = $"BOMREQ:{parentContext.ProductionOrderId:N}:{counter:D5}",
            RequirementPath = $"{parentPath} -> {display}",
            SourceType = MaterialRequirementSourceType.BomComponent,
            SourceEntityId = component.Id,
            ProductionOrderId = parentContext.ProductionOrderId,
            MaterialSpecificationCode = specCode,
            MaterialCode = materialCode,
            GradeCode = Normalize(component.ComponentGradeCode) ?? parentContext.GradeCode,
            CrossSectionCode = Normalize(component.ComponentCrossSectionCode) ?? parentContext.CrossSectionCode,
            ProductForm = spec?.ProductForm ?? SteelProductForm.Other,
            LocationCode = Normalize(component.LocationCode) ?? parentContext.LocationCode,
            MaterialUom = uom,
            GrossQuantity = 0m,
            ProducedQuantity = producedQuantity,
            RequiredAtUtc = requiredAt,
            TargetRequiredAtUtc = requiredAt,
            Priority = parentContext.Priority,
            FlowType = component.FlowType,
            Status = MaterialRequirementStatus.ProjectedOutput,
            SelectedBomId = bom.Id,
            SelectedBomCode = bom.BomCode,
            SelectedBomVersion = bom.VersionNumber,
            QualificationCode = Normalize(component.QualityClassCode) ?? parentContext.QualificationCode,
            TimingBasisCode = "BOM_PROJECTED_OUTPUT",
            Explanation = $"BOM {bom.BomCode} v{bom.VersionNumber} projects {producedQuantity:0.######} {uom} {component.FlowType.ToString().ToLowerInvariant()} output."
        };
    }

    private static BomSelection SelectBom(RequirementContext context, IReadOnlyCollection<BillOfMaterial> boms)
    {
        var candidates = boms
            .Where(x => x.IsActive && x.Status == BomStatus.Active)
            .Where(x => x.EffectiveFromUtc <= context.RequiredAtUtc && (!x.EffectiveToUtc.HasValue || x.EffectiveToUtc.Value >= context.RequiredAtUtc))
            .Where(x => OutputMatches(x, context))
            .Where(x => SelectorMatches(x.PlantId, context.PlantId))
            .Where(x => SelectorMatches(x.RouteCode, context.RouteCode))
            .Where(x => SelectorMatches(x.GradeCode, context.GradeCode))
            .Where(x => SelectorMatches(x.GradeFamilyCode, context.GradeFamilyCode))
            .Where(x => SelectorMatches(x.ProductFamilyCode, context.ProductFamilyCode))
            .Select(x => new BomCandidate(x, Specificity(x)))
            .OrderByDescending(x => x.Bom.SelectionPriority)
            .ThenByDescending(x => x.Specificity)
            .ThenByDescending(x => x.Bom.EffectiveFromUtc)
            .ThenByDescending(x => x.Bom.VersionNumber)
            .ThenBy(x => x.Bom.BomCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0) return new BomSelection(null, Array.Empty<BillOfMaterial>());
        var best = candidates[0];
        var tied = candidates
            .Where(x => x.Bom.SelectionPriority == best.Bom.SelectionPriority &&
                        x.Specificity == best.Specificity &&
                        x.Bom.EffectiveFromUtc == best.Bom.EffectiveFromUtc &&
                        x.Bom.VersionNumber == best.Bom.VersionNumber)
            .Select(x => x.Bom)
            .ToArray();
        return new BomSelection(best.Bom, tied);
    }

    private static bool OutputMatches(BillOfMaterial bom, RequirementContext context)
    {
        if (!string.IsNullOrWhiteSpace(bom.OutputMaterialSpecificationCode))
            return Same(bom.OutputMaterialSpecificationCode, context.MaterialSpecificationCode);
        return Same(bom.OutputMaterialCode, context.MaterialCode);
    }

    private static bool SelectorMatches(Guid? selector, Guid? actual) => !selector.HasValue || selector == actual;

    private static bool SelectorMatches(string? selector, string? actual) =>
        string.IsNullOrWhiteSpace(selector) || Same(selector, actual);

    private static int Specificity(BillOfMaterial bom)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(bom.OutputMaterialSpecificationCode)) score += 1000;
        if (bom.PlantId.HasValue) score += 200;
        if (!string.IsNullOrWhiteSpace(bom.RouteCode)) score += 100;
        if (!string.IsNullOrWhiteSpace(bom.GradeCode)) score += 80;
        if (!string.IsNullOrWhiteSpace(bom.GradeFamilyCode)) score += 40;
        if (!string.IsNullOrWhiteSpace(bom.ProductFamilyCode)) score += 20;
        return score;
    }

    private static IReadOnlyCollection<BillOfMaterial> ValidateBoms(
        IReadOnlyCollection<BillOfMaterial> boms,
        ICollection<PlanningIssue> issues)
    {
        var validator = new BillOfMaterialValidator();
        var valid = new List<BillOfMaterial>();
        foreach (var bom in boms)
        {
            var validation = validator.Validate(bom);
            if (!validation.IsValid)
            {
                AddValidationIssues(issues, "BOM_MASTER_INVALID", bom.Id, validation);
                continue;
            }
            valid.Add(bom);
        }
        return valid;
    }

    private static IReadOnlyDictionary<string, MaterialSpecification> BuildSpecificationLookup(
        IReadOnlyCollection<MaterialSpecification> specifications)
    {
        var result = new Dictionary<string, MaterialSpecification>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in specifications.Where(x => x.IsActive))
        {
            result[spec.MaterialSpecificationCode] = spec;
            if (!string.IsNullOrWhiteSpace(spec.SapMaterialCode)) result[spec.SapMaterialCode] = spec;
        }
        return result;
    }

    private static MaterialSpecification? ResolveSpecification(
        string? specificationCode,
        string materialCode,
        IReadOnlyDictionary<string, MaterialSpecification> specByCode)
    {
        if (!string.IsNullOrWhiteSpace(specificationCode) && specByCode.TryGetValue(specificationCode, out var bySpec)) return bySpec;
        return specByCode.TryGetValue(materialCode, out var byMaterial) ? byMaterial : null;
    }

    private static decimal EffectiveYield(BillOfMaterialComponent component)
    {
        if (component.YieldPct.HasValue)
            return Math.Max(component.YieldPct.Value / 100m, QuantityTolerance);
        var loss = Math.Max(0m, component.ScrapPct ?? 0m) + Math.Max(0m, component.LossPct ?? 0m);
        return Math.Max((100m - loss) / 100m, QuantityTolerance);
    }

    private static decimal EffectiveScrap(BillOfMaterialComponent component)
    {
        if (component.YieldPct.HasValue) return 100m - component.YieldPct.Value;
        return Math.Max(0m, component.ScrapPct ?? 0m) + Math.Max(0m, component.LossPct ?? 0m);
    }

    private static void AddValidationIssues(
        ICollection<PlanningIssue> issues,
        string code,
        Guid sourceId,
        ValidationResult result)
    {
        foreach (var error in result.Errors)
            issues.Add(new PlanningIssue(PlanningIssueSeverity.Error, code, error.ErrorMessage, sourceId));
    }

    private static string RequirementIdentity(string? specificationCode, string materialCode, string gradeCode, string crossSectionCode, string uom) =>
        $"{Normalize(specificationCode) ?? Normalize(materialCode) ?? "?"}|{Normalize(gradeCode) ?? ""}|{Normalize(crossSectionCode) ?? ""}|{NormalizeUom(uom)}";

    private static string RequirementDisplay(string? specificationCode, string materialCode, string uom) =>
        $"{Normalize(specificationCode) ?? Normalize(materialCode) ?? "?"}[{NormalizeUom(uom)}]";

    private static bool IsMt(string uom) => SameUom(uom, "MT");

    private static string NormalizeUom(string value) => string.IsNullOrWhiteSpace(value) ? "MT" : value.Trim().ToUpperInvariant();

    private static bool SameUom(string? left, string? right) => Same(NormalizeUom(left ?? string.Empty), NormalizeUom(right ?? string.Empty));

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record BomCandidate(BillOfMaterial Bom, int Specificity);
    private sealed record BomSelection(BillOfMaterial? Selected, IReadOnlyCollection<BillOfMaterial> TiedCandidates);

    private sealed record RequirementContext(
        Guid ProductionOrderId,
        Guid SourceEntityId,
        Guid? ParentRequirementId,
        MaterialRequirementSourceType SourceType,
        string MaterialCode,
        string? MaterialSpecificationCode,
        string GradeCode,
        string CrossSectionCode,
        string Uom,
        decimal Quantity,
        DateTime RequiredAtUtc,
        int Priority,
        string? LocationCode,
        Guid? PlantId,
        string? RouteCode,
        string? GradeFamilyCode,
        string? ProductFamilyCode,
        string? QualificationCode,
        BomFlowType FlowType,
        Guid? OriginBomId,
        string? OriginBomCode,
        int? OriginBomVersion,
        decimal? EffectiveYieldPct,
        decimal? EffectiveScrapPct);
}
