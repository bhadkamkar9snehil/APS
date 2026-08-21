using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

public sealed class DownstreamRouteProjectionTests
{
    private const string RouteCode = "ROUTE-58";

    [Fact]
    public void Required_operation_before_first_hot_roll_is_projected_in_configured_order()
    {
        var fixture = InventoryFixture(knownHot: true);
        var conditioning = Resource("COND-1", ProcessUnitType.FinishingLine, ResourceType.FinishingLine);
        var hotMill = Resource("HRM-1", ProcessUnitType.HotRollingMill, ResourceType.RollingMill);
        var route = new RoutePlanningInput(
            new[]
            {
                Operation(10, ProcessOperationType.Finish, WorkOrderType.Finishing, "150X150", "150X150"),
                Operation(20, ProcessOperationType.HotRoll, WorkOrderType.HotRolling, "150X150", "HRC")
            },
            new[]
            {
                Capability(conditioning.Id, ProcessOperationType.Finish, "150X150", "150X150"),
                Capability(hotMill.Id, ProcessOperationType.HotRoll, "150X150", "HRC")
            });

        var result = MultiStageRouteProjector.Apply(
            fixture.Structure,
            fixture.CampaignPlan,
            route,
            new[] { conditioning, hotMill },
            Array.Empty<ResourceCapability>(),
            new[]
            {
                new PlantFlowLink
                {
                    FromResourceId = conditioning.Id,
                    ToResourceId = hotMill.Id,
                    FromProcessOperationType = ProcessOperationType.Finish,
                    ToProcessOperationType = ProcessOperationType.HotRoll,
                    CouplingType = FlowCouplingType.HotTransfer,
                    SupportsHotTransfer = true,
                    IsEnabled = true
                }
            },
            committedSupplies: fixture.CommittedSupplies);

        Assert.DoesNotContain(result.Issues, x => x.Severity == PlanningIssueSeverity.Error);
        var plans = result.RouteOperationPlans!.OrderBy(x => x.SequenceNumber).ToArray();
        Assert.Equal(2, plans.Length);
        Assert.Equal(ProcessOperationType.Finish, plans[0].ProcessOperationType);
        Assert.Equal(ProcessOperationType.HotRoll, plans[1].ProcessOperationType);
        Assert.Equal(fixture.RollingPlan.Id, plans[0].UpstreamPlanId);
        Assert.Equal(plans[0].Id, plans[1].UpstreamPlanId);
        Assert.Contains(result.SchedulingTasks, x => x.SourceEntityId == plans[0].Id);
        Assert.Contains(result.SchedulingTasks, x => x.SourceEntityId == plans[1].Id);
    }

    [Fact]
    public void Required_reheat_without_eligible_furnace_reports_named_infeasibility()
    {
        var fixture = InventoryFixture(knownHot: false);
        var hotMill = Resource("HRM-1", ProcessUnitType.HotRollingMill, ResourceType.RollingMill);
        var route = new RoutePlanningInput(
            new[]
            {
                Operation(10, ProcessOperationType.Reheat, WorkOrderType.HotRolling, "150X150", "150X150"),
                Operation(20, ProcessOperationType.HotRoll, WorkOrderType.HotRolling, "150X150", "HRC")
            },
            new[]
            {
                Capability(hotMill.Id, ProcessOperationType.HotRoll, "150X150", "HRC")
            });

        var result = MultiStageRouteProjector.Apply(
            fixture.Structure,
            fixture.CampaignPlan,
            route,
            new[] { hotMill },
            Array.Empty<ResourceCapability>());

        Assert.Contains(result.Issues, x =>
            x.Severity == PlanningIssueSeverity.Error &&
            x.Code == "REHEAT_RESOURCE_MISSING");
    }

    private static Fixture InventoryFixture(bool knownHot)
    {
        var po = new ProductionOrder
        {
            ProductionOrderNumber = "PO-58",
            DemandSource = DemandSourceType.MakeToOrder,
            MaterialCode = "FG-HRC",
            GradeCode = "G1",
            FinalCrossSectionCode = "HRC",
            CasterSectionCode = "150X150",
            RouteCode = RouteCode,
            PlannedQuantityMt = 50m,
            RemainingQuantityMt = 50m,
            RequiredDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            Priority = 4
        };
        var rolling = new RollingPlan
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
        rolling.Allocations.Add(new RollingPlanAllocation
        {
            RollingPlanId = rolling.Id,
            RollingPlan = rolling,
            ProductionOrderId = po.Id,
            ProductionOrder = po,
            PlannedQuantityMt = 50m,
            ExistingIntermediateInventoryMt = 50m
        });

        var sourceReference = knownHot ? "COMMITTED-HOT" : "YARD-LOT";
        var use = knownHot
            ? PlanningInventoryUse.CommittedInternalProductionFeed
            : PlanningInventoryUse.IntermediateFeed;
        var availableFrom = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        var material = new PlanningInventoryAllocation(
            po.Id,
            InventoryStage.CastIntermediate,
            po.MaterialCode,
            po.GradeCode,
            po.CasterSectionCode,
            "YARD",
            50m,
            use,
            sourceReference,
            availableFrom);
        var campaignPlan = new CampaignPlanningResult(
            Array.Empty<Campaign>(),
            Array.Empty<ProductionOrder>(),
            new Dictionary<Guid, decimal> { [po.Id] = 50m },
            new Dictionary<Guid, decimal> { [po.Id] = 0m },
            new Dictionary<Guid, decimal> { [po.Id] = 50m },
            new[] { material });
        var structure = new ProductionStructurePlanningResult(
            Array.Empty<CastSequence>(),
            new[] { rolling },
            Array.Empty<PlannedBilletSupply>(),
            Array.Empty<FiniteScheduleTask>(),
            Array.Empty<PlanningIssue>());
        var committed = knownHot
            ? new[]
            {
                new CommittedMaterialSupply(
                    Guid.NewGuid(),
                    po.Id,
                    null,
                    sourceReference,
                    BilletSupplySourceType.InternalCastPlanned,
                    null,
                    po.GradeCode,
                    po.CasterSectionCode,
                    50m,
                    availableFrom,
                    "YARD",
                    ChargeMode.HotBuffered)
            }
            : Array.Empty<CommittedMaterialSupply>();
        return new Fixture(rolling, campaignPlan, structure, committed);
    }

    private static ManufacturingRouteOperation Operation(
        int sequence,
        ProcessOperationType type,
        WorkOrderType workOrderType,
        string input,
        string output) => new()
    {
        ManufacturingRouteId = Guid.NewGuid(),
        RouteCode = RouteCode,
        SequenceNumber = sequence,
        ProcessOperationType = type,
        ReleaseWorkOrderType = workOrderType,
        Requirement = RequirementDisposition.Required,
        InputCrossSectionCode = input,
        OutputCrossSectionCode = output
    };

    private static RouteResourceCapability Capability(
        Guid resourceId,
        ProcessOperationType type,
        string input,
        string output) => new()
    {
        ResourceId = resourceId,
        RouteCode = RouteCode,
        ProcessOperationType = type,
        GradeCode = "G1",
        InputCrossSectionCode = input,
        OutputCrossSectionCode = output,
        ThroughputMtPerHour = 50m
    };

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

    private sealed record Fixture(
        RollingPlan RollingPlan,
        CampaignPlanningResult CampaignPlan,
        ProductionStructurePlanningResult Structure,
        IReadOnlyCollection<CommittedMaterialSupply> CommittedSupplies);
}
