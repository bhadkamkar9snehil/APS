using APS.Domain;

namespace APS.Planning;

internal static class SteelOrderRequirementValidator
{
    public static void Validate(
        IReadOnlyCollection<ProductionOrder> productionOrders,
        IReadOnlyCollection<SteelGrade>? steelGrades)
    {
        var gradeByCode = (steelGrades ?? Array.Empty<SteelGrade>())
            .ToDictionary(x => x.GradeCode, StringComparer.OrdinalIgnoreCase);

        foreach (var order in productionOrders)
        {
            if (order.SteelGrade is null && gradeByCode.TryGetValue(order.GradeCode, out var resolved))
            {
                order.SteelGrade = resolved;
                order.SteelGradeId = resolved.Id;
                order.GradeFamilyCode ??= resolved.GradeFamilyCode;
                order.GradeSequenceClassCode ??= resolved.SequenceClassCode;
            }

            var grade = order.SteelGrade;
            var requirement = order.Requirement;
            if (grade is null || requirement is null) continue;

            ValidateProcessRequirements(order, grade, requirement);
            ValidateChemistry(order, grade, requirement);
            ValidateThermal(order, grade, requirement);
            ValidatePackaging(order, requirement);
        }
    }

    private static void ValidateProcessRequirements(
        ProductionOrder order,
        SteelGrade grade,
        ProductionOrderRequirement requirement)
    {
        var gradeRequirements = grade.ProcessRequirements.ToDictionary(x => x.ProcessOperationType);

        if (requirement.RequireVd == true && gradeRequirements.TryGetValue(ProcessOperationType.Vd, out var vdRequired) && vdRequired.Requirement == RequirementDisposition.Forbidden)
            Fail(order, "customer/SAP requirement requires VD while the grade master forbids VD");
        if (requirement.ForbidVd == true && gradeRequirements.TryGetValue(ProcessOperationType.Vd, out var vdForbidden) && vdForbidden.Requirement == RequirementDisposition.Required)
            Fail(order, "customer/SAP requirement forbids VD while the grade master requires VD");
        if (requirement.RequireTmt == true && !grade.TmtApplicable)
            Fail(order, "customer/SAP requirement requires TMT but the grade is not TMT-applicable");
        if (requirement.ForbidHotCharge == false && !grade.HotChargeEligible)
            Fail(order, "customer/SAP requirement attempts to permit hot charge although the grade forbids it");

        foreach (var process in requirement.ProcessOverrides)
        {
            if (!gradeRequirements.TryGetValue(process.ProcessOperationType, out var gradeRequirement)) continue;
            if (gradeRequirement.Requirement == RequirementDisposition.Required && process.Requirement == RequirementDisposition.Forbidden)
                Fail(order, $"order process override forbids required process {process.ProcessOperationType}");
            if (gradeRequirement.Requirement == RequirementDisposition.Forbidden && process.Requirement == RequirementDisposition.Required)
                Fail(order, $"order process override requires grade-forbidden process {process.ProcessOperationType}");
        }
    }

    private static void ValidateChemistry(
        ProductionOrder order,
        SteelGrade grade,
        ProductionOrderRequirement requirement)
    {
        var baseByElement = grade.Chemistry.ToDictionary(x => x.ElementCode, StringComparer.OrdinalIgnoreCase);
        foreach (var customer in requirement.ChemistryOverrides)
        {
            if (customer.MinimumPct.HasValue && customer.MaximumPct.HasValue && customer.MinimumPct > customer.MaximumPct)
                Fail(order, $"chemistry override for {customer.ElementCode} has minimum above maximum");
            if (customer.TargetPct.HasValue && customer.MinimumPct.HasValue && customer.TargetPct < customer.MinimumPct)
                Fail(order, $"chemistry target for {customer.ElementCode} is below the customer minimum");
            if (customer.TargetPct.HasValue && customer.MaximumPct.HasValue && customer.TargetPct > customer.MaximumPct)
                Fail(order, $"chemistry target for {customer.ElementCode} is above the customer maximum");

            if (!baseByElement.TryGetValue(customer.ElementCode, out var master)) continue;
            if (customer.MinimumPct.HasValue && master.MinimumPct.HasValue && customer.MinimumPct < master.MinimumPct)
                Fail(order, $"chemistry override for {customer.ElementCode} widens the grade minimum ({customer.MinimumPct} < {master.MinimumPct})");
            if (customer.MaximumPct.HasValue && master.MaximumPct.HasValue && customer.MaximumPct > master.MaximumPct)
                Fail(order, $"chemistry override for {customer.ElementCode} widens the grade maximum ({customer.MaximumPct} > {master.MaximumPct})");
            if (customer.MinimumPct.HasValue && master.MaximumPct.HasValue && customer.MinimumPct > master.MaximumPct)
                Fail(order, $"chemistry override for {customer.ElementCode} has no overlap with the grade range");
            if (customer.MaximumPct.HasValue && master.MinimumPct.HasValue && customer.MaximumPct < master.MinimumPct)
                Fail(order, $"chemistry override for {customer.ElementCode} has no overlap with the grade range");
        }
    }

    private static void ValidateThermal(
        ProductionOrder order,
        SteelGrade grade,
        ProductionOrderRequirement requirement)
    {
        ValidateNarrowRange(
            order,
            "superheat",
            grade.MinimumSuperheatC,
            grade.TargetSuperheatC,
            grade.MaximumSuperheatC,
            requirement.MinimumSuperheatC,
            requirement.TargetSuperheatC,
            requirement.MaximumSuperheatC);

        ValidateNarrowRange(
            order,
            "casting temperature",
            grade.MinimumCastingTemperatureC,
            grade.TargetCastingTemperatureC,
            grade.MaximumCastingTemperatureC,
            requirement.MinimumCastingTemperatureC,
            null,
            requirement.MaximumCastingTemperatureC);
    }

    private static void ValidateNarrowRange(
        ProductionOrder order,
        string name,
        decimal? masterMin,
        decimal? masterTarget,
        decimal? masterMax,
        decimal? orderMin,
        decimal? orderTarget,
        decimal? orderMax)
    {
        if (orderMin.HasValue && orderMax.HasValue && orderMin > orderMax)
            Fail(order, $"{name} override has minimum above maximum");
        if (orderMin.HasValue && masterMin.HasValue && orderMin < masterMin)
            Fail(order, $"{name} override widens the grade minimum");
        if (orderMax.HasValue && masterMax.HasValue && orderMax > masterMax)
            Fail(order, $"{name} override widens the grade maximum");
        if (orderTarget.HasValue && orderMin.HasValue && orderTarget < orderMin)
            Fail(order, $"{name} target is below the customer minimum");
        if (orderTarget.HasValue && orderMax.HasValue && orderTarget > orderMax)
            Fail(order, $"{name} target is above the customer maximum");
        if (orderTarget.HasValue && masterMin.HasValue && orderTarget < masterMin)
            Fail(order, $"{name} target is below the grade minimum");
        if (orderTarget.HasValue && masterMax.HasValue && orderTarget > masterMax)
            Fail(order, $"{name} target is above the grade maximum");
        _ = masterTarget;
    }

    private static void ValidatePackaging(ProductionOrder order, ProductionOrderRequirement requirement)
    {
        ValidateWeightRange(order, "bundle", requirement.MinimumBundleWeightMt, requirement.TargetBundleWeightMt, requirement.MaximumBundleWeightMt);
        ValidateWeightRange(order, "coil", requirement.MinimumCoilWeightMt, requirement.TargetCoilWeightMt, requirement.MaximumCoilWeightMt);
        if (requirement.CutLengthM.HasValue && requirement.CutLengthM <= 0m)
            Fail(order, "cut length must be positive");
    }

    private static void ValidateWeightRange(ProductionOrder order, string type, decimal? min, decimal? target, decimal? max)
    {
        if (min.HasValue && min <= 0m) Fail(order, $"{type} minimum weight must be positive");
        if (target.HasValue && target <= 0m) Fail(order, $"{type} target weight must be positive");
        if (max.HasValue && max <= 0m) Fail(order, $"{type} maximum weight must be positive");
        if (min.HasValue && max.HasValue && min > max) Fail(order, $"{type} minimum weight exceeds maximum");
        if (target.HasValue && min.HasValue && target < min) Fail(order, $"{type} target weight is below minimum");
        if (target.HasValue && max.HasValue && target > max) Fail(order, $"{type} target weight exceeds maximum");
    }

    private static void Fail(ProductionOrder order, string message) =>
        throw new InvalidOperationException($"Production Order {order.ProductionOrderNumber}: {message}.");
}
