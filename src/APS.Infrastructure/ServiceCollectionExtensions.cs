using APS.Application;
using APS.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace APS.Infrastructure;

/// <summary>
/// Single source of truth for APS backend registration across the API service and desktop app.
/// SQLite is always locally provisionable; SQL Server remains a future MES integration surface.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static bool AddApsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var sqliteConnection = configuration.GetConnectionString("APS");
        if (string.IsNullOrWhiteSpace(sqliteConnection))
        {
            var paths = LocalApplicationPaths.ForCurrentUser();
            paths.EnsureDirectories();
            sqliteConnection = $"Data Source={Path.Combine(paths.DataDirectory, "aps.db")}";
        }

        services.AddScoped<IMtsProductionOrderService, MtsProductionOrderService>();
        services.AddScoped<ICampaignPlanningService, CampaignPlanningService>();
        services.AddScoped<IProductionStructurePlanningService, ProductionStructurePlanningService>();
        services.AddScoped<IFiniteScheduleOptimizer, FiniteScheduleOptimizer>();
        services.AddScoped<IRecursiveMaterialRequirementEngine, RecursiveMaterialRequirementEngine>();
        services.AddScoped<PlanningEngine>();
        services.AddScoped<IPlanningEngine, BomAwarePlanningEngine>();
        services.AddScoped<IPlanReleaseBuilder, PlanReleaseBuilder>();

        services.AddDbContext<ApsDbContext>(options => options.UseSqlite(sqliteConnection));
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
        services.AddScoped<IPlanningWorkbenchCommandService, PlanningWorkbenchCommandService>();
        services.AddScoped<IMasterDataAdminService, MasterDataAdminService>();

        return bool.TryParse(configuration["APS:DemoModeEnabled"], out var demoModeEnabled) && demoModeEnabled;
    }

    /// <summary>
    /// Applies pending EF Core migrations to the self-contained SQLite database before hosted services start.
    /// </summary>
    public static async Task MigrateApsDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApsDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
