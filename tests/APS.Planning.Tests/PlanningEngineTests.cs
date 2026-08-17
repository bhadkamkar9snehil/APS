using APS.Application;
using APS.Domain;
using APS.Planning;
using Xunit;

namespace APS.Planning.Tests;

public sealed class PlanningEngineTests
{
    [Fact]
    public void End_to_end_plan_preserves_mill_sequence_and_setup_time()
    {
        var po16 = NewPo("PO-16", "16MM", 100m);
        var po20 = NewPo("PO-20", "20MM", 100m);
        var plantId = Guid.NewGuid();
        var caster = new Resource
        {
            PlantId = plantId,
            ProcessStageId = Guid.NewGuid(),
            Code = "CCM-1",
            Name = "CCM-1",
            ResourceType = ResourceType.Caster,
            StrandCount = 4
        };
        var mill = new Resource
        {
            PlantId = plantId,
            ProcessStageId = Guid.NewGuid(),
            Code = "RM-1",
            Name = "RM-1",
            ResourceType = ResourceType.RollingMill
        };
        var resources = new[] { caster, mill };
        var capabilities = new[]
        {
            new ResourceCapability
            {
                ResourceId = caster.Id,
                GradeCode = "G1",
                OutputCrossSectionCode = "150X150",
                RouteCode = "SMS-RM",
                ThroughputMtPerHour = 60m
            },
            new ResourceCapability
            {
                ResourceId = mill.Id,
                GradeCode = "G1",
                InputCrossSectionCode = "150X150",
                OutputCrossSectionCode = "16MM",
                RouteCode = "SMS-RM",
                ThroughputMtPerHour = 50m
            },
            new ResourceCapability
            {
                ResourceId = mill.Id,
                GradeCode = "G1",
                InputCrossSectionCode = "150X150",
                OutputCrossSectionCode = "20MM",
                RouteCode = "SMS-RM",
                ThroughputMtPerHour = 50m
            }
        };
        var transitions = new[]
        {
            new TransitionRule
            {
                ResourceId = mill.Id,
                Dimension = TransitionDimension.CrossSection,
                FromCode = "16MM",
                ToCode = "20MM",
                IsAllowed = true,
                Penalty = 10,
                TransitionTime = TimeSpan.FromMinutes(15)
            }
        };
        var links = new[]
        {
            new PlantFlowLink
            {
                FromResourceId = caster.Id,
                ToResourceId = mill.Id,
                CouplingType = FlowCouplingType.HotTransfer,
                MinimumTransferTime = TimeSpan.FromMinutes(10),
                MaximumTransferTime = TimeSpan.FromMinutes(300),
                SupportsHotTransfer = true
            }
        };
        var horizonStart = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);

        var engine = new PlanningEngine(
            new CampaignPlanningService(),
            new ProductionStructurePlanningService(),
            new FiniteScheduleOptimizer());
        var result = engine.Run(new PlanningRunRequest(
            new[] { po16, po20 },
            Array.Empty<InventoryPosition>(),
            resources,
            capabilities,
            Array.Empty<ResourceCalendar>(),
            transitions,
            links,
            new CampaignPlanningPolicy(50m, 40m, 55m, 250m, 300m),
            new ProductionStructurePlanningPolicy(MaximumHeatsPerCastSequence: 8),
            horizonStart,
            horizonStart.AddHours(12),
            5));

        Assert.True(result.IsFeasible, string.Join("; ", result.Schedule.Issues.Select(i => i.Message)));
        Assert.Equal(2, result.ProductionStructure.RollingPlans.Count);

        var firstPlan = result.ProductionStructure.RollingPlans.Single(p => p.SequenceNumber == 1);
        var secondPlan = result.ProductionStructure.RollingPlans.Single(p => p.SequenceNumber == 2);
        var first = result.Schedule.Assignments.Single(a => a.SourceEntityId == firstPlan.Id);
        var second = result.Schedule.Assignments.Single(a => a.SourceEntityId == secondPlan.Id);

        Assert.True(second.StartUtc >= first.EndUtc.AddMinutes(15));
    }

    private static ProductionOrder NewPo(string number, string section, decimal quantity) => new()
    {
        ProductionOrderNumber = number,
        DemandSource = DemandSourceType.MakeToOrder,
        MaterialCode = $"FG-{section}",
        GradeCode = "G1",
        GradeSequenceClassCode = "SEQ-A",
        FinalCrossSectionCode = section,
        CasterSectionCode = "150X150",
        RouteCode = "SMS-RM",
        PlannedQuantityMt = quantity,
        RemainingQuantityMt = quantity,
        RequiredDate = new DateTime(2026, 8, 22),
        Priority = 1
    };
}
