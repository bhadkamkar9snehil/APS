using APS.Domain;

namespace APS.Planning;

internal sealed record ScenarioAppliedPlantState(
    IReadOnlyCollection<Resource> Resources,
    IReadOnlyCollection<ResourceCapability> Capabilities,
    IReadOnlyCollection<ResourceCalendar> Calendars);

internal static class PlanningScenarioApplier
{
    public static ScenarioAppliedPlantState Apply(
        IReadOnlyCollection<Resource> resources,
        IReadOnlyCollection<ResourceCapability> capabilities,
        IReadOnlyCollection<ResourceCalendar> calendars,
        PlanningScenario? scenario,
        DateTime horizonStartUtc,
        DateTime horizonEndUtc)
    {
        if (scenario is null || scenario.ResourceOverrides.Count == 0)
            return new ScenarioAppliedPlantState(resources, capabilities, calendars);

        var resourceCopies = resources.ToDictionary(x => x.Id, CloneResource);
        var capabilityCopies = capabilities.Select(CloneCapability).ToList();
        var calendarCopies = calendars.Select(CloneCalendar).ToList();

        foreach (var adjustment in scenario.ResourceOverrides)
        {
            if (!resourceCopies.TryGetValue(adjustment.ResourceId, out var resource)) continue;
            var from = adjustment.EffectiveFromUtc ?? horizonStartUtc;
            var to = adjustment.EffectiveToUtc ?? horizonEndUtc;
            if (to <= horizonStartUtc || from >= horizonEndUtc) continue;

            var coversHorizon = from <= horizonStartUtc && to >= horizonEndUtc;
            if (IsUnavailable(adjustment.OperatingState))
            {
                if (coversHorizon)
                {
                    resource.OperatingState = adjustment.OperatingState;
                }
                else
                {
                    calendarCopies.Add(new ResourceCalendar
                    {
                        ResourceId = resource.Id,
                        Start = from < horizonStartUtc ? horizonStartUtc : from,
                        End = to > horizonEndUtc ? horizonEndUtc : to,
                        IsAvailable = false,
                        ReasonCode = adjustment.Reason ?? adjustment.OperatingState.ToString()
                    });
                }
            }
            else
            {
                resource.OperatingState = adjustment.OperatingState;
            }

            if (adjustment.CapacityFactorPct.HasValue)
            {
                var factor = Math.Clamp(adjustment.CapacityFactorPct.Value, 0m, 100m);
                resource.CapacityFactorPct = factor;
                resource.NominalThroughputMtPerHour = Scale(resource.NominalThroughputMtPerHour, factor);
                resource.WorkingCapacityMt = Scale(resource.WorkingCapacityMt, factor);

                foreach (var capability in capabilityCopies.Where(x => x.ResourceId == resource.Id))
                {
                    capability.ThroughputMtPerHour = Scale(capability.ThroughputMtPerHour, factor);
                    capability.MaximumQuantityMt = Scale(capability.MaximumQuantityMt, factor);
                }
            }

            if (!string.IsNullOrWhiteSpace(adjustment.AllowedGradeCode) ||
                !string.IsNullOrWhiteSpace(adjustment.ForbiddenGradeCode) ||
                adjustment.RestrictedProcessOperationType.HasValue)
            {
                capabilityCopies.RemoveAll(capability =>
                    capability.ResourceId == resource.Id &&
                    IsRestricted(capability, adjustment));
            }
        }

        return new ScenarioAppliedPlantState(resourceCopies.Values.ToArray(), capabilityCopies, calendarCopies);
    }

    private static bool IsRestricted(ResourceCapability capability, ResourceScenarioOverride adjustment)
    {
        if (adjustment.RestrictedProcessOperationType.HasValue &&
            capability.ProcessOperationType.HasValue &&
            capability.ProcessOperationType != adjustment.RestrictedProcessOperationType)
            return false;

        if (!string.IsNullOrWhiteSpace(adjustment.ForbiddenGradeCode) &&
            (string.IsNullOrWhiteSpace(capability.GradeCode) ||
             string.Equals(capability.GradeCode, adjustment.ForbiddenGradeCode, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (!string.IsNullOrWhiteSpace(adjustment.AllowedGradeCode) &&
            !string.Equals(capability.GradeCode, adjustment.AllowedGradeCode, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool IsUnavailable(ResourceOperatingState state) => state is
        ResourceOperatingState.PlannedMaintenance or
        ResourceOperatingState.Breakdown or
        ResourceOperatingState.Disabled;

    private static decimal? Scale(decimal? value, decimal factorPct) =>
        value.HasValue ? value.Value * factorPct / 100m : null;

    private static Resource CloneResource(Resource x) => new()
    {
        Id = x.Id,
        PlantId = x.PlantId,
        ProcessStageId = x.ProcessStageId,
        Code = x.Code,
        Name = x.Name,
        ResourceType = x.ResourceType,
        ProcessUnitType = x.ProcessUnitType,
        OperatingState = x.OperatingState,
        CapacityFactorPct = x.CapacityFactorPct,
        SchedulingMode = x.SchedulingMode,
        CapacityBasis = x.CapacityBasis,
        NominalConcurrentCapacity = x.NominalConcurrentCapacity,
        AppliesSequenceRules = x.AppliesSequenceRules,
        MinimumHeatWeightMt = x.MinimumHeatWeightMt,
        NominalHeatWeightMt = x.NominalHeatWeightMt,
        MaximumHeatWeightMt = x.MaximumHeatWeightMt,
        LadleCapacityMt = x.LadleCapacityMt,
        WorkingCapacityMt = x.WorkingCapacityMt,
        NominalThroughputMtPerHour = x.NominalThroughputMtPerHour,
        MinimumResidenceMinutes = x.MinimumResidenceMinutes,
        NominalResidenceMinutes = x.NominalResidenceMinutes,
        MaximumResidenceMinutes = x.MaximumResidenceMinutes,
        StrandCount = x.StrandCount,
        MaximumHeatsPerSequence = x.MaximumHeatsPerSequence,
        MaximumHeatsPerTundish = x.MaximumHeatsPerTundish,
        MinimumCastingSpeedMPerMin = x.MinimumCastingSpeedMPerMin,
        NominalCastingSpeedMPerMin = x.NominalCastingSpeedMPerMin,
        MaximumCastingSpeedMPerMin = x.MaximumCastingSpeedMPerMin,
        ExpectedYieldPct = x.ExpectedYieldPct,
        SupportsHotCharge = x.SupportsHotCharge,
        SupportsColdCharge = x.SupportsColdCharge,
        TargetDischargeTemperatureC = x.TargetDischargeTemperatureC,
        IsActive = x.IsActive
    };

    private static ResourceCapability CloneCapability(ResourceCapability x) => new()
    {
        Id = x.Id,
        ResourceId = x.ResourceId,
        ProcessOperationType = x.ProcessOperationType,
        CapabilityClassCode = x.CapabilityClassCode,
        GradeCode = x.GradeCode,
        GradeFamilyCode = x.GradeFamilyCode,
        CastingClassCode = x.CastingClassCode,
        MaterialSpecificationCode = x.MaterialSpecificationCode,
        InputCrossSectionCode = x.InputCrossSectionCode,
        OutputCrossSectionCode = x.OutputCrossSectionCode,
        RouteCode = x.RouteCode,
        ProductFamilyCode = x.ProductFamilyCode,
        MinimumQuantityMt = x.MinimumQuantityMt,
        MaximumQuantityMt = x.MaximumQuantityMt,
        ThroughputMtPerHour = x.ThroughputMtPerHour,
        FixedDurationMinutes = x.FixedDurationMinutes,
        AssignmentPenalty = x.AssignmentPenalty,
        IsPreferred = x.IsPreferred
    };

    private static ResourceCalendar CloneCalendar(ResourceCalendar x) => new()
    {
        Id = x.Id,
        ResourceId = x.ResourceId,
        Start = x.Start,
        End = x.End,
        IsAvailable = x.IsAvailable,
        CapacityFactorPct = x.CapacityFactorPct,
        ReasonCode = x.ReasonCode
    };
}
