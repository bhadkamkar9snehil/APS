using APS.Domain;
using FluentValidation;

namespace APS.Application;

public enum MaterialCoverageSourceType
{
    OpeningInventory = 1,
    KnownIncoming = 2,
    CommittedInternalProduction = 3,
    PlannedInternalProduction = 4,
    ActualProduction = 5
}

public sealed record MaterialDemandSeed(
    Guid ProductionOrderId,
    string MaterialCode,
    string? MaterialSpecificationCode,
    string GradeCode,
    string CrossSectionCode,
    decimal Quantity,
    string Uom,
    DateTime RequiredAtUtc,
    int Priority,
    string? LocationCode = null,
    Guid? PlantId = null,
    string? RouteCode = null,
    string? GradeFamilyCode = null,
    string? ProductFamilyCode = null,
    string? QualificationCode = null);

public sealed record MaterialCoverageRequest(
    Guid RequirementId,
    Guid ProductionOrderId,
    string MaterialCode,
    string? MaterialSpecificationCode,
    string GradeCode,
    string CrossSectionCode,
    decimal RequiredQuantity,
    string Uom,
    DateTime RequiredAtUtc,
    string? LocationCode,
    string? QualificationCode,
    string RequirementPath);

public sealed record MaterialCoverageAllocation(
    Guid RequirementId,
    MaterialCoverageSourceType SourceType,
    string? SourceReference,
    string MaterialCode,
    string? MaterialSpecificationCode,
    string GradeCode,
    string CrossSectionCode,
    string Uom,
    string? LocationCode,
    decimal Quantity,
    DateTime? AvailableFromUtc,
    MaterialQualityStatus? QualityStatus = null,
    InventoryStage? InventoryStage = null);

public sealed record MaterialCoverageResult(
    decimal CoveredQuantity,
    IReadOnlyCollection<MaterialCoverageAllocation> Allocations,
    decimal LateSupplyQuantity = 0m,
    DateTime? EarliestLateSupplyUtc = null)
{
    public static readonly MaterialCoverageResult None = new(0m, Array.Empty<MaterialCoverageAllocation>());
}

/// <summary>
/// Run-scoped qualified-supply allocator. Implementations must consume a supply quantity at most once in a session.
/// The recursive BOM engine never reads inventory tables directly; #14 evolves this contract into the full
/// time-phased material ledger without changing recursive BOM arithmetic.
/// </summary>
public interface IMaterialCoverageSession
{
    MaterialCoverageResult Cover(MaterialCoverageRequest request);
}

public sealed record RecursiveMaterialRequirementRequest(
    IReadOnlyCollection<MaterialDemandSeed> Demand,
    IReadOnlyCollection<BillOfMaterial> BillsOfMaterial,
    IReadOnlyCollection<MaterialSpecification> MaterialSpecifications,
    IMaterialCoverageSession CoverageSession);

public sealed record RecursiveMaterialRequirementResult(
    IReadOnlyCollection<MaterialRequirement> Requirements,
    IReadOnlyCollection<MaterialCoverageAllocation> CoverageAllocations,
    IReadOnlyCollection<PlanningIssue> Issues)
{
    public bool HasErrors => Issues.Any(x => x.Severity == PlanningIssueSeverity.Error);

    public IReadOnlyCollection<MaterialRequirement> Roots => Requirements
        .Where(x => x.ParentRequirementId is null && x.FlowType == BomFlowType.Input)
        .OrderByDescending(x => x.Priority)
        .ThenBy(x => x.RequiredAtUtc)
        .ThenBy(x => x.RequirementKey, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

public interface IRecursiveMaterialRequirementEngine
{
    RecursiveMaterialRequirementResult Explode(RecursiveMaterialRequirementRequest request);
}

public sealed class MaterialDemandSeedValidator : AbstractValidator<MaterialDemandSeed>
{
    public MaterialDemandSeedValidator()
    {
        RuleFor(x => x.ProductionOrderId).NotEmpty();
        RuleFor(x => x.MaterialCode).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0m);
        RuleFor(x => x.Uom).NotEmpty();
        RuleFor(x => x.RequiredAtUtc).NotEqual(default(DateTime));
    }
}

public sealed class BillOfMaterialValidator : AbstractValidator<BillOfMaterial>
{
    public BillOfMaterialValidator()
    {
        RuleFor(x => x.BomCode).NotEmpty();
        RuleFor(x => x.VersionNumber).GreaterThan(0);
        RuleFor(x => x.OutputMaterialCode).NotEmpty();
        RuleFor(x => x.OutputQuantity).GreaterThan(0m);
        RuleFor(x => x.OutputUom).NotEmpty();
        RuleForEach(x => x.Components).SetValidator(new BillOfMaterialComponentValidator());
        RuleFor(x => x.EffectiveToUtc)
            .GreaterThan(x => x.EffectiveFromUtc)
            .When(x => x.EffectiveToUtc.HasValue);
    }
}

public sealed class BillOfMaterialComponentValidator : AbstractValidator<BillOfMaterialComponent>
{
    public BillOfMaterialComponentValidator()
    {
        RuleFor(x => x.ComponentMaterialCode).NotEmpty();
        RuleFor(x => x.QuantityPerOutput).GreaterThan(0m);
        RuleFor(x => x.Uom).NotEmpty();
        RuleFor(x => x.YieldPct).InclusiveBetween(0.0001m, 100m).When(x => x.YieldPct.HasValue);
        RuleFor(x => x.ScrapPct).InclusiveBetween(0m, 99.9999m).When(x => x.ScrapPct.HasValue);
        RuleFor(x => x.LossPct).InclusiveBetween(0m, 99.9999m).When(x => x.LossPct.HasValue);
        RuleFor(x => x)
            .Must(x => !x.ScrapPct.HasValue || !x.LossPct.HasValue || x.ScrapPct.Value + x.LossPct.Value < 100m)
            .WithMessage("BOM component ScrapPct + LossPct must be below 100%.");
    }
}