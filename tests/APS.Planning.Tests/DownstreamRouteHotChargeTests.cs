using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

public sealed class DownstreamRouteHotChargeTests
{
    private const string RouteCode = "ROUTE-BILLET";

    [Fact]
    public void Known_hot_committed_feed_skips_optional_reheat_and_goes_directly_to_hot_roll()
    {
        var result = Run(PlanningInventoryUse.CommittedInternalProductionFeed, ChargeMode.HotDirect);

        Assert.DoesNotContain(result.SchedulingTasks, x => x.ProcessOperationType == ProcessOperationType.Reheat);
        Assert.Contains(result.SchedulingTasks, x => x.ProcessOperationType == ProcessOperationType.HotRoll);
        Assert.Contains(result.RouteOperationDecisions!, x =>
            x.ProcessOperationType == ProcessOperationType.Reheat &&
            x.Outcome == RouteOperationOutcome.SkippedOptional &&
            x.ReasonCode == "HOT_CHARGE_PREFERRED");
    }

    [Fact]
    public void Order_can_forbid_direct_hot_charge_and_force_reheat_even_when_feed_is_hot()
    {
        var result = Run(
            PlanningInventoryUse.CommittedInternalProductionFeed,
            ChargeMode.HotDirect,
            forbidHotCharge: true);

        Assert.Contains(result.SchedulingTasks, x =>
            x.ProcessOperationType == ProcessOperationType.Reheat &&
            x.TaskType == FiniteScheduleTaskType.Reheating);
        Assert.Contains(result.RouteOperationDecisions!, x =>
            x.ProcessOperationType == ProcessOperationType.Reheat &&
            x.Outcome == RouteOperationOutcome.Included);
    }

    [Fact]
    public void Committed_feed_without_known_hot_state_uses_optional_reheat()
    {
        var result = Run(PlanningInventoryUse.CommittedInternalProductionFeed, thermalState: null);

        Assert.Contains(result.SchedulingTasks, x =>
            x.ProcessOperationType == ProcessOperationType.Reheat &&
            x.TaskType == FiniteScheduleTaskType.Reheating);
        Assert.Contains(result.SchedulingTasks, x => x.ProcessOperationType == ProcessOperationType.HotRoll);
    }

    [Fact]
    public void Yard_inventory_uses_reheat_before_hot_roll()
    {
        var result = Run(PlanningInventoryUse.IntermediateFeed, thermalState: null);

        var reheat = Assert.Single(result.RouteOperationPlans!, x => x.ProcessOperationType == ProcessOperationType.Reheat);
        var hotRoll = Assert.Single(result.RouteOperationPlans!, x => x.ProcessOperationType == ProcessOperationType.HotRoll);
        Assert.Equal(reheat.Id, hotRoll.UpstreamPlanId);
        Assert.Contains(result.SchedulingTasks, x => x.SourceEntityId == reheat.Id && x.TaskType == FiniteScheduleTaskType.Reheating);
    }

    [Fact]
    public void Actual_measured_temperature_overrides_stale_hot_label_and_requires_reheat()
    {
        var result = Run(
            PlanningInventoryUse.CommittedInternalProductionFeed,
            ChargeMode.HotDirect,
            estimatedTemperatureC: 950m,
            thermalBasis: BilletThermalSourceBasis.ActualMeasurement,
            minimumRollingEntryTemperatureC: 1000m);

        Assert.Contains(result.SchedulingTasks, x => x.ProcessOperationType == ProcessOperationType.Reheat);
        Assert.Contains(result.RouteOperationDecisions!, x =>
            x.ProcessOperationType == ProcessOperationType.Reheat &&
            x.Outcome == RouteOperationOutcome.Included &&
            x.ReasonCode == "ACTUAL_TEMPERATURE_BELOW_ROLLING_MINIMUM");
        var thermal = Assert.Single(result.BilletThermalDecisions!);
        Assert.Equal(BilletThermalSourceBasis.ActualMeasurement, thermal.SourceBasis);
        Assert.Equal(950m, thermal.SourceTemperatureC);
        Assert.Equal(1000m, thermal.MinimumRollingEntryTemperatureC);
        Assert.Equal(BilletThermalOutcome.Reheated, thermal.Outcome);
        Assert.True(thermal.ReheatRequiredByThermalState);
        Assert.False(thermal.ReheatRequiredByPolicy);
        Assert.Contains("ACTUAL_TEMPERATURE_BELOW_ROLLING_MINIMUM", thermal.RejectedHotPaths);
    }

    [Fact]
    public void Actual_measured_temperature_above_threshold_overrides_stale_cold_label()
    {
        var result = Run(
            PlanningInventoryUse.CommittedInternalProductionFeed,
            ChargeMode.ColdCharge,
            estimatedTemperatureC: 1050m,
            thermalBasis: BilletThermalSourceBasis.ActualMeasurement,
            minimumRollingEntryTemperatureC: 1000m);

        Assert.DoesNotContain(result.SchedulingTasks, x => x.ProcessOperationType == ProcessOperationType.Reheat);
        Assert.Contains(result.SchedulingTasks, x => x.ProcessOperationType == ProcessOperationType.HotRoll);
        var thermal = Assert.Single(result.BilletThermalDecisions!);
        Assert.Equal(BilletThermalSourceBasis.ActualMeasurement, thermal.SourceBasis);
        Assert.Equal(BilletThermalOutcome.HotDirect, thermal.Outcome);
        Assert.Equal(1050m, thermal.SourceTemperatureC);
    }

    [Fact]
    public void Actual_measurement_ages_out_when_its_configured_hot_hold_window_expires()
    {
        var observed = new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc);
        var result = Run(
            PlanningInventoryUse.CommittedInternalProductionFeed,
            ChargeMode.HotDirect,
            estimatedTemperatureC: 1050m,
            thermalBasis: BilletThermalSourceBasis.ActualMeasurement,
            minimumRollingEntryTemperatureC: 1000m,
            maximumHotHoldMinutes: 30,
            temperatureObservedOnUtc: observed,
            thermalReferenceTimeUtc: observed.AddHours(2));

        Assert.Contains(result.SchedulingTasks, x => x.ProcessOperationType == ProcessOperationType.Reheat);
        Assert.Contains(result.RouteOperationDecisions!, x =>
            x.ProcessOperationType == ProcessOperationType.Reheat &&
            x.ReasonCode == "ACTUAL_THERMAL_STATE_EXPIRED");
    }

    private static ProductionStructurePlanningResult Run(
        PlanningInventoryUse use,
        ChargeMode? thermalState,
        bool forbidHotCharge = false,
        decimal? estimatedTemperatureC = null,
        BilletThermalSourceBasis? thermalBasis = null,
        decimal? minimumRollingEntryTemperatureC = null,
        int? maximumHotHoldMinutes = null,
        DateTime? temperatureObservedOnUtc = null,
        DateTime? thermalReferenceTimeUtc = null)
    {
        var grade = new SteelGrade { GradeCode = "G1", Description = "G1" };
        var po = new ProductionOrder
        {
            ProductionOrderNumber = "PO-HOTCHARGE-1",
            DemandSource = DemandSourceType.MakeToOrder,
            MaterialCode = "BILLET-1",
            GradeCode = "G1",
            FinalCrossSectionCode = "HRC",
            CasterSectionCode = "150X150",
            RouteCode = RouteCode,
            PlannedQuantityMt = 50m,
            RemainingQuantityMt = 50m,
            RequiredDate = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc)
        };
        po.SteelGrade = grade;
        if (forbidHotCharge)
        {
            po.Requirement = new ProductionOrderRequirement
            {
                ProductionOrderId = po.Id,
                ProductionOrder = po,
                ForbidHotCharge = true
            };
        }

        var plan = new RollingPlan
        {
            SequenceNumber = 1,
            GradeCode = "G1",
            InputCrossSectionCode = "150X150",
            OutputCrossSectionCode = "HRC",
            RouteCode = RouteCode,
            PlannedQuantityMt = 50m,
            ExistingIntermediateInventoryMt = 50m,
            FreshSteelQuantityMt = 0m
        };
        plan.Allocations.Add(new RollingPlanAllocation
        {
            RollingPlanId = plan.Id,
            RollingPlan = plan,
            ProductionOrderId = po.Id,
            ProductionOrder = po,
            PlannedQuantityMt = 50m,
            ExistingIntermediateInventoryMt = 50m
        });

        var reheat = Resource("RHF-1", ProcessUnitType.ReheatingFurnace, ResourceType.Furnace);
        reheat.NominalResidenceMinutes = 30;
        var hotMill = Resource("HRM-1", ProcessUnitType.HotRollingMill, ResourceType.RollingMill);
        hotMill.NominalThroughputMtPerHour = 50m;

        var structure = new ProductionStructurePlanningResult(
            Array.Empty<CastSequence>(),
            new[] { plan },
            Array.Empty<PlannedBilletSupply>(),
            Array.Empty<FiniteScheduleTask>(),
            Array.Empty<PlanningIssue>());

        const string sourceReference = "SRC-1";
        var allocation = new PlanningInventoryAllocation(
            po.Id,
            InventoryStage.CastIntermediate,
            po.MaterialCode,
            po.GradeCode,
            po.CasterSectionCode,
            "YARD",
            50m,
            use,
            sourceReference,
            new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc));
        var campaignPlan = new CampaignPlanningResult(
            Array.Empty<Campaign>(),
            Array.Empty<ProductionOrder>(),
            new Dictionary<Guid, decimal> { [po.Id] = 50m },
            new Dictionary<Guid, decimal> { [po.Id] = 0m },
            new Dictionary<Guid, decimal> { [po.Id] = 50m },
            new[] { allocation });

        var committedSupplies = use == PlanningInventoryUse.CommittedInternalProductionFeed
            ? new[]
            {
                new CommittedMaterialSupply(
                    Guid.NewGuid(),
                    po.Id,
                    null,
                    sourceReference,
                    BilletSupplySourceType.InternalCastPlanned,
                    null,
                    "G1",
                    "150X150",
                    50m,
                    new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc),
                    "YARD",
                    thermalState,
                    estimatedTemperatureC,
                    thermalBasis,
                    temperatureObservedOnUtc ?? new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc))
            }
            : Array.Empty<CommittedMaterialSupply>();

        var routePlanning = new RoutePlanningInput(
            new[]
            {
                new ManufacturingRouteOperation
                {
                    ManufacturingRouteId = Guid.NewGuid(),
                    RouteCode = RouteCode,
                    SequenceNumber = 10,
                    ProcessOperationType = ProcessOperationType.Reheat,
                    ReleaseWorkOrderType = WorkOrderType.HotRolling,
                    Requirement = RequirementDisposition.Optional,
                    InputCrossSectionCode = "150X150",
                    OutputCrossSectionCode = "150X150"
                },
                new ManufacturingRouteOperation
                {
                    ManufacturingRouteId = Guid.NewGuid(),
                    RouteCode = RouteCode,
                    SequenceNumber = 20,
                    ProcessOperationType = ProcessOperationType.HotRoll,
                    ReleaseWorkOrderType = WorkOrderType.HotRolling,
                    Requirement = RequirementDisposition.Required,
                    InputCrossSectionCode = "150X150",
                    OutputCrossSectionCode = "HRC"
                }
            },
            new[]
            {
                new RouteResourceCapability
                {
                    ResourceId = hotMill.Id,
                    RouteCode = RouteCode,
                    ProcessOperationType = ProcessOperationType.HotRoll,
                    GradeCode = "G1",
                    InputCrossSectionCode = "150X150",
                    OutputCrossSectionCode = "HRC",
                    ThroughputMtPerHour = 50m
                }
            });

        return MultiStageRouteProjector.Apply(
            structure,
            campaignPlan,
            routePlanning,
            new[] { reheat, hotMill },
            Array.Empty<ResourceCapability>(),
            new[]
            {
                new PlantFlowLink
                {
                    FromResourceId = reheat.Id,
                    ToResourceId = hotMill.Id,
                    FromProcessOperationType = ProcessOperationType.Reheat,
                    ToProcessOperationType = ProcessOperationType.HotRoll,
                    CouplingType = FlowCouplingType.HotTransfer,
                    SupportsHotTransfer = true,
                    IsEnabled = true
                }
            },
            Array.Empty<ExternalMaterialSupply>(),
            committedSupplies,
            minimumRollingEntryTemperatureC.HasValue || maximumHotHoldMinutes.HasValue
                ? new[]
                {
                    new GradeProcessTemperatureRequirement
                    {
                        SteelGradeId = grade.Id,
                        ProcessOperationType = ProcessOperationType.HotRoll,
                        MinimumEntryTemperatureC = minimumRollingEntryTemperatureC,
                        MaximumHoldingMinutesAfterExit = maximumHotHoldMinutes
                    }
                }
                : Array.Empty<GradeProcessTemperatureRequirement>(),
            thermalReferenceTimeUtc ?? new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc));
    }

    private static Resource Resource(string code, ProcessUnitType unitType, ResourceType type) => new()
    {
        PlantId = Guid.NewGuid(),
        ProcessStageId = Guid.NewGuid(),
        Code = code,
        Name = code,
        ProcessUnitType = unitType,
        ResourceType = type,
        OperatingState = ResourceOperatingState.Available
    };
}
