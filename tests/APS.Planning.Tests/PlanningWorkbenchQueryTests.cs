using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class PlanningWorkbenchQueryTests
{
    [Fact]
    public async Task Aggregates_schedule_demand_campaign_material_and_exceptions_for_one_plan()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApsDbContext(options);

        var planId = Guid.NewGuid();
        var baselinePlanId = Guid.NewGuid();
        var plantId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var eafStageId = Guid.NewGuid();
        var lrfStageId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var originalLrfResourceId = Guid.NewGuid();
        var revisedLrfResourceId = Guid.NewGuid();
        var productionOrderId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var heatId = Guid.NewGuid();
        var start = new DateTime(2026, 8, 21, 8, 0, 0, DateTimeKind.Utc);

        db.PlanVersions.Add(new PlanVersion
        {
            Id = planId,
            VersionNumber = "PLAN-20260821-01",
            CreatedOnUtc = start
        });
        db.PlanVersions.Add(new PlanVersion
        {
            Id = baselinePlanId,
            VersionNumber = "PLAN-20260821-00",
            CreatedOnUtc = start.AddMinutes(-5)
        });
        db.PlanVersionStates.Add(new PlanVersionState
        {
            PlanVersionId = planId,
            ParentPlanVersionId = baselinePlanId,
            Status = PlanVersionStatus.Feasible,
            Trigger = PlanTriggerType.Manual,
            ReferenceTimeUtc = start,
            HorizonStartUtc = start,
            HorizonEndUtc = start.AddDays(7),
            SolverStatus = "Optimal",
            IsActive = true
        });
        db.PlanVersionStates.Add(new PlanVersionState
        {
            PlanVersionId = baselinePlanId,
            Status = PlanVersionStatus.Feasible,
            Trigger = PlanTriggerType.Manual,
            ReferenceTimeUtc = start,
            HorizonStartUtc = start,
            HorizonEndUtc = start.AddDays(7),
            SolverStatus = "Optimal",
            IsActive = false
        });
        db.Plants.Add(new Plant { Id = plantId, Code = "MELT", Name = "Melt Shop" });
        db.PlantAreas.Add(new PlantArea
        {
            Id = areaId,
            PlantId = plantId,
            Code = "SMS",
            Name = "Steel Melting Shop",
            SequenceNumber = 20
        });
        db.ProcessStages.AddRange(
            new ProcessStage
            {
                Id = eafStageId,
                PlantId = plantId,
                PlantAreaId = areaId,
                Code = "EAF",
                Name = "Primary melting",
                ProcessOperationType = ProcessOperationType.Eaf,
                SequenceNumber = 10
            },
            new ProcessStage
            {
                Id = lrfStageId,
                PlantId = plantId,
                PlantAreaId = areaId,
                Code = "LRF",
                Name = "Ladle refining",
                ProcessOperationType = ProcessOperationType.Lrf,
                SequenceNumber = 20
            });
        db.Resources.AddRange(new Resource
        {
            Id = resourceId,
            PlantId = plantId,
            ProcessStageId = eafStageId,
            Code = "EAF1-A",
            Name = "Electric Arc Furnace 1",
            ResourceType = ResourceType.Furnace,
            ProcessUnitType = ProcessUnitType.Eaf,
            OperatingState = ResourceOperatingState.Available
        }, new Resource
        {
            Id = originalLrfResourceId,
            PlantId = plantId,
            ProcessStageId = lrfStageId,
            Code = "LRF-01",
            Name = "Ladle Refining Furnace 1",
            ResourceType = ResourceType.Furnace,
            ProcessUnitType = ProcessUnitType.Lrf,
            OperatingState = ResourceOperatingState.Available
        }, new Resource
        {
            Id = revisedLrfResourceId,
            PlantId = plantId,
            ProcessStageId = lrfStageId,
            Code = "LRF-02",
            Name = "Ladle Refining Furnace 2",
            ResourceType = ResourceType.Furnace,
            ProcessUnitType = ProcessUnitType.Lrf,
            OperatingState = ResourceOperatingState.Available
        });
        db.ResourceCalendars.Add(new ResourceCalendar
        {
            ResourceId = revisedLrfResourceId,
            Start = start.AddHours(3),
            End = start.AddHours(4),
            IsAvailable = false,
            CapacityFactorPct = 0m,
            ReasonCode = "PLANNED_MAINTENANCE"
        });
        db.ResourceCalendars.Add(new ResourceCalendar
        {
            ResourceId = resourceId,
            Start = start.AddHours(1),
            End = start.AddHours(2),
            IsAvailable = true,
            CapacityFactorPct = 50m,
            ReasonCode = "ENERGY_DERATE"
        });
        db.PlanProductionOrderSnapshots.Add(new PlanProductionOrderSnapshot
        {
            PlanVersionId = planId,
            ProductionOrderId = productionOrderId,
            ProductionOrderNumber = "MTO-SO-1001-10",
            DemandSource = DemandSourceType.MakeToOrder,
            SalesOrderNumber = "SO-1001",
            SalesOrderItemNumber = "10",
            MaterialCode = "BAR-SAE1008-12",
            GradeCode = "SAE1008",
            FinalCrossSectionCode = "RND-12",
            CasterSectionCode = "BLT-150SQ",
            RouteCode = "STD-BAR",
            PlannedQuantityMt = 75m,
            RemainingQuantityMt = 75m,
            RequiredDate = start.AddDays(1),
            Status = ProductionOrderStatus.Planned,
            FreshSteelRequirementMt = 70m
        });
        db.PlanCampaignSnapshots.Add(new PlanCampaignSnapshot
        {
            PlanVersionId = planId,
            CampaignId = campaignId,
            CampaignNumber = "CMP-00001",
            GradeSequenceClassCode = "LOW-C",
            CasterSectionCode = "BLT-150SQ",
            RouteCode = "STD-BAR",
            PlannedQuantityMt = 70m,
            FreshSteelRequirementMt = 70m,
            RequiredDate = start.AddDays(1),
            Status = CampaignStatus.Planned
        });
        db.PlanCampaignAllocationSnapshots.Add(new PlanCampaignAllocationSnapshot
        {
            PlanVersionId = planId,
            CampaignId = campaignId,
            ProductionOrderId = productionOrderId,
            PlannedQuantityMt = 70m,
            FreshSteelQuantityMt = 70m
        });
        db.PlanHeatSnapshots.Add(new PlanHeatSnapshot
        {
            PlanVersionId = planId,
            CampaignHeatId = heatId,
            CampaignId = campaignId,
            SequenceNumber = 1,
            GradeCode = "SAE1008",
            PlannedQuantityMt = 70m
        });
        db.PlanOperationSnapshots.Add(new PlanOperationSnapshot
        {
            PlanVersionId = planId,
            PlanningKey = "HEAT:CMP-00001:H01:EAF",
            SourceEntityId = heatId,
            OperationType = PlanOperationType.Eaf,
            ProcessOperationType = ProcessOperationType.Eaf,
            ResourceId = resourceId,
            StartUtc = start,
            EndUtc = start.AddHours(1),
            QuantityMt = 70m,
            GradeCode = "SAE1008",
            CrossSectionCode = "BLT-150SQ",
            ExecutionStatus = OperationExecutionStatus.Running,
            ActualStartUtc = start.AddMinutes(5),
            ActualQuantityMt = 32m
        });
        db.PlanOperationSnapshots.Add(new PlanOperationSnapshot
        {
            PlanVersionId = planId,
            PlanningKey = "HEAT:CMP-00001:H01:LRF",
            SourceEntityId = heatId,
            OperationType = PlanOperationType.Lrf,
            ProcessOperationType = ProcessOperationType.Lrf,
            ResourceId = revisedLrfResourceId,
            PredecessorPlanningKeysJson = "[\"HEAT:CMP-00001:H01:EAF\"]",
            StartUtc = start.AddMinutes(90),
            EndUtc = start.AddMinutes(150),
            QuantityMt = 70m,
            GradeCode = "SAE1008",
            CrossSectionCode = "BLT-150SQ"
        });
        db.PlanOperationSnapshots.AddRange(
            new PlanOperationSnapshot
            {
                PlanVersionId = baselinePlanId,
                PlanningKey = "HEAT:CMP-00001:H01:EAF",
                SourceEntityId = heatId,
                OperationType = PlanOperationType.Eaf,
                ProcessOperationType = ProcessOperationType.Eaf,
                ResourceId = resourceId,
                StartUtc = start,
                EndUtc = start.AddHours(1),
                QuantityMt = 70m,
                GradeCode = "SAE1008",
                CrossSectionCode = "BLT-150SQ"
            },
            new PlanOperationSnapshot
            {
                PlanVersionId = baselinePlanId,
                PlanningKey = "HEAT:CMP-00001:H01:LRF",
                SourceEntityId = heatId,
                OperationType = PlanOperationType.Lrf,
                ProcessOperationType = ProcessOperationType.Lrf,
                ResourceId = originalLrfResourceId,
                StartUtc = start.AddMinutes(75),
                EndUtc = start.AddMinutes(135),
                QuantityMt = 70m,
                GradeCode = "SAE1008",
                CrossSectionCode = "BLT-150SQ"
            });
        db.PlanOperationResourceOptionSnapshots.Add(new PlanOperationResourceOptionSnapshot
        {
            PlanVersionId = planId,
            PlanningKey = "HEAT:CMP-00001:H01:EAF",
            SourceEntityId = heatId,
            ProcessOperationType = ProcessOperationType.Eaf,
            ResourceId = resourceId,
            DurationMinutes = 60,
            WasSelected = true,
            EligibilityBasisCode = "ROUTE_GRADE_CAPABILITY"
        });
        await db.SaveChangesAsync();

        var service = new PlannerWorkspaceQueryService(db, new PlanVersionRepository(db));
        var result = await service.GetPlanningWorkbenchAsync(planId);

        Assert.NotNull(result);
        Assert.Equal("PLAN-20260821-01", result.Plan.VersionNumber);
        Assert.Equal(2, result.Schedule.ResourceLanes.Count);
        var eafLane = Assert.Single(result.Schedule.ResourceLanes, x => x.ResourceId == resourceId);
        Assert.Equal(plantId, eafLane.PlantId);
        Assert.Equal("MELT", eafLane.PlantCode);
        Assert.Equal(areaId, eafLane.AreaId);
        Assert.Equal("SMS", eafLane.AreaCode);
        Assert.Equal(eafStageId, eafLane.ProcessStageId);
        Assert.Equal("EAF", eafLane.ProcessStageCode);
        Assert.Equal(20_010_000, eafLane.DisplayOrder);
        Assert.Single(result.Demand.Rows);
        Assert.Single(result.Campaigns.Campaigns);
        Assert.Equal(2, result.OperationDetails.Count);
        var eafDetail = Assert.Single(result.OperationDetails, x => x.PlanningKey.EndsWith(":EAF"));
        Assert.Single(eafDetail.ResourceOptions);
        Assert.Equal(OperationExecutionStatus.Running, eafDetail.ExecutionStatus);
        Assert.Equal(start.AddMinutes(5), eafDetail.ActualStartUtc);
        Assert.Equal(32m, eafDetail.ActualQuantityMt);
        Assert.Empty(result.Material.Pools);
        Assert.Equal(1, result.Queue.TotalDemand);
        Assert.Equal(0, result.Queue.UnscheduledDemand);
        Assert.Contains(result.Exceptions, x => x.Kind == PlanningWorkbenchExceptionKind.UncoveredDemand);

        var dependency = Assert.Single(result.DependencyLinks);
        Assert.Equal("HEAT:CMP-00001:H01:EAF", dependency.PredecessorPlanningKey);
        Assert.Equal("HEAT:CMP-00001:H01:LRF", dependency.SuccessorPlanningKey);
        Assert.Equal(PlanningDependencyType.FinishStart, dependency.Type);
        Assert.Equal(PlanningDependencyCategory.Routing, dependency.Category);
        Assert.Null(dependency.MinimumLagMinutes);
        Assert.Equal(30, dependency.CurrentLagMinutes);

        Assert.Equal(2, result.ResourceCalendarIntervals.Count);
        var calendar = Assert.Single(result.ResourceCalendarIntervals, x => !x.IsAvailable);
        Assert.Equal(revisedLrfResourceId, calendar.ResourceId);
        Assert.Equal(start.AddHours(3), calendar.StartUtc);
        Assert.Equal(start.AddHours(4), calendar.EndUtc);
        Assert.False(calendar.IsAvailable);
        Assert.Equal("PLANNED_MAINTENANCE", calendar.ReasonCode);

        Assert.Equal(2, result.BaselinePlacements.Count);
        var unchanged = Assert.Single(result.BaselinePlacements, x => x.PlanningKey.EndsWith(":EAF"));
        Assert.Equal(resourceId, unchanged.ResourceId);
        Assert.Equal(start, unchanged.StartUtc);
        var resourceChanged = Assert.Single(result.BaselinePlacements, x => x.PlanningKey.EndsWith(":LRF"));
        Assert.Equal(originalLrfResourceId, resourceChanged.ResourceId);
        Assert.Equal("LRF-01", resourceChanged.ResourceCode);
        Assert.Equal("LRF", resourceChanged.ProcessStageCode);
        Assert.Equal(start.AddMinutes(75), resourceChanged.StartUtc);

        var processingBucket = Assert.Single(result.CapacityBuckets, x =>
            x.ResourceId == resourceId && x.StartUtc == start);
        Assert.Equal(60, processingBucket.AvailableMinutes);
        Assert.Equal(60, processingBucket.ProcessingMinutes);
        Assert.Equal(0, processingBucket.UnavailableMinutes);
        Assert.Equal(1m, processingBucket.OccupancyRatio);
        Assert.Equal(PlanningCapacityBasis.MachineTime, processingBucket.Basis);

        var calendarFactorBucket = Assert.Single(result.CapacityBuckets, x =>
            x.ResourceId == resourceId && x.StartUtc == start.AddHours(1));
        // A unary/disjunctive resource remains one available machine-hour. Capacity factors scale
        // cumulative capacity units; they do not manufacture fractional clock time for unary machines.
        Assert.Equal(60, calendarFactorBucket.AvailableMinutes);
        Assert.Equal(0, calendarFactorBucket.ProcessingMinutes);
        Assert.Equal(0, calendarFactorBucket.UnavailableMinutes);

        var maintenanceBucket = Assert.Single(result.CapacityBuckets, x =>
            x.ResourceId == revisedLrfResourceId && x.StartUtc == start.AddHours(3));
        Assert.Equal(0, maintenanceBucket.AvailableMinutes);
        Assert.Equal(0, maintenanceBucket.ProcessingMinutes);
        Assert.Equal(60, maintenanceBucket.UnavailableMinutes);
    }
}
