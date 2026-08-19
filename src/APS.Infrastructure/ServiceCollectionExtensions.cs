using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
    /// <summary>
    /// APS is a self-contained desktop app: it must not require a separately-administered SQL
    /// Server instance just to run. SQLite is the active provider - a single local file, created
    /// and migrated automatically, no external dependency. SQL Server remains the intended provider
    /// for a future MES integration surface only; nothing in this method touches it.
    /// </summary>
    public static ApsInfrastructureRegistration AddApsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var configuredConnection = configuration.GetConnectionString("APS");
        string sqliteConnection;
        if (string.IsNullOrWhiteSpace(configuredConnection))
        {
            var paths = LocalApplicationPaths.ForCurrentUser();
            paths.EnsureDirectories();
            sqliteConnection = $"Data Source={Path.Combine(paths.DataDirectory, "aps.db")}";
        }
        else
        {
            sqliteConnection = configuredConnection;
        }
        var hasApsDatabase = true; // SQLite is always locally provisionable - no "unconfigured" state.
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
        }
        else
        {
            // Every DB-backed service above needs a registration here too - Blazor's DI throws at
            // component-construction time (before the page's own code ever runs) for any page that
            // injects a service with no registration at all, which crashes the whole app instead of
            // letting the page show its own "workspace unavailable" state.
            services.AddScoped<IPlannerWorkspaceQueryService>(
                _ => new UnavailablePlannerWorkspaceQueryService(demoModeEnabled));
            services.AddScoped<ITraceabilityService, UnavailableTraceabilityService>();
            services.AddScoped<IWorkOrderExecutionService, UnavailableWorkOrderExecutionService>();
            services.AddScoped<IHeatExecutionService, UnavailableHeatExecutionService>();
            services.AddScoped<IOperationExecutionService, UnavailableOperationExecutionService>();
            services.AddScoped<IInventorySnapshotProvider, UnavailableInventorySnapshotProvider>();
            services.AddScoped<IReplanningActualStateProvider, UnavailableReplanningActualStateProvider>();
            services.AddScoped<IPlanVersionRepository, UnavailablePlanVersionRepository>();
            services.AddScoped<IPlanReleaseRepository, UnavailablePlanReleaseRepository>();
            services.AddScoped<IPersistedPlanReleaseService, UnavailablePersistedPlanReleaseService>();
            services.AddScoped<IPlanComparisonService, UnavailablePlanComparisonService>();
            services.AddScoped<IPlanningMasterDataProvider, UnavailablePlanningMasterDataProvider>();
            services.AddScoped<IProductionDemandOrchestrationService, UnavailableProductionDemandOrchestrationService>();
            services.AddScoped<IPlanningLifecycleService, UnavailablePlanningLifecycleService>();
        }

        return new ApsInfrastructureRegistration(hasApsDatabase, demoModeEnabled);
    }

    /// <summary>
    /// Applies any pending EF Core migrations to the SQLite database, creating the file and schema
    /// on first run. A self-contained desktop app has no separate deployment step to run migrations
    /// out of band, so this runs once at startup instead - call it right after the host is built,
    /// before showing any UI or accepting requests.
    /// </summary>
    public static async Task MigrateApsDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApsDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}

/// <summary>Reports what <see cref="ServiceCollectionExtensions.AddApsInfrastructure"/> actually wired up, so callers can gate endpoints/UI the same way without recomputing the same configuration checks.</summary>
public sealed record ApsInfrastructureRegistration(bool HasApsDatabase, bool DemoModeEnabled);
