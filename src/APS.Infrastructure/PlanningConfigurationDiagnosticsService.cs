using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

/// <summary>
/// Read-only preflight for the configuration that the canonical production lifecycle will consume.
/// This deliberately reports bad master data instead of repairing it behind the planner's back.
/// </summary>
public sealed class PlanningConfigurationDiagnosticsService(
    ApsDbContext db,
    IPlanningMasterDataProvider masterDataProvider) : IPlanningConfigurationDiagnosticsService
{
    public async Task<PlanningConfigurationDiagnosticsView> GetAsync(CancellationToken cancellationToken = default)
    {
        var masters = await masterDataProvider.GetAsync(cancellationToken);
        var productionOrders = await db.ProductionOrders
            .AsNoTracking()
            .Where(x =>
                x.RemainingQuantityMt > 0m &&
                (x.Status == ProductionOrderStatus.Planned || x.Status == ProductionOrderStatus.Firmed))
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.RequiredDate)
            .ThenBy(x => x.ProductionOrderNumber)
            .ToArrayAsync(cancellationToken);

        var diagnostics = new List<PlanningConfigurationDiagnostic>();
        AddGlobalDiagnostics(masters, diagnostics);
        AddProductionOrderRouteDiagnostics(masters, productionOrders, diagnostics);
        AddRouteMasterDiagnostics(masters, diagnostics);
        AddResourceDiagnostics(masters, diagnostics);
        AddTransitionDiagnostics(masters, diagnostics);
        AddThermalDiagnostics(masters, diagnostics);
        AddScenarioDiagnostics(masters, diagnostics);

        var ordered = diagnostics
            .DistinctBy(x => (x.Code, x.EntityCode, x.Message))
            .OrderByDescending(x => x.Severity)
            .ThenBy(x => x.Area, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.EntityCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PlanningConfigurationDiagnosticsView(
            DateTime.UtcNow,
            productionOrders.Length,
            masters.Routes.Count,
            masters.Resources.Count,
            ordered);
    }

    private static void AddGlobalDiagnostics(
        PlanningMasterDataSnapshot masters,
        ICollection<PlanningConfigurationDiagnostic> diagnostics)
    {
        if (masters.Resources.Count == 0)
        {
            diagnostics.Add(Blocker(
                "NO_RESOURCES",
                "Resources",
                "No physical resources are configured",
                "Canonical production planning cannot calculate without at least one active physical resource.",
                fixHref: "/plan/master-data",
                fixLabel: "Open resources"));
        }

        if (masters.Routes.Count == 0)
        {
            diagnostics.Add(Blocker(
                "NO_ROUTES",
                "Routes",
                "No active manufacturing routes are configured",
                "Current manufacturing demand cannot be projected into process operations without an active route.",
                fixHref: "/plan/routes",
                fixLabel: "Open routes"));
        }

        if (masters.RouteOperations.Count == 0)
        {
            diagnostics.Add(Blocker(
                "NO_ROUTE_OPERATIONS",
                "Routes",
                "No manufacturing-route operations are configured",
                "The production lifecycle rejects the simplified compatibility fallback in Production mode.",
                fixHref: "/plan/routes",
                fixLabel: "Configure route operations"));
        }
    }

    private static void AddProductionOrderRouteDiagnostics(
        PlanningMasterDataSnapshot masters,
        IReadOnlyCollection<ProductionOrder> productionOrders,
        ICollection<PlanningConfigurationDiagnostic> diagnostics)
    {
        var routes = masters.Routes.ToDictionary(x => x.RouteCode, StringComparer.OrdinalIgnoreCase);
        var operationsByRoute = masters.RouteOperations
            .GroupBy(x => x.RouteCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(y => y.SequenceNumber).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var crossSections = masters.EffectiveCrossSections
            .Select(x => x.CrossSectionCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var po in productionOrders)
        {
            if (!routes.ContainsKey(po.RouteCode))
            {
                diagnostics.Add(Blocker(
                    "DEMAND_ROUTE_NOT_FOUND",
                    "Demand / route",
                    "Manufacturing requirement references a missing route",
                    $"{po.ProductionOrderNumber} requires route {po.RouteCode}, but that route is not active in planning master data.",
                    po.ProductionOrderNumber,
                    po.Id,
                    "/plan/routes",
                    "Open routes"));
                continue;
            }

            if (!operationsByRoute.TryGetValue(po.RouteCode, out var operations) || operations.Length == 0)
            {
                diagnostics.Add(Blocker(
                    "DEMAND_ROUTE_EMPTY",
                    "Demand / route",
                    "Manufacturing route has no operations",
                    $"{po.ProductionOrderNumber} uses {po.RouteCode}, but the route contains no configured process operations.",
                    po.ProductionOrderNumber,
                    po.Id,
                    "/plan/routes",
                    "Configure route"));
                continue;
            }

            // The planner can infer an omitted output code from demand/capabilities, but an explicitly
            // configured final output is authoritative. If that explicit endpoint differs from the PO
            // requirement, this is exactly the ROUTE_FINAL_SECTION_NOT_REACHED failure the engine emits.
            var configuredFinalSection = operations
                .OrderByDescending(x => x.SequenceNumber)
                .Select(x => x.OutputCrossSectionCode)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (!string.IsNullOrWhiteSpace(configuredFinalSection) &&
                !Same(configuredFinalSection, po.FinalCrossSectionCode))
            {
                diagnostics.Add(Blocker(
                    "ROUTE_FINAL_SECTION_MISMATCH",
                    "Demand / route",
                    "Route does not reach the required finished section",
                    $"{po.ProductionOrderNumber} requires {po.FinalCrossSectionCode}, but route {po.RouteCode} explicitly ends at {configuredFinalSection}. Correct the route/output capability or the demand route assignment before Calculate.",
                    po.ProductionOrderNumber,
                    po.Id,
                    "/plan/routes",
                    "Fix route"));
            }

            if (crossSections.Count > 0 && !crossSections.Contains(po.FinalCrossSectionCode))
            {
                diagnostics.Add(Warning(
                    "DEMAND_FINAL_SECTION_MASTER_MISSING",
                    "Cross sections",
                    "Finished section is not in the cross-section master",
                    $"{po.ProductionOrderNumber} requires {po.FinalCrossSectionCode}, but no active cross-section specification exists for that code. Exact-code routing may still work, but hierarchy/family matching and diagnostics are incomplete.",
                    po.ProductionOrderNumber,
                    po.Id,
                    "/plan/master-data",
                    "Review cross sections"));
            }

            if (crossSections.Count > 0 && !crossSections.Contains(po.CasterSectionCode))
            {
                diagnostics.Add(Warning(
                    "DEMAND_CASTER_SECTION_MASTER_MISSING",
                    "Cross sections",
                    "Caster section is not in the cross-section master",
                    $"{po.ProductionOrderNumber} requires caster section {po.CasterSectionCode}, but no active cross-section specification exists for that code.",
                    po.ProductionOrderNumber,
                    po.Id,
                    "/plan/master-data",
                    "Review cross sections"));
            }

            foreach (var operation in operations.Where(x => x.Requirement == RequirementDisposition.Required))
            {
                if (HasObviousResourceEvidence(masters, operation, po)) continue;
                diagnostics.Add(Blocker(
                    "ROUTE_OPERATION_NO_RESOURCE",
                    "Resource eligibility",
                    "Required route operation has no eligible resource evidence",
                    $"{po.ProductionOrderNumber} requires {operation.ProcessOperationType} on route {po.RouteCode}, but no active physical resource, generic capability or route capability provides evidence for that operation.",
                    $"{po.RouteCode}:{operation.SequenceNumber}:{operation.ProcessOperationType}",
                    operation.Id,
                    "/plan/resource-constraints",
                    "Fix resource eligibility"));
            }
        }
    }

    private static void AddRouteMasterDiagnostics(
        PlanningMasterDataSnapshot masters,
        ICollection<PlanningConfigurationDiagnostic> diagnostics)
    {
        foreach (var group in masters.RouteOperations.GroupBy(x => x.RouteCode, StringComparer.OrdinalIgnoreCase))
        {
            var operations = group.OrderBy(x => x.SequenceNumber).ToArray();
            foreach (var duplicate in operations.GroupBy(x => x.SequenceNumber).Where(x => x.Count() > 1))
            {
                diagnostics.Add(Blocker(
                    "ROUTE_DUPLICATE_SEQUENCE",
                    "Routes",
                    "Route has duplicate operation sequence numbers",
                    $"Route {group.Key} contains {duplicate.Count()} operations at sequence {duplicate.Key}. Operation order is ambiguous.",
                    group.Key,
                    fixHref: "/plan/routes",
                    fixLabel: "Fix route order"));
            }

            for (var i = 0; i < operations.Length - 1; i++)
            {
                var current = operations[i];
                var next = operations[i + 1];
                if (string.IsNullOrWhiteSpace(current.OutputCrossSectionCode) ||
                    string.IsNullOrWhiteSpace(next.InputCrossSectionCode) ||
                    Same(current.OutputCrossSectionCode, next.InputCrossSectionCode))
                {
                    continue;
                }

                diagnostics.Add(Blocker(
                    "ROUTE_SECTION_DISCONTINUITY",
                    "Routes",
                    "Route section chain is discontinuous",
                    $"Route {group.Key} sequence {current.SequenceNumber} explicitly outputs {current.OutputCrossSectionCode}, while sequence {next.SequenceNumber} explicitly expects {next.InputCrossSectionCode}.",
                    group.Key,
                    current.Id,
                    "/plan/routes",
                    "Fix route chain"));
            }
        }
    }

    private static void AddResourceDiagnostics(
        PlanningMasterDataSnapshot masters,
        ICollection<PlanningConfigurationDiagnostic> diagnostics)
    {
        var resourceIds = masters.Resources.Select(x => x.Id).ToHashSet();

        foreach (var capability in masters.RouteResourceCapabilities.Where(x => !resourceIds.Contains(x.ResourceId)))
        {
            diagnostics.Add(Warning(
                "ROUTE_CAPABILITY_RESOURCE_MISSING",
                "Resource eligibility",
                "Route capability points to a resource that is not active",
                $"Route {capability.RouteCode} {capability.ProcessOperationType} capability references resource {capability.ResourceId}. It cannot provide planning eligibility while that resource is missing/disabled.",
                capability.RouteCode,
                capability.Id,
                "/plan/resource-constraints",
                "Review qualification"));
        }

        foreach (var calendar in masters.ResourceCalendars)
        {
            if (!resourceIds.Contains(calendar.ResourceId))
            {
                diagnostics.Add(Warning(
                    "CALENDAR_RESOURCE_MISSING",
                    "Resource calendars",
                    "Calendar interval points to a resource that is not active",
                    $"Calendar interval {calendar.Id} references resource {calendar.ResourceId}. It cannot constrain an inactive/missing planning resource.",
                    calendar.Id.ToString("N")[..8],
                    calendar.Id,
                    "/plan/resource-constraints",
                    "Review calendars"));
            }

            if (calendar.End <= calendar.Start)
            {
                diagnostics.Add(Blocker(
                    "CALENDAR_INTERVAL_INVALID",
                    "Resource calendars",
                    "Resource calendar interval has invalid dates",
                    $"Calendar interval for {ResourceCode(masters, calendar.ResourceId)} ends at {calendar.End:O}, not after its start {calendar.Start:O}.",
                    ResourceCode(masters, calendar.ResourceId),
                    calendar.Id,
                    "/plan/resource-constraints",
                    "Fix calendar"));
            }

            if (calendar.CapacityFactorPct is < 0m or > 100m)
            {
                diagnostics.Add(Blocker(
                    "CALENDAR_CAPACITY_INVALID",
                    "Resource calendars",
                    "Resource calendar capacity is outside 0–100%",
                    $"Calendar interval for {ResourceCode(masters, calendar.ResourceId)} has capacity factor {calendar.CapacityFactorPct:0.##}%.",
                    ResourceCode(masters, calendar.ResourceId),
                    calendar.Id,
                    "/plan/resource-constraints",
                    "Fix calendar"));
            }
        }
    }

    private static void AddTransitionDiagnostics(
        PlanningMasterDataSnapshot masters,
        ICollection<PlanningConfigurationDiagnostic> diagnostics)
    {
        foreach (var rule in masters.TransitionRules)
        {
            if (rule.Penalty < 0 || rule.TransitionTime < TimeSpan.Zero)
            {
                diagnostics.Add(Blocker(
                    "TRANSITION_VALUE_INVALID",
                    "Sequence rules",
                    "Transition rule contains a negative penalty or duration",
                    $"Transition {rule.FromCode} → {rule.ToCode} has penalty {rule.Penalty} and duration {rule.TransitionTime.TotalMinutes:0.##} minutes.",
                    $"{rule.FromCode}→{rule.ToCode}",
                    rule.Id,
                    "/plan/process-constraints",
                    "Fix transition"));
            }

            if (!rule.IsAllowed && rule.RequiresSequenceBreak)
            {
                diagnostics.Add(Warning(
                    "TRANSITION_FORBIDDEN_AND_BREAK",
                    "Sequence rules",
                    "Forbidden transition also requests a sequence break",
                    $"Transition {rule.FromCode} → {rule.ToCode} is forbidden and also marked RequiresSequenceBreak. The forbidden rule dominates; remove the contradictory break flag for clear planner intent.",
                    $"{rule.FromCode}→{rule.ToCode}",
                    rule.Id,
                    "/plan/process-constraints",
                    "Review transition"));
            }
        }
    }

    private static void AddThermalDiagnostics(
        PlanningMasterDataSnapshot masters,
        ICollection<PlanningConfigurationDiagnostic> diagnostics)
    {
        foreach (var requirement in masters.EffectiveGradeTemperatureRequirements)
        {
            if (!InvalidEnvelope(
                    requirement.MinimumEntryTemperatureC,
                    requirement.TargetEntryTemperatureC,
                    requirement.MaximumEntryTemperatureC) &&
                !InvalidEnvelope(
                    requirement.MinimumExitTemperatureC,
                    requirement.TargetExitTemperatureC,
                    requirement.MaximumExitTemperatureC) &&
                requirement.MaximumHoldingMinutesAfterExit is not < 0)
            {
                continue;
            }

            diagnostics.Add(Blocker(
                "GRADE_TEMPERATURE_ENVELOPE_INVALID",
                "Thermal constraints",
                "Grade temperature envelope is internally inconsistent",
                $"Grade {GradeCode(masters, requirement.SteelGradeId)} {requirement.ProcessOperationType} has an inverted temperature range or negative maximum holding time.",
                GradeCode(masters, requirement.SteelGradeId),
                requirement.Id,
                "/plan/process-constraints",
                "Fix thermal envelope"));
        }

        foreach (var capability in masters.EffectiveResourceTemperatureCapabilities)
        {
            if (!InvalidEnvelope(
                    capability.MinimumAchievableExitTemperatureC,
                    capability.NominalExitTemperatureC,
                    capability.MaximumAchievableExitTemperatureC) &&
                capability.MaximumHeatingRateCPerMinute is not < 0m &&
                capability.NominalTemperatureLossCPerMinuteWhileHolding is not < 0m)
            {
                continue;
            }

            diagnostics.Add(Blocker(
                "RESOURCE_TEMPERATURE_CAPABILITY_INVALID",
                "Thermal constraints",
                "Resource thermal capability is internally inconsistent",
                $"{ResourceCode(masters, capability.ResourceId)} {capability.ProcessOperationType} has an inverted exit-temperature range or negative heating/holding rate.",
                ResourceCode(masters, capability.ResourceId),
                capability.Id,
                "/plan/process-constraints",
                "Fix thermal capability"));
        }
    }

    private static void AddScenarioDiagnostics(
        PlanningMasterDataSnapshot masters,
        ICollection<PlanningConfigurationDiagnostic> diagnostics)
    {
        var resourceIds = masters.Resources.Select(x => x.Id).ToHashSet();
        foreach (var scenario in masters.EffectivePlanningScenarios)
        {
            foreach (var adjustment in scenario.ResourceOverrides)
            {
                if (!resourceIds.Contains(adjustment.ResourceId))
                {
                    diagnostics.Add(Warning(
                        "SCENARIO_RESOURCE_MISSING",
                        "Operating scenarios",
                        "Scenario override points to a resource that is not active",
                        $"Scenario {scenario.ScenarioCode} contains an override for resource {adjustment.ResourceId}. Selecting this scenario will not affect a missing/disabled planning resource.",
                        scenario.ScenarioCode,
                        adjustment.Id,
                        "/plan/constraints",
                        "Review scenario"));
                }

                if (adjustment.CapacityFactorPct is < 0m or > 100m)
                {
                    diagnostics.Add(Blocker(
                        "SCENARIO_CAPACITY_INVALID",
                        "Operating scenarios",
                        "Scenario capacity factor is outside 0–100%",
                        $"Scenario {scenario.ScenarioCode} override for {ResourceCode(masters, adjustment.ResourceId)} has capacity factor {adjustment.CapacityFactorPct:0.##}%.",
                        scenario.ScenarioCode,
                        adjustment.Id,
                        "/plan/constraints",
                        "Fix scenario"));
                }

                if (adjustment.EffectiveFromUtc.HasValue &&
                    adjustment.EffectiveToUtc.HasValue &&
                    adjustment.EffectiveToUtc <= adjustment.EffectiveFromUtc)
                {
                    diagnostics.Add(Blocker(
                        "SCENARIO_INTERVAL_INVALID",
                        "Operating scenarios",
                        "Scenario override has invalid effective dates",
                        $"Scenario {scenario.ScenarioCode} override for {ResourceCode(masters, adjustment.ResourceId)} ends before or at its start.",
                        scenario.ScenarioCode,
                        adjustment.Id,
                        "/plan/constraints",
                        "Fix scenario"));
                }
            }
        }
    }

    private static bool HasObviousResourceEvidence(
        PlanningMasterDataSnapshot masters,
        ManufacturingRouteOperation operation,
        ProductionOrder po)
    {
        var unitType = UnitTypeFor(operation.ProcessOperationType);
        var activeResourceIds = masters.Resources.Select(x => x.Id).ToHashSet();

        foreach (var resource in masters.Resources)
        {
            if (resource.ProcessUnitType == unitType) return true;

            if (masters.RouteResourceCapabilities.Any(x =>
                    x.ResourceId == resource.Id &&
                    x.ProcessOperationType == operation.ProcessOperationType &&
                    Matches(x.RouteCode, po.RouteCode) &&
                    Matches(x.GradeCode, po.GradeCode) &&
                    Matches(x.GradeFamilyCode, po.GradeFamilyCode)))
            {
                return true;
            }

            if (masters.ResourceCapabilities.Any(x =>
                    x.ResourceId == resource.Id &&
                    (x.ProcessOperationType == operation.ProcessOperationType ||
                     (!x.ProcessOperationType.HasValue && resource.ProcessUnitType == unitType)) &&
                    Matches(x.RouteCode, po.RouteCode) &&
                    Matches(x.GradeCode, po.GradeCode) &&
                    Matches(x.GradeFamilyCode, po.GradeFamilyCode)))
            {
                return true;
            }
        }

        return masters.RouteResourceCapabilities.Any(x =>
                   activeResourceIds.Contains(x.ResourceId) &&
                   x.ProcessOperationType == operation.ProcessOperationType &&
                   Matches(x.RouteCode, po.RouteCode)) ||
               masters.ResourceCapabilities.Any(x =>
                   activeResourceIds.Contains(x.ResourceId) &&
                   x.ProcessOperationType == operation.ProcessOperationType);
    }

    private static ProcessUnitType UnitTypeFor(ProcessOperationType operation) => operation switch
    {
        ProcessOperationType.Eaf => ProcessUnitType.Eaf,
        ProcessOperationType.Lrf => ProcessUnitType.Lrf,
        ProcessOperationType.Vd => ProcessUnitType.Vd,
        ProcessOperationType.Ccm => ProcessUnitType.Ccm,
        ProcessOperationType.Reheat => ProcessUnitType.ReheatingFurnace,
        ProcessOperationType.HotRoll => ProcessUnitType.HotRollingMill,
        ProcessOperationType.ColdRoll => ProcessUnitType.ColdRollingMill,
        ProcessOperationType.Tmt => ProcessUnitType.TmtWaterBox,
        ProcessOperationType.Cool => ProcessUnitType.CoolingBed,
        ProcessOperationType.Cut => ProcessUnitType.Shear,
        ProcessOperationType.Bundle => ProcessUnitType.BundlingLine,
        ProcessOperationType.Coil => ProcessUnitType.Coiler,
        ProcessOperationType.Finish => ProcessUnitType.FinishingLine,
        _ => ProcessUnitType.Unknown
    };

    private static bool InvalidEnvelope(decimal? minimum, decimal? target, decimal? maximum) =>
        minimum.HasValue && target.HasValue && target < minimum ||
        target.HasValue && maximum.HasValue && maximum < target ||
        minimum.HasValue && maximum.HasValue && maximum < minimum;

    private static bool Matches(string? configured, string? actual) =>
        string.IsNullOrWhiteSpace(configured) ||
        string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string ResourceCode(PlanningMasterDataSnapshot masters, Guid resourceId) =>
        masters.Resources.FirstOrDefault(x => x.Id == resourceId)?.Code ?? resourceId.ToString("N")[..8];

    private static string GradeCode(PlanningMasterDataSnapshot masters, Guid steelGradeId) =>
        masters.EffectiveSteelGrades.FirstOrDefault(x => x.Id == steelGradeId)?.GradeCode ?? steelGradeId.ToString("N")[..8];

    private static PlanningConfigurationDiagnostic Blocker(
        string code,
        string area,
        string title,
        string message,
        string? entityCode = null,
        Guid? entityId = null,
        string? fixHref = null,
        string? fixLabel = null) => new(
        PlanningConfigurationDiagnosticSeverity.Blocker,
        code,
        area,
        title,
        message,
        entityCode,
        entityId,
        fixHref,
        fixLabel);

    private static PlanningConfigurationDiagnostic Warning(
        string code,
        string area,
        string title,
        string message,
        string? entityCode = null,
        Guid? entityId = null,
        string? fixHref = null,
        string? fixLabel = null) => new(
        PlanningConfigurationDiagnosticSeverity.Warning,
        code,
        area,
        title,
        message,
        entityCode,
        entityId,
        fixHref,
        fixLabel);
}
