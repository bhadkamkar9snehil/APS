using System.Text.Json.Serialization;
using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using APS.Planning;
using APS.Service.Components;
using APS.UI.State;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<PlannerWorkspaceState>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddScoped<IMtsProductionOrderService, MtsProductionOrderService>();
builder.Services.AddScoped<ICampaignPlanningService, CampaignPlanningService>();
builder.Services.AddScoped<IProductionStructurePlanningService, ProductionStructurePlanningService>();
builder.Services.AddScoped<IFiniteScheduleOptimizer, FiniteScheduleOptimizer>();
builder.Services.AddScoped<IPlanningEngine, PlanningEngine>();
builder.Services.AddScoped<IPlanReleaseBuilder, PlanReleaseBuilder>();

var apsConnection = builder.Configuration.GetConnectionString("APS");
var hasApsDatabase = !string.IsNullOrWhiteSpace(apsConnection);
if (hasApsDatabase)
{
    builder.Services.AddDbContext<ApsDbContext>(options => options.UseSqlServer(apsConnection));
    builder.Services.AddScoped<ITraceabilityService, TraceabilityService>();
    builder.Services.AddScoped<IWorkOrderExecutionService, WorkOrderExecutionService>();
    builder.Services.AddScoped<IHeatExecutionService, HeatExecutionService>();
    builder.Services.AddScoped<IInventorySnapshotProvider, SqlInventorySnapshotProvider>();
    builder.Services.AddScoped<IReplanningActualStateProvider, ReplanningActualStateProvider>();
    builder.Services.AddScoped<IPlanVersionRepository, PlanVersionRepository>();
    builder.Services.AddScoped<IPlanReleaseRepository, PlanReleaseRepository>();
    builder.Services.AddScoped<IPlanComparisonService, PlanComparisonService>();
    builder.Services.AddScoped<IPlannerWorkspaceQueryService, PlannerWorkspaceQueryService>();
}
else
{
    builder.Services.AddScoped<IPlannerWorkspaceQueryService, UnavailablePlannerWorkspaceQueryService>();
}

var app = builder.Build();

app.UseHttpsRedirection();
app.MapStaticAssets();
app.UseAntiforgery();

app.MapGet("/api/health", () => Results.Ok(new
{
    service = "APS.Service",
    status = "ok",
    databaseConfigured = hasApsDatabase,
    utc = DateTime.UtcNow
}));

if (hasApsDatabase)
{
    app.MapGet("/api/inventory/snapshot",
        async (IInventorySnapshotProvider inventory, CancellationToken cancellationToken) =>
            Results.Ok(await inventory.GetInventoryAsync(cancellationToken)));

    app.MapGet("/api/ui/planner/current",
        async (IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var current = await planner.GetCurrentPlanAsync(cancellationToken);
            return current is null ? Results.NotFound() : Results.Ok(current);
        });

    app.MapGet("/api/ui/planner/versions",
        async (int? take, IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
            Results.Ok(await planner.GetRecentPlanVersionsAsync(take ?? 20, cancellationToken)));

    app.MapGet("/api/ui/planner/versions/{planVersionId:guid}/context",
        async (Guid planVersionId, IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var context = await planner.GetPlanContextAsync(planVersionId, cancellationToken);
            return context is null ? Results.NotFound() : Results.Ok(context);
        });

    app.MapGet("/api/ui/planner/control-tower",
        async (IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var view = await planner.GetControlTowerAsync(null, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

    app.MapGet("/api/ui/planner/control-tower/{planVersionId:guid}",
        async (Guid planVersionId, IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var view = await planner.GetControlTowerAsync(planVersionId, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

    app.MapPost("/api/planning/run",
        async (PlanningRunRequest request, IPlanningEngine planningEngine, IPlanVersionRepository plans, CancellationToken cancellationToken) =>
        {
            var result = planningEngine.Run(request);
            var persisted = await plans.SaveAsync(new PersistPlanningRunRequest(
                request,
                result,
                PlanTriggerType.Manual,
                DateTime.UtcNow,
                "Manual planning run"), cancellationToken);
            return result.IsFeasible
                ? Results.Ok(new { plan = result, version = persisted })
                : Results.UnprocessableEntity(new { plan = result, version = persisted });
        });

    app.MapPost("/api/planning/replan/{baselinePlanVersionId:guid}",
        async (Guid baselinePlanVersionId,
            ReplanApiRequest request,
            IPlanningEngine planningEngine,
            IPlanVersionRepository plans,
            IReplanningActualStateProvider actualStateProvider,
            CancellationToken cancellationToken) =>
        {
            var baseline = await plans.GetAsync(baselinePlanVersionId, cancellationToken);
            if (baseline is null) return Results.NotFound(new { message = "Baseline plan version was not found." });

            var referenceTime = request.ReferenceTimeUtc ?? DateTime.UtcNow;
            var actualState = await actualStateProvider.GetAsync(
                baselinePlanVersionId,
                referenceTime,
                baseline.Operations,
                cancellationToken);
            var planningRequest = request.Planning with
            {
                Inventory = request.RefreshInventoryFromProvider ? actualState.Inventory : request.Planning.Inventory,
                ReplanContext = new PlanningReplanContext(
                    baselinePlanVersionId,
                    referenceTime,
                    request.TimeFencePolicy,
                    actualState.BaselineOperations)
            };

            var result = planningEngine.Run(planningRequest);
            var persisted = await plans.SaveAsync(new PersistPlanningRunRequest(
                planningRequest,
                result,
                request.Trigger,
                referenceTime,
                request.Reason ?? "Replanning from current manufacturing and inventory state"), cancellationToken);

            var response = new
            {
                plan = result,
                version = persisted,
                executionState = new
                {
                    actualState.CompletedPlanningKeys,
                    actualState.RunningPlanningKeys,
                    InventoryPositions = actualState.Inventory.Count
                }
            };
            return result.IsFeasible ? Results.Ok(response) : Results.UnprocessableEntity(response);
        });

    app.MapGet("/api/planning/versions/{planVersionId:guid}",
        async (Guid planVersionId, IPlanVersionRepository plans, CancellationToken cancellationToken) =>
        {
            var version = await plans.GetAsync(planVersionId, cancellationToken);
            return version is null ? Results.NotFound() : Results.Ok(version);
        });

    app.MapGet("/api/planning/versions/{newPlanVersionId:guid}/compare/{baselinePlanVersionId:guid}",
        async (Guid newPlanVersionId, Guid baselinePlanVersionId, IPlanComparisonService comparison, CancellationToken cancellationToken) =>
            Results.Ok(await comparison.CompareAsync(baselinePlanVersionId, newPlanVersionId, cancellationToken)));

    app.MapPost("/api/planning/release",
        async (PlanReleaseBuildRequest request, IPlanReleaseBuilder releaseBuilder, IPlanReleaseRepository releases, CancellationToken cancellationToken) =>
        {
            if (!request.Schedule.IsFeasible)
            {
                return Results.UnprocessableEntity(new { message = "Cannot release Work Orders from an infeasible schedule." });
            }

            var release = releaseBuilder.Build(request);
            var persisted = await releases.PersistAsync(release, cancellationToken);
            return Results.Ok(persisted);
        });
}
else
{
    app.MapPost("/api/planning/run",
        (PlanningRunRequest request, IPlanningEngine planningEngine) =>
        {
            var result = planningEngine.Run(request);
            return result.IsFeasible ? Results.Ok(result) : Results.UnprocessableEntity(result);
        });
}

app.MapPost("/api/planning/mts/production-order",
    (MtsProductionOrderRequest request, IMtsProductionOrderService service) =>
        Results.Ok(service.Propose(request.Policy, request.Inventory, request.AlreadyFirmedSupplyMt)));

app.MapPost("/api/planning/campaigns/form",
    (CampaignPlanningRequest request, ICampaignPlanningService service) =>
        Results.Ok(service.FormCampaigns(request)));

app.MapPost("/api/planning/structure/build",
    (ProductionStructurePlanningRequest request, IProductionStructurePlanningService service) =>
        Results.Ok(service.Build(request)));

app.MapPost("/api/planning/schedule/solve",
    (FiniteScheduleRequest request, IFiniteScheduleOptimizer optimizer) =>
    {
        var result = optimizer.Solve(request);
        return result.IsFeasible ? Results.Ok(result) : Results.UnprocessableEntity(result);
    });

app.MapPost("/api/planning/release/build",
    (PlanReleaseBuildRequest request, IPlanReleaseBuilder releaseBuilder) =>
    {
        if (!request.Schedule.IsFeasible)
        {
            return Results.UnprocessableEntity(new { message = "Cannot build Work Orders from an infeasible schedule." });
        }

        return Results.Ok(releaseBuilder.Build(request));
    });

if (hasApsDatabase)
{
    app.MapPost("/api/execution/work-orders/{workOrderId:guid}",
        async (Guid workOrderId, ManualWorkOrderExecutionRequest request, IWorkOrderExecutionService execution, CancellationToken cancellationToken) =>
        {
            var snapshot = await execution.ApplyAsync(new WorkOrderExecutionUpdate(
                workOrderId,
                null,
                request.Status,
                request.ActualStart,
                request.ActualEnd,
                request.ActualQuantityMt,
                request.ChangedOnUtc ?? DateTime.UtcNow,
                ExecutionUpdateSource.Manual,
                null,
                request.Comment,
                request.IsCorrection), cancellationToken);
            return Results.Ok(snapshot);
        });

    app.MapPost("/api/execution/heats",
        async (ManualHeatExecutionRequest request, IHeatExecutionService execution, CancellationToken cancellationToken) =>
        {
            var snapshot = await execution.ApplyAsync(new HeatExecutionUpdate(
                request.PlanVersionId,
                request.PlanningKey,
                request.Status,
                request.ChangedOnUtc ?? DateTime.UtcNow,
                ExecutionUpdateSource.Manual,
                null,
                request.ExternalHeatNumber,
                request.ExternalCastNumber,
                request.CasterResourceId,
                request.ActualStartUtc,
                request.ActualEndUtc,
                request.ActualQuantityMt,
                request.MaterialOutputs,
                request.Comment,
                request.IsCorrection), cancellationToken);
            return Results.Ok(snapshot);
        });

    app.MapPost("/api/integration/xstudio/execution-events",
        async (WorkOrderExecutionUpdate update, IWorkOrderExecutionService execution, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(update.ExternalExecutionId))
            {
                return Results.BadRequest(new { message = "ExternalExecutionId is required for MES execution events." });
            }

            var snapshot = await execution.ApplyAsync(update with
            {
                WorkOrderId = null,
                Source = ExecutionUpdateSource.MesApi
            }, cancellationToken);
            return Results.Ok(snapshot);
        });

    app.MapPost("/api/integration/xstudio/heat-events",
        async (HeatExecutionUpdate update, IHeatExecutionService execution, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(update.ExternalEventId))
            {
                return Results.BadRequest(new { message = "ExternalEventId is required for MES heat events." });
            }

            return Results.Ok(await execution.ApplyAsync(update with
            {
                Source = ExecutionUpdateSource.MesApi
            }, cancellationToken));
        });

    app.MapGet("/api/traceability/work-orders/{workOrderId:guid}",
        async (Guid workOrderId, ITraceabilityService traceability, CancellationToken cancellationToken) =>
        {
            var trace = await traceability.GetWorkOrderTraceAsync(workOrderId, cancellationToken);
            return trace is null ? Results.NotFound() : Results.Ok(trace);
        });

    app.MapGet("/api/traceability/material-lots/{materialLotId:guid}",
        async (Guid materialLotId, ITraceabilityService traceability, CancellationToken cancellationToken) =>
        {
            var trace = await traceability.GetMaterialLotTraceAsync(materialLotId, cancellationToken);
            return trace is null ? Results.NotFound() : Results.Ok(trace);
        });
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(APS.UI.Components.Layout.MainLayout).Assembly)
    .WithStaticAssets();

app.Run();

public sealed record MtsProductionOrderRequest(
    StockPolicy Policy,
    InventoryPosition Inventory,
    decimal AlreadyFirmedSupplyMt = 0m);

public sealed record ReplanApiRequest(
    PlanningRunRequest Planning,
    PlanningTimeFencePolicy TimeFencePolicy,
    DateTime? ReferenceTimeUtc = null,
    PlanTriggerType Trigger = PlanTriggerType.ExecutionFeedback,
    string? Reason = null,
    bool RefreshInventoryFromProvider = true);

public sealed record ManualHeatExecutionRequest(
    Guid PlanVersionId,
    string PlanningKey,
    HeatExecutionStatus Status,
    string? ExternalHeatNumber = null,
    string? ExternalCastNumber = null,
    Guid? CasterResourceId = null,
    DateTime? ActualStartUtc = null,
    DateTime? ActualEndUtc = null,
    decimal? ActualQuantityMt = null,
    IReadOnlyCollection<StrandMaterialActualInput>? MaterialOutputs = null,
    DateTime? ChangedOnUtc = null,
    string? Comment = null,
    bool IsCorrection = false);

public sealed record ManualWorkOrderExecutionRequest(
    WorkOrderStatus Status,
    DateTime? ActualStart = null,
    DateTime? ActualEnd = null,
    decimal? ActualQuantityMt = null,
    DateTime? ChangedOnUtc = null,
    string? Comment = null,
    bool IsCorrection = false);