using APS.Application;
using APS.Domain;

namespace APS.Infrastructure;

internal static class PlanStructureSnapshotProjector
{
    public static void AddToContext(
        ApsDbContext db,
        PlanningRunRequest request,
        PlanningRunResult result)
    {
        AddProductionOrders(db, request, result);
        AddCampaignStructure(db, result);
        AddPhysicalStructure(db, result);
        AddPackaging(db, result);
    }

    private static void AddProductionOrders(
        ApsDbContext db,
        PlanningRunRequest request,
        PlanningRunResult result)
    {
        var external = result.CampaignPlan.ExternalIntermediateAllocatedMt
                       ?? new Dictionary<Guid, decimal>();
        var fgByPo = result.CampaignPlan.InventoryAllocations
            .Where(x => x.Use == PlanningInventoryUse.FinishedGoodsFulfilment)
            .GroupBy(x => x.ProductionOrderId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.QuantityMt));
        var requirements = (result.RequirementSnapshots ?? Array.Empty<PlanOrderRequirementSnapshot>())
            .GroupBy(x => x.ProductionOrderId)
            .ToDictionary(x => x.Key, x => x.First());

        foreach (var po in request.ProductionOrders.DistinctBy(x => x.Id))
        {
            result.CampaignPlan.RollingRequirementsMt.TryGetValue(po.Id, out var rollingRequirement);
            result.CampaignPlan.FreshSteelRequirementsMt.TryGetValue(po.Id, out var freshSteel);
            result.CampaignPlan.IntermediateInventoryAllocatedMt.TryGetValue(po.Id, out var existingIntermediate);
            external.TryGetValue(po.Id, out var externalIntermediate);
            fgByPo.TryGetValue(po.Id, out var finishedGoods);
            requirements.TryGetValue(po.Id, out var requirement);

            db.PlanProductionOrderSnapshots.Add(new PlanProductionOrderSnapshot
            {
                PlanVersionId = result.PlanVersionId,
                ProductionOrderId = po.Id,
                ProductionOrderNumber = po.ProductionOrderNumber,
                DemandSource = po.DemandSource,
                SalesOrderId = po.SalesOrderId,
                SalesOrderNumber = po.SalesOrder?.SalesOrderNumber,
                SalesOrderItemNumber = po.SalesOrder?.ItemNumber,
                CustomerCode = po.Requirement?.CustomerCode ?? po.SalesOrder?.CustomerCode,
                CustomerGroupCode = po.Requirement?.CustomerGroupCode ?? po.SalesOrder?.CustomerGroupCode,
                MaterialCode = po.MaterialCode,
                GradeCode = po.GradeCode,
                GradeFamilyCode = po.GradeFamilyCode,
                GradeSequenceClassCode = po.GradeSequenceClassCode,
                FinalCrossSectionCode = po.FinalCrossSectionCode,
                CasterSectionCode = po.CasterSectionCode,
                RouteCode = po.RouteCode,
                ProductFamilyCode = po.ProductFamilyCode,
                PlannedQuantityMt = po.PlannedQuantityMt,
                RemainingQuantityMt = po.RemainingQuantityMt,
                RequiredDate = po.RequiredDate,
                Priority = po.Priority,
                Status = po.Status,
                TargetStockMt = po.TargetStockMt,
                ProjectedAvailableStockMt = po.ProjectedAvailableStockMt,
                StockPolicyCode = po.StockPolicyCode,
                FinishedGoodsAllocatedMt = finishedGoods,
                RollingRequirementMt = rollingRequirement,
                ExistingIntermediateAllocatedMt = existingIntermediate,
                ExternalIntermediateAllocatedMt = externalIntermediate,
                FreshSteelRequirementMt = freshSteel,
                RequirementSnapshotId = requirement?.Id,
                RequirementSnapshot = requirement
            });
        }
    }

    private static void AddCampaignStructure(ApsDbContext db, PlanningRunResult result)
    {
        foreach (var campaign in result.CampaignPlan.Campaigns)
        {
            db.PlanCampaignSnapshots.Add(new PlanCampaignSnapshot
            {
                PlanVersionId = result.PlanVersionId,
                CampaignId = campaign.Id,
                CampaignNumber = campaign.CampaignNumber,
                GradeSequenceClassCode = campaign.GradeSequenceClassCode,
                CasterSectionCode = campaign.CasterSectionCode,
                RouteCode = campaign.RouteCode,
                PlannedQuantityMt = campaign.PlannedQuantityMt,
                FreshSteelRequirementMt = campaign.FreshSteelRequirementMt,
                ExistingIntermediateInventoryMt = campaign.ExistingIntermediateInventoryMt,
                RequiredDate = campaign.RequiredDate,
                Status = campaign.Status
            });

            foreach (var allocation in campaign.Allocations)
            {
                db.PlanCampaignAllocationSnapshots.Add(new PlanCampaignAllocationSnapshot
                {
                    PlanVersionId = result.PlanVersionId,
                    CampaignId = campaign.Id,
                    ProductionOrderId = allocation.ProductionOrderId,
                    PlannedQuantityMt = allocation.PlannedQuantityMt,
                    ExistingIntermediateInventoryMt = allocation.ExistingIntermediateInventoryMt,
                    FreshSteelQuantityMt = allocation.FreshSteelQuantityMt
                });
            }

            foreach (var grade in campaign.GradeSequence)
            {
                db.PlanCampaignGradeSequenceSnapshots.Add(new PlanCampaignGradeSequenceSnapshot
                {
                    PlanVersionId = result.PlanVersionId,
                    CampaignId = campaign.Id,
                    SequenceNumber = grade.SequenceNumber,
                    GradeCode = grade.GradeCode,
                    PlannedQuantityMt = grade.PlannedQuantityMt
                });
            }

            foreach (var heat in campaign.Heats)
            {
                db.PlanHeatSnapshots.Add(new PlanHeatSnapshot
                {
                    PlanVersionId = result.PlanVersionId,
                    CampaignHeatId = heat.Id,
                    CampaignId = campaign.Id,
                    CampaignGradeSequenceId = heat.CampaignGradeSequenceId,
                    SequenceNumber = heat.SequenceNumber,
                    GradeCode = heat.GradeCode,
                    PlannedQuantityMt = heat.PlannedQuantityMt,
                    MinimumFeasibleQuantityMt = heat.MinimumFeasibleQuantityMt,
                    TargetQuantityMt = heat.TargetQuantityMt,
                    MaximumFeasibleQuantityMt = heat.MaximumFeasibleQuantityMt,
                    PreferredSteelmakingResourceId = heat.PreferredSteelmakingResourceId,
                    PreferredCasterResourceId = heat.PreferredCasterResourceId
                });
            }
        }

        foreach (var allocation in result.CampaignPlan.HeatAllocations ?? Array.Empty<CampaignHeatAllocation>())
        {
            db.PlanHeatAllocationSnapshots.Add(new PlanHeatAllocationSnapshot
            {
                PlanVersionId = result.PlanVersionId,
                CampaignHeatId = allocation.CampaignHeatId,
                ProductionOrderId = allocation.ProductionOrderId,
                PlannedOutputQuantityMt = allocation.PlannedOutputQuantityMt,
                PlannedInputQuantityMt = allocation.PlannedInputQuantityMt
            });
        }
    }

    private static void AddPhysicalStructure(ApsDbContext db, PlanningRunResult result)
    {
        foreach (var sequence in result.ProductionStructure.CastSequences)
        {
            db.PlanCastSequenceSnapshots.Add(new PlanCastSequenceSnapshot
            {
                PlanVersionId = result.PlanVersionId,
                CastSequenceId = sequence.Id,
                CampaignId = sequence.CampaignId,
                CasterResourceId = sequence.CasterResourceId,
                SequenceNumber = sequence.SequenceNumber,
                CasterSectionCode = sequence.CasterSectionCode,
                RouteCode = sequence.RouteCode,
                TundishNumber = sequence.TundishNumber,
                PlannedStart = sequence.PlannedStart,
                PlannedEnd = sequence.PlannedEnd
            });

            foreach (var item in sequence.Heats)
            {
                db.PlanCastSequenceHeatSnapshots.Add(new PlanCastSequenceHeatSnapshot
                {
                    PlanVersionId = result.PlanVersionId,
                    CastSequenceId = sequence.Id,
                    CampaignHeatId = item.CampaignHeatId,
                    Position = item.Position
                });
            }
        }

        foreach (var rolling in result.ProductionStructure.RollingPlans)
        {
            db.PlanRollingPlanSnapshots.Add(new PlanRollingPlanSnapshot
            {
                PlanVersionId = result.PlanVersionId,
                RollingPlanId = rolling.Id,
                CampaignId = rolling.CampaignId,
                ProductionOrderId = rolling.ProductionOrderId,
                RollingMillResourceId = rolling.RollingMillResourceId,
                SequenceNumber = rolling.SequenceNumber,
                GradeCode = rolling.GradeCode,
                InputCrossSectionCode = rolling.InputCrossSectionCode,
                OutputCrossSectionCode = rolling.OutputCrossSectionCode,
                RouteCode = rolling.RouteCode,
                PlannedQuantityMt = rolling.PlannedQuantityMt,
                ExistingIntermediateInventoryMt = rolling.ExistingIntermediateInventoryMt,
                FreshSteelQuantityMt = rolling.FreshSteelQuantityMt
            });

            foreach (var allocation in rolling.Allocations)
            {
                db.PlanRollingPlanAllocationSnapshots.Add(new PlanRollingPlanAllocationSnapshot
                {
                    PlanVersionId = result.PlanVersionId,
                    RollingPlanId = rolling.Id,
                    CampaignId = allocation.CampaignId,
                    ProductionOrderId = allocation.ProductionOrderId,
                    PlannedQuantityMt = allocation.PlannedQuantityMt,
                    ExistingIntermediateInventoryMt = allocation.ExistingIntermediateInventoryMt,
                    FreshSteelQuantityMt = allocation.FreshSteelQuantityMt
                });
            }
        }
    }

    private static void AddPackaging(ApsDbContext db, PlanningRunResult result)
    {
        foreach (var unit in result.PlannedPackagingUnits ?? Array.Empty<PlannedPackagingUnit>())
        {
            db.PlanPackagingUnitSnapshots.Add(new PlanPackagingUnitSnapshot
            {
                PlanVersionId = result.PlanVersionId,
                PlannedPackagingUnitId = unit.Id,
                ProductionOrderId = unit.ProductionOrderId,
                WorkOrderId = unit.WorkOrderId,
                PackagingUnitType = unit.PackagingUnitType,
                SequenceNumber = unit.SequenceNumber,
                PlannedWeightMt = unit.PlannedWeightMt,
                PlannedPieceCount = unit.PlannedPieceCount,
                CutLengthM = unit.CutLengthM,
                PackagingCode = unit.PackagingCode,
                PlannedIdentifier = unit.PlannedIdentifier
            });
        }
    }
}
