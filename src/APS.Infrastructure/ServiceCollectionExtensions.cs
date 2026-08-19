using APS.Application;
using APS.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace APS.Infrastructure;

/// <summary>
/// Single source of truth for "what is the APS backend" so every host process (the API service,
/// the desktop app) registers the exact same services rather than independently reconstructing
/// slightly different wiring.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static ApsInfrastructureRegistration AddApsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var apsConnection = configuration.GetConnectionString("APS");
        var hasApsDatabase = !string.IsNullOrWhiteSpace(apsConnection);
        var demoModeEnabled = configuration.GetValue<bool>("APS:DemoModeEnabled");

        services.AddScoped<IMtsProductionOrderService, MtsProductionOrderService>();
        services.AddScoped<ICampaignPlanningService, CampaignPlanningService>();
        services.AddScoped<IProductionStructurePlanningService, ProductionStructurePlanningService>();
        services.AddScoped<IFiniteScheduleOptimizer, FiniteScheduleOptimizer>();
        services.AddScoped<IRecursiveMaterialRequirementEngine, RecursiveMaterialRequirementEngine>();
        services.AddScoped<PlanningEngine>();
        services.AddScoped<IPlanningEngine, BomAwarePlanningEngine>();
        services.AddScoped<IPlanReleaseBuilder, PlanReleaseBuilder>();

        if (hasApsDatabase)
        {
            services.AddDbContext<ApsDbContext>(options => options.UseSqlServer(apsConnection));
            services.AddScoped<ITraceabilityService, TraceabilityService>();
            services.AddScoped<IWorkOrderExecutionService, WorkOrderExecutionService>();
            services.AddScoped<IHeatExecutionService, HeatExecutionService>();
            services.AddScoped<IOperationExecutionService, OperationExecutionService>();
            services.AddHostedService<OperationCommitmentHostedService>();
            services.AddScoped<IInventorySnapshotProvider, SqlInventorySnapshotProvider>();
            services.AddScoped<IReplanningActualStateProvider, ReplanningActualStateProvider>();
            services.AddScoped<IPlanVersionRepository, PlanVersionRepository>();
            services.AddScoped<IPlanReleaseRepository, PlanReleaseRepository>();
            services.AddScoped<IPersistedPlanReleaseService, PersistedPlanReleaseService>();
            services.AddScoped<IPlanComparisonService, PlanComparisonService>();
            services.AddScoped<IPlannerWorkspaceQueryService, PlannerWorkspaceQueryService>();
            services.AddScoped<IPlanningMasterDataProvider, SqlPlanningMasterDataProvider>();
            services.AddScoped<IProductionDemandOrchestrationService, ProductionDemandOrchestrationService>();
            services.AddScoped<IPlanningLifecycleService, PlanningLifecycleService>();
        }
        else
        {
            services.AddScoped<IPlannerWorkspaceQueryService>(
                _ => new UnavailablePlannerWorkspaceQueryService(demoModeEnabled));
        }

        return new ApsInfrastructureRegistration(hasApsDatabase, demoModeEnabled);
    }
}

/// <summary>Reports what <see cref="ServiceCollectionExtensions.AddApsInfrastructure"/> actually wired up, so callers can gate endpoints/UI the same way without recomputing the same configuration checks.</summary>
public sealed record ApsInfrastructureRegistration(bool HasApsDatabase, bool DemoModeEnabled);
