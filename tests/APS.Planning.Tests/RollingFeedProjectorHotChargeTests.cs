using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

public sealed class RollingFeedProjectorHotChargeTests
{
    private const string RouteCode = "ROUTE-BILLET";

    [Fact]
    public void Committed_internal_production_feed_known_hot_bypasses_reheat()
    {
        var result = Run(PlanningInventoryUse.CommittedInternalProductionFeed, ChargeMode.HotDirect);

        Assert.DoesNotContain(result.SchedulingTasks, x => x.TaskType == FiniteScheduleTaskType.Reheating);
        Assert.DoesNotContain(result.Issues, x => x.Code == "REHEAT_ROUTE_MISSING");
    }

    [Fact]
    public void Committed_internal_production_feed_with_no_known_thermal_state_requires_reheat()
    {
        var result = Run(PlanningInventoryUse.CommittedInternalProductionFeed, thermalState: null);

        Assert.Contains(result.SchedulingTasks, x => x.TaskType == FiniteScheduleTaskType.Reheating);
    }

    [Fact]
    public void Existing_yard_inventory_feed_always_requires_reheat()
    {
        var result = Run(PlanningInventoryUse.IntermediateFeed, thermalState: null);

        Assert.Contains(result.SchedulingTasks, x => x.TaskType == FiniteScheduleTaskType.Reheating);
    }

    private static ProductionStructurePlanningResult Run(PlanningInventoryUse use, ChargeMode? thermalState)
    {
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

        var reheatMill = new Resource
        {
            PlantId = Guid.NewGuid(),
            ProcessStageId = Guid.NewGuid(),
            Code = "RHF-1",
            Name = "RHF-1",
            ResourceType = ResourceType.Furnace,
            ProcessUnitType = ProcessUnitType.ReheatingFurnace,
            OperatingState = ResourceOperatingState.Available,
            NominalResidenceMinutes = 30
        };

        var rollingTask = new FiniteScheduleTask(
            Guid.NewGuid(), plan.Id, FiniteScheduleTaskType.HotRolling, "Roll", "G1", "HRC", 50m,
            null, po.RequiredDate, 1,
            new[] { new FiniteScheduleResourceOption(Guid.NewGuid(), 60, 0, "MILL") },
            Array.Empty<FiniteScheduleDependency>(),
            ProcessOperationType.Unknown);

        var structure = new ProductionStructurePlanningResult(
            Array.Empty<CastSequence>(),
            new[] { plan },
            Array.Empty<PlannedBilletSupply>(),
            new[] { rollingTask },
            Array.Empty<PlanningIssue>());

        const string sourceReference = "SRC-1";
        var allocation = new PlanningInventoryAllocation(
            po.Id, InventoryStage.CastIntermediate, po.MaterialCode, po.GradeCode, po.CasterSectionCode,
            "YARD", 50m, use, sourceReference, DateTime.UtcNow);
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
                    Guid.NewGuid(), po.Id, null, sourceReference, BilletSupplySourceType.InternalCastPlanned,
                    null, "G1", "150X150", 50m, DateTime.UtcNow, "YARD", thermalState)
            }
            : Array.Empty<CommittedMaterialSupply>();

        var routePlanning = new RoutePlanningInput(
            new[]
            {
                new ManufacturingRouteOperation
                {
                    ManufacturingRouteId = Guid.NewGuid(),
                    RouteCode = RouteCode,
                    SequenceNumber = 1,
                    ProcessOperationType = ProcessOperationType.Reheat,
                    ReleaseWorkOrderType = WorkOrderType.HotRolling
                }
            },
            Array.Empty<RouteResourceCapability>());

        return RollingFeedProjector.Apply(
            structure,
            campaignPlan,
            routePlanning,
            new[] { reheatMill },
            Array.Empty<ResourceCapability>(),
            Array.Empty<PlantFlowLink>(),
            Array.Empty<ExternalMaterialSupply>(),
            committedSupplies);
    }
}
