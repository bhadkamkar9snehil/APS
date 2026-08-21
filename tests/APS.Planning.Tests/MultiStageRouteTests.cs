using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

public sealed class MultiStageRouteTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Mill_outage_that_expires_direct_hot_window_preserves_upstream_billet_plan(
        bool reheatAvailable)
    {
        var grade = new SteelGrade { GradeCode = "G1", Description = "G1" };
        var po = Order("PO-THERMAL-RECOVERY", "HRC", 60m);
        po.SteelGrade = grade;
        var plant = Guid.NewGuid();
        var eaf = PrimaryFurnace(plant, "EAF-1");
        var caster = Resource(plant, "CCM-1", ResourceType.Caster, ProcessUnitType.Ccm, 4);
        var reheat = Resource(plant, "RHF-1", ResourceType.Furnace, ProcessUnitType.ReheatingFurnace);
        reheat.NominalResidenceMinutes = 30;
        if (!reheatAvailable) reheat.OperatingState = ResourceOperatingState.Breakdown;
        var hotMill = Resource(plant, "HRM-1", ResourceType.RollingMill, ProcessUnitType.HotRollingMill);
        var start = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);
        var routeId = Guid.NewGuid();
        var direct = Link(caster.Id, hotMill.Id, ProcessOperationType.Ccm, ProcessOperationType.HotRoll, hot: true, minMinutes: 10);
        direct.NominalTemperatureLossCPerMinute = 5m;

        var result = Run(
            po,
            new[] { eaf, caster, reheat, hotMill },
            new[]
            {
                Operation(routeId, 10, ProcessOperationType.Reheat, WorkOrderType.HotRolling, "150X150", "150X150", requirement: RequirementDisposition.Optional),
                Operation(routeId, 20, ProcessOperationType.HotRoll, WorkOrderType.HotRolling, "150X150", "HRC")
            },
            new[] { Capability(hotMill.Id, ProcessOperationType.HotRoll, "150X150", "HRC", 60m) },
            new[]
            {
                direct,
                Link(caster.Id, reheat.Id, ProcessOperationType.Ccm, ProcessOperationType.Reheat),
                Link(reheat.Id, hotMill.Id, ProcessOperationType.Reheat, ProcessOperationType.HotRoll, hot: true)
            },
            new[]
            {
                new GradeProcessTemperatureRequirement
                {
                    SteelGradeId = grade.Id,
                    ProcessOperationType = ProcessOperationType.HotRoll,
                    MinimumEntryTemperatureC = 1000m
                }
            },
            new[]
            {
                new ResourceTemperatureCapability
                {
                    ResourceId = caster.Id,
                    ProcessOperationType = ProcessOperationType.Ccm,
                    NominalExitTemperatureC = 1100m
                },
                new ResourceTemperatureCapability
                {
                    ResourceId = reheat.Id,
                    ProcessOperationType = ProcessOperationType.Reheat,
                    NominalExitTemperatureC = 1100m,
                    CanCorrectTemperature = true
                }
            },
            new[] { grade },
            new[]
            {
                new ResourceCalendar
                {
                    ResourceId = caster.Id,
                    Start = start.AddHours(3),
                    End = start.AddDays(10),
                    IsAvailable = false,
                    ReasonCode = "CASTER_MAINTENANCE"
                },
                new ResourceCalendar
                {
                    ResourceId = hotMill.Id,
                    Start = start,
                    End = start.AddHours(5),
                    IsAvailable = false,
                    ReasonCode = "MILL_OUTAGE"
                }
            });

        Assert.Contains(result.ProductionStructure.SchedulingTasks, x => x.ProcessOperationType == ProcessOperationType.Ccm);
        if (reheatAvailable)
        {
            Assert.True(result.IsFeasible, string.Join("; ", result.Schedule.Issues.Select(x => x.Message)));
            Assert.Contains(result.ProductionStructure.SchedulingTasks, x => x.ProcessOperationType == ProcessOperationType.Reheat);
            Assert.Contains(result.ProductionStructure.RouteOperationDecisions!, x =>
                x.ProcessOperationType == ProcessOperationType.Reheat &&
                x.ReasonCode == "SCHEDULED_THERMAL_WINDOW_REQUIRES_REHEAT");
            var thermal = Assert.Single(result.ProductionStructure.BilletThermalDecisions!);
            Assert.Equal(BilletThermalOutcome.Reheated, thermal.Outcome);
            Assert.True(thermal.ReheatRequiredByThermalState);
        }
        else
        {
            Assert.False(result.IsFeasible);
            Assert.Contains(result.ProductionStructure.Issues, x => x.Code == "REHEAT_RESOURCE_MISSING");
            Assert.DoesNotContain(result.ProductionStructure.SchedulingTasks, x => x.ProcessOperationType == ProcessOperationType.HotRoll);
        }
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData(12, 12)]
    public void Fresh_internal_billet_carries_effective_hot_charge_window_into_the_solver(
        int? orderMaximumQueueMinutes,
        int expectedMaximumLagMinutes)
    {
        var grade = new SteelGrade { GradeCode = "G1", Description = "G1" };
        var po = Order("PO-THERMAL-HOT", "HRC", 60m);
        po.SteelGrade = grade;
        if (orderMaximumQueueMinutes.HasValue)
        {
            po.Requirement = new ProductionOrderRequirement
            {
                ProductionOrderId = po.Id,
                ProductionOrder = po,
                ProcessOverrides =
                {
                    new OrderProcessRequirement
                    {
                        ProcessOperationType = ProcessOperationType.HotRoll,
                        Requirement = RequirementDisposition.Required,
                        MaximumQueueMinutes = orderMaximumQueueMinutes
                    }
                }
            };
        }

        var plant = Guid.NewGuid();
        var eaf = PrimaryFurnace(plant, "EAF-1");
        var caster = Resource(plant, "CCM-1", ResourceType.Caster, ProcessUnitType.Ccm, 4);
        var hotMill = Resource(plant, "HRM-1", ResourceType.RollingMill, ProcessUnitType.HotRollingMill);
        var routeId = Guid.NewGuid();
        var link = Link(
            caster.Id,
            hotMill.Id,
            ProcessOperationType.Ccm,
            ProcessOperationType.HotRoll,
            hot: true,
            minMinutes: 10);
        link.NominalTemperatureLossCPerMinute = 5m;

        var result = Run(
            po,
            new[] { eaf, caster, hotMill },
            new[] { Operation(routeId, 10, ProcessOperationType.HotRoll, WorkOrderType.HotRolling, "150X150", "HRC") },
            new[] { Capability(hotMill.Id, ProcessOperationType.HotRoll, "150X150", "HRC", 60m) },
            new[] { link },
            new[]
            {
                new GradeProcessTemperatureRequirement
                {
                    SteelGradeId = grade.Id,
                    ProcessOperationType = ProcessOperationType.HotRoll,
                    MinimumEntryTemperatureC = 1000m
                }
            },
            new[]
            {
                new ResourceTemperatureCapability
                {
                    ResourceId = caster.Id,
                    ProcessOperationType = ProcessOperationType.Ccm,
                    NominalExitTemperatureC = 1100m
                }
            },
            new[] { grade });

        Assert.True(result.IsFeasible, string.Join("; ", result.Schedule.Issues.Select(x => x.Message)));
        var hotRoll = Assert.Single(result.ProductionStructure.SchedulingTasks, x =>
            x.ProcessOperationType == ProcessOperationType.HotRoll);
        var dependency = Assert.Single(hotRoll.Dependencies);
        var pair = Assert.Single(dependency.AllowedResourcePairs!);
        Assert.Equal(10, pair.MinimumLagMinutes);
        Assert.Equal(expectedMaximumLagMinutes, pair.MaximumLagMinutes);

        var thermal = Assert.Single(result.ProductionStructure.BilletThermalDecisions!);
        Assert.Equal(BilletThermalSourceBasis.PlannedCcm, thermal.SourceBasis);
        Assert.Equal(BilletThermalOutcome.HotDirect, thermal.Outcome);
        Assert.Equal("ROLLING_ENTRY_TEMPERATURE_PROVEN", thermal.ReasonCode);
        Assert.Equal(1100m, thermal.SourceTemperatureC);
        Assert.Equal(1000m, thermal.MinimumRollingEntryTemperatureC);
        Assert.Equal(5m, thermal.TemperatureLossCPerMinute);
        Assert.Equal(expectedMaximumLagMinutes, thermal.MaximumHotHoldMinutes);
        Assert.True(thermal.PredictedOrActualRollingEntryTemperatureC >= 1000m);
    }

    [Fact]
    public void Configured_route_projects_first_hot_roll_cold_roll_and_finishing_as_one_route_chain()
    {
        var po = Order("PO-ROUTE-1", "1.0MM", 100m);
        var plant = Guid.NewGuid();
        var eaf = PrimaryFurnace(plant, "EAF-1");
        var caster = Resource(plant, "CCM-1", ResourceType.Caster, ProcessUnitType.Ccm, 4);
        var hotMill = Resource(plant, "HRM-1", ResourceType.RollingMill, ProcessUnitType.HotRollingMill);
        var coldMill = Resource(plant, "CRM-1", ResourceType.RollingMill, ProcessUnitType.ColdRollingMill);
        var finishing = Resource(plant, "FIN-1", ResourceType.FinishingLine, ProcessUnitType.FinishingLine);
        var resources = new[] { eaf, caster, hotMill, coldMill, finishing };

        var routeId = Guid.NewGuid();
        var routeOperations = new[]
        {
            Operation(routeId, 10, ProcessOperationType.HotRoll, WorkOrderType.HotRolling, "150X150", "HRC"),
            Operation(routeId, 20, ProcessOperationType.ColdRoll, WorkOrderType.ColdRolling, "HRC", "CRC", minQueueMinutes: 30),
            Operation(routeId, 30, ProcessOperationType.Finish, WorkOrderType.Finishing, "CRC", "1.0MM", minQueueMinutes: 15)
        };
        var routeCapabilities = new[]
        {
            Capability(hotMill.Id, ProcessOperationType.HotRoll, "150X150", "HRC", 50m),
            Capability(coldMill.Id, ProcessOperationType.ColdRoll, "HRC", "CRC", 40m),
            Capability(finishing.Id, ProcessOperationType.Finish, "CRC", "1.0MM", 35m)
        };
        var links = new[]
        {
            Link(caster.Id, hotMill.Id, ProcessOperationType.Ccm, ProcessOperationType.HotRoll, hot: true, minMinutes: 10),
            Link(hotMill.Id, coldMill.Id, ProcessOperationType.HotRoll, ProcessOperationType.ColdRoll),
            Link(coldMill.Id, finishing.Id, ProcessOperationType.ColdRoll, ProcessOperationType.Finish)
        };

        var result = Run(po, resources, routeOperations, routeCapabilities, links);

        Assert.True(result.IsFeasible, string.Join("; ", result.Schedule.Issues.Select(x => x.Message)));
        var rollingDemand = Assert.Single(result.ProductionStructure.RollingPlans);
        var routePlans = result.ProductionStructure.RouteOperationPlans!.OrderBy(x => x.SequenceNumber).ToArray();
        Assert.Equal(3, routePlans.Length);
        Assert.Equal(
            new[] { ProcessOperationType.HotRoll, ProcessOperationType.ColdRoll, ProcessOperationType.Finish },
            routePlans.Select(x => x.ProcessOperationType).ToArray());
        Assert.Equal(rollingDemand.Id, routePlans[0].UpstreamPlanId);
        Assert.Equal(routePlans[0].Id, routePlans[1].UpstreamPlanId);
        Assert.Equal(routePlans[1].Id, routePlans[2].UpstreamPlanId);

        AssertAssignments(result, routePlans[0].Id, hotMill.Id);
        AssertAssignments(result, routePlans[1].Id, coldMill.Id);
        AssertAssignments(result, routePlans[2].Id, finishing.Id);

        var coldTasks = result.ProductionStructure.SchedulingTasks.Where(x => x.SourceEntityId == routePlans[1].Id).ToArray();
        var finishTasks = result.ProductionStructure.SchedulingTasks.Where(x => x.SourceEntityId == routePlans[2].Id).ToArray();
        Assert.NotEmpty(coldTasks);
        Assert.Equal(coldTasks.Length, finishTasks.Length);
        Assert.All(coldTasks, task => Assert.All(task.Dependencies, dependency => Assert.True(
            dependency.AllowedResourcePairs is { Count: > 0 } || dependency.MinimumLagMinutes == 30)));
        Assert.All(finishTasks, task => Assert.All(task.Dependencies, dependency => Assert.True(
            dependency.AllowedResourcePairs is { Count: > 0 } || dependency.MinimumLagMinutes == 15)));

        var release = new PlanReleaseBuilder().Build(new PlanReleaseBuildRequest(
            result.PlanVersionId,
            result.CampaignPlan.Campaigns,
            result.ProductionStructure,
            result.Schedule));
        Assert.Contains(release.WorkOrders, x => x.WorkOrderType == WorkOrderType.HotRolling);
        Assert.Contains(release.WorkOrders, x => x.WorkOrderType == WorkOrderType.ColdRolling);
        Assert.Contains(release.WorkOrders, x => x.WorkOrderType == WorkOrderType.Finishing);
        Assert.All(release.WorkOrders.Where(x => x.WorkOrderType is WorkOrderType.HotRolling or WorkOrderType.ColdRolling or WorkOrderType.Finishing),
            workOrder => Assert.Contains(workOrder.Allocations, allocation => allocation.ProductionOrderId == po.Id));
    }

    [Fact]
    public void Hot_roll_reheat_hot_roll_is_projected_in_order_and_keeps_distinct_mills()
    {
        var po = Order("PO-TWO-MILLS", "FINAL", 60m);
        var plant = Guid.NewGuid();
        var eaf = PrimaryFurnace(plant, "EAF-1");
        var caster = Resource(plant, "CCM-1", ResourceType.Caster, ProcessUnitType.Ccm, 4);
        var firstMill = Resource(plant, "HRM-1", ResourceType.RollingMill, ProcessUnitType.HotRollingMill);
        var reheat = Resource(plant, "RHF-1", ResourceType.Furnace, ProcessUnitType.ReheatingFurnace);
        reheat.NominalResidenceMinutes = 20;
        var secondMill = Resource(plant, "HRM-2", ResourceType.RollingMill, ProcessUnitType.HotRollingMill);

        var routeId = Guid.NewGuid();
        var routeOperations = new[]
        {
            Operation(routeId, 10, ProcessOperationType.HotRoll, WorkOrderType.HotRolling, "150X150", "INTERMEDIATE"),
            Operation(routeId, 20, ProcessOperationType.Reheat, WorkOrderType.HotRolling, "INTERMEDIATE", "INTERMEDIATE"),
            Operation(routeId, 30, ProcessOperationType.HotRoll, WorkOrderType.HotRolling, "INTERMEDIATE", "FINAL")
        };
        var routeCapabilities = new[]
        {
            Capability(firstMill.Id, ProcessOperationType.HotRoll, "150X150", "INTERMEDIATE", 60m),
            Capability(secondMill.Id, ProcessOperationType.HotRoll, "INTERMEDIATE", "FINAL", 60m)
        };
        var links = new[]
        {
            Link(caster.Id, firstMill.Id, ProcessOperationType.Ccm, ProcessOperationType.HotRoll, hot: true),
            Link(firstMill.Id, reheat.Id, ProcessOperationType.HotRoll, ProcessOperationType.Reheat),
            Link(reheat.Id, secondMill.Id, ProcessOperationType.Reheat, ProcessOperationType.HotRoll, hot: true)
        };

        var result = Run(po, new[] { eaf, caster, firstMill, reheat, secondMill }, routeOperations, routeCapabilities, links);

        Assert.True(result.IsFeasible, string.Join("; ", result.Schedule.Issues.Select(x => x.Message)));
        var plans = result.ProductionStructure.RouteOperationPlans!.OrderBy(x => x.SequenceNumber).ToArray();
        Assert.Equal(
            new[] { ProcessOperationType.HotRoll, ProcessOperationType.Reheat, ProcessOperationType.HotRoll },
            plans.Select(x => x.ProcessOperationType).ToArray());
        Assert.Contains(result.ProductionStructure.SchedulingTasks, x =>
            x.SourceEntityId == plans[1].Id && x.TaskType == FiniteScheduleTaskType.Reheating);
        AssertAssignments(result, plans[0].Id, firstMill.Id);
        AssertAssignments(result, plans[2].Id, secondMill.Id);
    }

    [Fact]
    public void Required_reheat_hot_roll_and_finishing_remain_one_configured_route_chain()
    {
        var po = Order("PO-RHF-RM-FIN", "FINAL", 60m);
        var plant = Guid.NewGuid();
        var eaf = PrimaryFurnace(plant, "EAF-1");
        var caster = Resource(plant, "CCM-1", ResourceType.Caster, ProcessUnitType.Ccm, 4);
        var reheat = Resource(plant, "RHF-1", ResourceType.Furnace, ProcessUnitType.ReheatingFurnace);
        reheat.NominalResidenceMinutes = 20;
        var hotMill = Resource(plant, "HRM-1", ResourceType.RollingMill, ProcessUnitType.HotRollingMill);
        var finishing = Resource(plant, "FIN-1", ResourceType.FinishingLine, ProcessUnitType.FinishingLine);

        var routeId = Guid.NewGuid();
        var routeOperations = new[]
        {
            Operation(routeId, 10, ProcessOperationType.Reheat, WorkOrderType.HotRolling, "150X150", "150X150"),
            Operation(routeId, 20, ProcessOperationType.HotRoll, WorkOrderType.HotRolling, "150X150", "HRC"),
            Operation(routeId, 30, ProcessOperationType.Finish, WorkOrderType.Finishing, "HRC", "FINAL")
        };
        var routeCapabilities = new[]
        {
            Capability(hotMill.Id, ProcessOperationType.HotRoll, "150X150", "HRC", 60m),
            Capability(finishing.Id, ProcessOperationType.Finish, "HRC", "FINAL", 60m)
        };
        var links = new[]
        {
            Link(caster.Id, reheat.Id, ProcessOperationType.Ccm, ProcessOperationType.Reheat),
            Link(reheat.Id, hotMill.Id, ProcessOperationType.Reheat, ProcessOperationType.HotRoll, hot: true),
            Link(hotMill.Id, finishing.Id, ProcessOperationType.HotRoll, ProcessOperationType.Finish)
        };

        var result = Run(po, new[] { eaf, caster, reheat, hotMill, finishing }, routeOperations, routeCapabilities, links);

        Assert.True(result.IsFeasible, string.Join("; ", result.Schedule.Issues.Select(x => x.Message)));
        var plans = result.ProductionStructure.RouteOperationPlans!.OrderBy(x => x.SequenceNumber).ToArray();
        Assert.Equal(
            new[] { ProcessOperationType.Reheat, ProcessOperationType.HotRoll, ProcessOperationType.Finish },
            plans.Select(x => x.ProcessOperationType).ToArray());
        Assert.Contains(result.ProductionStructure.SchedulingTasks, x =>
            x.SourceEntityId == plans[0].Id && x.TaskType == FiniteScheduleTaskType.Reheating);
        AssertAssignments(result, plans[1].Id, hotMill.Id);
        AssertAssignments(result, plans[2].Id, finishing.Id);

        var release = new PlanReleaseBuilder().Build(new PlanReleaseBuildRequest(
            result.PlanVersionId,
            result.CampaignPlan.Campaigns,
            result.ProductionStructure,
            result.Schedule));
        Assert.Contains(release.WorkOrders, x => x.WorkOrderType == WorkOrderType.HotRolling);
        Assert.Contains(release.WorkOrders, x => x.WorkOrderType == WorkOrderType.Finishing);
    }

    private static PlanningRunResult Run(
        ProductionOrder po,
        IReadOnlyCollection<Resource> resources,
        IReadOnlyCollection<ManufacturingRouteOperation> operations,
        IReadOnlyCollection<RouteResourceCapability> routeCapabilities,
        IReadOnlyCollection<PlantFlowLink> links,
        IReadOnlyCollection<GradeProcessTemperatureRequirement>? gradeTemperatureRequirements = null,
        IReadOnlyCollection<ResourceTemperatureCapability>? resourceTemperatureCapabilities = null,
        IReadOnlyCollection<SteelGrade>? steelGrades = null,
        IReadOnlyCollection<ResourceCalendar>? resourceCalendars = null)
    {
        var caster = resources.Single(x => x.ProcessUnitType == ProcessUnitType.Ccm);
        var capabilities = new[]
        {
            new ResourceCapability
            {
                ResourceId = caster.Id,
                RouteCode = po.RouteCode,
                GradeCode = po.GradeCode,
                OutputCrossSectionCode = po.CasterSectionCode,
                ThroughputMtPerHour = 60m
            }
        };
        var start = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);
        return new PlanningEngine(
            new CampaignPlanningService(),
            new ProductionStructurePlanningService(),
            new FiniteScheduleOptimizer()).Run(new PlanningRunRequest(
                new[] { po },
                Array.Empty<InventoryPosition>(),
                resources,
                capabilities,
                resourceCalendars ?? Array.Empty<ResourceCalendar>(),
                Array.Empty<TransitionRule>(),
                links,
                new CampaignPlanningPolicy(60m, 50m, 70m, 250m, 300m),
                new ProductionStructurePlanningPolicy(MaximumHeatsPerCastSequence: 8),
                start,
                start.AddDays(10),
                5,
                RoutePlanning: new RoutePlanningInput(operations, routeCapabilities),
                SteelGrades: steelGrades,
                GradeTemperatureRequirements: gradeTemperatureRequirements,
                ResourceTemperatureCapabilities: resourceTemperatureCapabilities));
    }

    private static void AssertAssignments(PlanningRunResult result, Guid sourceId, Guid resourceId)
    {
        var assignments = result.Schedule.Assignments.Where(x => x.SourceEntityId == sourceId).ToArray();
        Assert.NotEmpty(assignments);
        Assert.All(assignments, x => Assert.Equal(resourceId, x.ResourceId));
    }

    private static ProductionOrder Order(string number, string finalSection, decimal quantity) => new()
    {
        ProductionOrderNumber = number,
        DemandSource = DemandSourceType.MakeToOrder,
        MaterialCode = $"FG-{finalSection}",
        GradeCode = "G1",
        GradeSequenceClassCode = "SEQ-A",
        FinalCrossSectionCode = finalSection,
        CasterSectionCode = "150X150",
        RouteCode = "ROUTE-COLD",
        PlannedQuantityMt = quantity,
        RemainingQuantityMt = quantity,
        RequiredDate = new DateTime(2026, 8, 23),
        Priority = 2
    };

    private static Resource PrimaryFurnace(Guid plant, string code)
    {
        var resource = Resource(plant, code, ResourceType.Furnace, ProcessUnitType.Eaf);
        resource.MinimumHeatWeightMt = 50m;
        resource.NominalHeatWeightMt = 60m;
        resource.MaximumHeatWeightMt = 70m;
        return resource;
    }

    private static Resource Resource(
        Guid plant,
        string code,
        ResourceType type,
        ProcessUnitType unitType,
        int? strands = null) => new()
        {
            PlantId = plant,
            ProcessStageId = Guid.NewGuid(),
            Code = code,
            Name = code,
            ResourceType = type,
            ProcessUnitType = unitType,
            StrandCount = strands,
            OperatingState = ResourceOperatingState.Available
        };

    private static ManufacturingRouteOperation Operation(
        Guid routeId,
        int sequence,
        ProcessOperationType processType,
        WorkOrderType workOrderType,
        string input,
        string output,
        int minQueueMinutes = 0,
        RequirementDisposition requirement = RequirementDisposition.Required) => new()
        {
            ManufacturingRouteId = routeId,
            RouteCode = "ROUTE-COLD",
            SequenceNumber = sequence,
            ProcessOperationType = processType,
            ReleaseWorkOrderType = workOrderType,
            Requirement = requirement,
            InputCrossSectionCode = input,
            OutputCrossSectionCode = output,
            MinimumQueueTime = TimeSpan.FromMinutes(minQueueMinutes),
            YieldPct = 100m
        };

    private static RouteResourceCapability Capability(
        Guid resourceId,
        ProcessOperationType processType,
        string input,
        string output,
        decimal throughput) => new()
        {
            ResourceId = resourceId,
            RouteCode = "ROUTE-COLD",
            ProcessOperationType = processType,
            GradeCode = "G1",
            InputCrossSectionCode = input,
            OutputCrossSectionCode = output,
            ThroughputMtPerHour = throughput
        };

    private static PlantFlowLink Link(
        Guid from,
        Guid to,
        ProcessOperationType fromProcess,
        ProcessOperationType toProcess,
        bool hot = false,
        int minMinutes = 0) => new()
        {
            FromResourceId = from,
            ToResourceId = to,
            FromProcessOperationType = fromProcess,
            ToProcessOperationType = toProcess,
            CouplingType = hot ? FlowCouplingType.HotTransfer : FlowCouplingType.Buffered,
            MinimumTransferTime = TimeSpan.FromMinutes(minMinutes),
            SupportsHotTransfer = hot,
            IsEnabled = true
        };
}
