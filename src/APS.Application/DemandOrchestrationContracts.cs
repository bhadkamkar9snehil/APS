using System.Security.Cryptography;
using System.Text;
using APS.Domain;
using FluentValidation;

namespace APS.Application;

public sealed record SalesOrderChemistryRequirementInput(
    string ElementCode,
    decimal? MinimumPct = null,
    decimal? TargetPct = null,
    decimal? MaximumPct = null);

public sealed record SalesOrderProcessRequirementInput(
    ProcessOperationType ProcessOperationType,
    RequirementDisposition Requirement,
    string? CapabilityClassCode = null,
    Guid? RequiredResourceId = null,
    int? MaximumQueueMinutes = null);

public sealed record SalesOrderRequirementInput(
    string? QualityClassCode = null,
    SegregationPolicy SegregationPolicy = SegregationPolicy.None,
    bool? RequireVd = null,
    bool? ForbidVd = null,
    bool? RequireReheating = null,
    bool? ForbidHotCharge = null,
    bool? RequireTmt = null,
    string? RequiredRouteCode = null,
    Guid? RequiredResourceId = null,
    string? RequiredResourceGroupCode = null,
    decimal? MinimumSuperheatC = null,
    decimal? TargetSuperheatC = null,
    decimal? MaximumSuperheatC = null,
    decimal? MinimumCastingTemperatureC = null,
    decimal? MaximumCastingTemperatureC = null,
    decimal? CutLengthM = null,
    decimal? TargetBundleWeightMt = null,
    decimal? MinimumBundleWeightMt = null,
    decimal? MaximumBundleWeightMt = null,
    decimal? TargetCoilWeightMt = null,
    decimal? MinimumCoilWeightMt = null,
    decimal? MaximumCoilWeightMt = null,
    bool? AllowMixedHeatBundle = null,
    string? MarkingRequirementCode = null,
    string? InspectionRequirementCode = null,
    IReadOnlyCollection<SalesOrderChemistryRequirementInput>? ChemistryOverrides = null,
    IReadOnlyCollection<SalesOrderProcessRequirementInput>? ProcessOverrides = null);

public sealed record SalesOrderDemandInput(
    string SalesOrderNumber,
    string ItemNumber,
    string MaterialCode,
    string GradeCode,
    string FinalCrossSectionCode,
    decimal OrderQuantityMt,
    decimal OpenQuantityMt,
    DateTime CustomerRequiredDate,
    DateTime? ConfirmedDeliveryDate = null,
    string? CustomerCode = null,
    string? CustomerGroupCode = null,
    string? ExternalStatus = null,
    int Priority = 0,
    SalesOrderRequirementInput? Requirement = null);

public sealed record SalesOrderReconciliationResult(
    int Created,
    int Updated,
    int Unchanged,
    int CancelledOrClosed,
    IReadOnlyCollection<Guid> SalesOrderIds);

public sealed record DemandServiceDatePolicy(
    int QualityLeadMinutes = 0,
    int PackingLeadMinutes = 0,
    int DispatchLeadMinutes = 0)
{
    public int TotalLeadMinutes =>
        Math.Max(0, QualityLeadMinutes) + Math.Max(0, PackingLeadMinutes) + Math.Max(0, DispatchLeadMinutes);

    public DateTime ProductionRequiredBy(DateTime customerOrConfirmedDate) =>
        customerOrConfirmedDate.AddMinutes(-TotalLeadMinutes);
}

public sealed record PlanningDemandSelection(
    IReadOnlyCollection<Guid>? SalesOrderIds = null,
    DateTime? RequiredThroughUtc = null,
    bool IncludeMakeToStock = true,
    DemandServiceDatePolicy? ServiceDatePolicy = null);

public sealed record DemandCoverageEvidence(
    string MaterialCode,
    string GradeCode,
    string CrossSectionCode,
    string? LocationCode,
    DateTime? AvailableFromUtc,
    MaterialQualityStatus QualityStatus,
    decimal QuantityMt);

public sealed record DemandOrchestrationItem(
    Guid SalesOrderId,
    string SalesOrderNumber,
    string SalesOrderItemNumber,
    string MaterialCode,
    string GradeCode,
    string FinalCrossSectionCode,
    string? CustomerCode,
    string? CustomerGroupCode,
    Guid? ProductionOrderId,
    string? ProductionOrderNumber,
    decimal OpenDemandQuantityMt,
    decimal FinishedGoodsCoveredQuantityMt,
    decimal ManufacturingRequirementQuantityMt,
    DateTime CustomerRequiredDate,
    DateTime? ConfirmedDeliveryDate,
    DateTime ProductionRequiredByDate,
    int Priority,
    DemandReconciliationDisposition Disposition,
    bool PlannerAttentionRequired,
    string? ReasonCode,
    IReadOnlyCollection<DemandCoverageEvidence> FinishedGoodsCoverage,
    string? RequirementQualificationFingerprint = null,
    bool RequiresCertifiedFinishedGoodsMatch = false);

public sealed record DemandOrchestrationResult(
    IReadOnlyCollection<ProductionOrder> ProductionOrders,
    IReadOnlyCollection<DemandOrchestrationItem> MakeToOrderDemand,
    IReadOnlyCollection<ProductionOrder> MakeToStockProductionOrders,
    IReadOnlyCollection<PlanningIssue> Issues);

public interface IProductionDemandOrchestrationService
{
    Task<SalesOrderReconciliationResult> ReconcileSalesOrdersAsync(
        IReadOnlyCollection<SalesOrderDemandInput> salesOrders,
        CancellationToken cancellationToken = default);

    Task<DemandOrchestrationResult> PrepareAsync(
        PlanningDemandSelection selection,
        IReadOnlyCollection<InventoryPosition> inventory,
        PlanningMasterDataSnapshot masters,
        DateTime referenceTimeUtc,
        DateTime horizonEndUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DemandOrchestrationItem>> GetCurrentMtoDemandAsync(
        CancellationToken cancellationToken = default);
}

public static class SalesOrderRequirementFingerprint
{
    public static bool RequiresCertifiedFinishedGoodsMatch(SalesOrderRequirementInput? requirement) =>
        requirement is not null && (
            !string.IsNullOrWhiteSpace(requirement.QualityClassCode) ||
            requirement.SegregationPolicy != SegregationPolicy.None ||
            requirement.RequireVd == true || requirement.ForbidVd == true ||
            requirement.RequireReheating == true || requirement.ForbidHotCharge == true || requirement.RequireTmt == true ||
            !string.IsNullOrWhiteSpace(requirement.RequiredRouteCode) || requirement.RequiredResourceId.HasValue ||
            !string.IsNullOrWhiteSpace(requirement.RequiredResourceGroupCode) ||
            requirement.MinimumSuperheatC.HasValue || requirement.TargetSuperheatC.HasValue || requirement.MaximumSuperheatC.HasValue ||
            requirement.MinimumCastingTemperatureC.HasValue || requirement.MaximumCastingTemperatureC.HasValue ||
            requirement.CutLengthM.HasValue || requirement.TargetBundleWeightMt.HasValue || requirement.TargetCoilWeightMt.HasValue ||
            requirement.AllowMixedHeatBundle.HasValue || !string.IsNullOrWhiteSpace(requirement.MarkingRequirementCode) ||
            !string.IsNullOrWhiteSpace(requirement.InspectionRequirementCode) ||
            requirement.ChemistryOverrides is { Count: > 0 } || requirement.ProcessOverrides is { Count: > 0 });

    public static string? Compute(SalesOrderDemandInput order, SalesOrderRequirementInput? requirement)
    {
        if (!RequiresCertifiedFinishedGoodsMatch(requirement)) return null;
        requirement ??= new SalesOrderRequirementInput();
        var text = new StringBuilder()
            .Append(order.MaterialCode.Trim()).Append('|')
            .Append(order.GradeCode.Trim()).Append('|')
            .Append(order.FinalCrossSectionCode.Trim()).Append('|')
            .Append(requirement.QualityClassCode).Append('|')
            .Append(requirement.SegregationPolicy).Append('|')
            .Append(requirement.RequireVd).Append('|').Append(requirement.ForbidVd).Append('|')
            .Append(requirement.RequireReheating).Append('|').Append(requirement.ForbidHotCharge).Append('|')
            .Append(requirement.RequireTmt).Append('|').Append(requirement.RequiredRouteCode).Append('|')
            .Append(requirement.RequiredResourceId).Append('|').Append(requirement.RequiredResourceGroupCode).Append('|')
            .Append(requirement.MinimumSuperheatC).Append('|').Append(requirement.TargetSuperheatC).Append('|').Append(requirement.MaximumSuperheatC).Append('|')
            .Append(requirement.MinimumCastingTemperatureC).Append('|').Append(requirement.MaximumCastingTemperatureC).Append('|')
            .Append(requirement.CutLengthM).Append('|').Append(requirement.TargetBundleWeightMt).Append('|').Append(requirement.TargetCoilWeightMt).Append('|')
            .Append(requirement.AllowMixedHeatBundle).Append('|').Append(requirement.MarkingRequirementCode).Append('|').Append(requirement.InspectionRequirementCode).Append('|');

        if (requirement.SegregationPolicy is SegregationPolicy.SameCustomerOnly or SegregationPolicy.DedicatedCampaign)
            text.Append("CUSTOMER:").Append(order.CustomerCode).Append('|');
        if (requirement.SegregationPolicy == SegregationPolicy.SameSalesOrderOnly)
            text.Append("SO:").Append(order.SalesOrderNumber).Append('/').Append(order.ItemNumber).Append('|');

        foreach (var item in requirement.ChemistryOverrides ?? Array.Empty<SalesOrderChemistryRequirementInput>())
            text.Append("CHEM:").Append(item.ElementCode).Append(':').Append(item.MinimumPct).Append(':').Append(item.TargetPct).Append(':').Append(item.MaximumPct).Append(';');
        foreach (var item in requirement.ProcessOverrides ?? Array.Empty<SalesOrderProcessRequirementInput>())
            text.Append("PROC:").Append(item.ProcessOperationType).Append(':').Append(item.Requirement).Append(':').Append(item.CapabilityClassCode).Append(':').Append(item.RequiredResourceId).Append(':').Append(item.MaximumQueueMinutes).Append(';');

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}

public sealed class SalesOrderDemandInputValidator : AbstractValidator<SalesOrderDemandInput>
{
    public SalesOrderDemandInputValidator()
    {
        RuleFor(x => x.SalesOrderNumber).NotEmpty();
        RuleFor(x => x.ItemNumber).NotEmpty();
        RuleFor(x => x.MaterialCode).NotEmpty();
        RuleFor(x => x.GradeCode).NotEmpty();
        RuleFor(x => x.FinalCrossSectionCode).NotEmpty();
        RuleFor(x => x.OrderQuantityMt).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.OpenQuantityMt).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.OpenQuantityMt).LessThanOrEqualTo(x => x.OrderQuantityMt)
            .When(x => x.OrderQuantityMt > 0m);
        RuleFor(x => x.CustomerRequiredDate).NotEmpty();
        RuleFor(x => x.Priority).GreaterThanOrEqualTo(0);
        RuleForEach(x => x.Requirement!.ChemistryOverrides).ChildRules(chemistry =>
        {
            chemistry.RuleFor(x => x.ElementCode).NotEmpty();
        }).When(x => x.Requirement?.ChemistryOverrides is { Count: > 0 });
    }
}

public sealed class PlanningDemandSelectionValidator : AbstractValidator<PlanningDemandSelection>
{
    public PlanningDemandSelectionValidator()
    {
        RuleForEach(x => x.SalesOrderIds).NotEmpty()
            .When(x => x.SalesOrderIds is { Count: > 0 });
        RuleFor(x => x.ServiceDatePolicy!.QualityLeadMinutes).GreaterThanOrEqualTo(0)
            .When(x => x.ServiceDatePolicy is not null);
        RuleFor(x => x.ServiceDatePolicy!.PackingLeadMinutes).GreaterThanOrEqualTo(0)
            .When(x => x.ServiceDatePolicy is not null);
        RuleFor(x => x.ServiceDatePolicy!.DispatchLeadMinutes).GreaterThanOrEqualTo(0)
            .When(x => x.ServiceDatePolicy is not null);
    }
}
