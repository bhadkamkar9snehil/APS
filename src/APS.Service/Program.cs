using System.Text.Json.Serialization;
using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using APS.Planning;
using APS.Service.Components;
using APS.UI.State;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddProblemDetails();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
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
builder.Services.AddScoped<IRecursiveMaterialRequirementEngine, RecursiveMaterialRequirementEngine>();
builder.Services.AddScoped<PlanningEngine>();
builder.Services.AddScoped<IPlanningEngine, BomAwarePlanningEngine>();
builder.Services.AddScoped<IPlanReleaseBuilder, PlanReleaseBuilder>();

var apsConnection = builder.Configuration.GetConnectionString("APS");
var hasApsDatabase = !string.IsNullOrWhiteSpace(apsConnection);
var demoModeEnabled = builder.Configuration.GetValue<bool>("APS:DemoModeEnabled");

if (hasApsDatabase)
{
    builder.Services.AddDbContext<ApsDbContext>(options => options.UseSqlServer(apsConnection));
    builder.Services.AddScoped<ITraceabilityService, TraceabilityService>();
    builder.Services.AddScoped<IWorkOrderExecutionService, WorkOrderExecutionService>();
    builder.Services.AddScoped<IHeatExecutionService, HeatExecutionService>();
    builder.Services.AddScoped<IOperationExecutionService, OperationExecutionService>();
    builder.Services.AddHostedService<OperationCommitmentHostedService>();
    builder.Services.AddScoped<IInventorySnapshotProvider, SqlInventorySnapshotProvider>();
    builder.Services.AddScoped<IReplanningActualStateProvider, ReplanningActualStateProvider>();
    builder.Services.AddScoped<IPlanVersionRepository, PlanVersionRepository>();
    builder.Services.AddScoped<IPlanReleaseRepository, PlanReleaseRepository>();
    builder.Services.AddScoped<IPersistedPlanReleaseService, PersistedPlanReleaseService>();
    builder.Services.AddScoped<IPlanComparisonService, PlanComparisonService>();
    builder.Services.AddScoped<IPlannerWorkspaceQueryService, PlannerWorkspaceQueryService>();
    builder.Services.AddScoped<IPlanningMasterDataProvider, SqlPlanningMasterDataProvider>();
    builder.Services.AddScoped<IProductionDemandOrchestrationService, ProductionDemandOrchestrationService>();
    builder.Services.AddScoped<IPlanningLifecycleService, PlanningLifecycleService>();
}
else
{
    builder.Services.AddScoped<IPlannerWorkspaceQueryService>(
        _ => new UnavailablePlannerWorkspaceQueryService(demoModeEnabled));
}

var app = builder.Build();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.MapStaticAssets();
app.UseAntiforgery();

app.MapGet("/api/health", () => Results.Ok(new
{
    service = "APS.Service",
    status = "ok",
    databaseConfigured = hasApsDatabase,
    productionPlanningAvailable = hasApsDatabase,
    demoModeEnabled,
    utc = DateTime.UtcNow
}));

if (hasApsDatabase)
{
    app.MapGet("/api/inventory/snapshot",
        async (IInventorySnapshotProvider inventory, CancellationToken cancellationToken) =>
            Results.Ok(await inventory.GetInventoryAsync(cancellationToken)));

    app.MapGet("/api/planning/master-data",
        async (IPlanningMasterDataProvider masters, CancellationToken cancellationToken) =>
            Results.Ok(await masters.GetAsync(cancellationToken)));

    app.MapPost("/api/demand/sales-orders/reconcile",
        async (IReadOnlyCollection<SalesOrderDemandInput> salesOrders,
            IProductionDemandOrchestrationService demand,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await demand.ReconcileSalesOrdersAsync(salesOrders, cancellationToken));
            }
            catch (ValidationException ex)
            {
                return Results.ValidationProblem(ex.Errors
                    .GroupBy(x => x.PropertyName)
                    .ToDictionary(x => x.Key, x => x.Select(y => y.ErrorMessage).ToArray()));
            }
        });

    app.MapGet("/api/demand/mto",
        async (IProductionDemandOrchestrationService demand, CancellationToken cancellationToken) =>
            Results.Ok(await demand.GetCurrentMtoDemandAsync(cancellationToken)));

    var plannerApi = app.MapGroup("/api/ui/planner");

    plannerApi.MapGet("/current",
        async (IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var current = await planner.GetCurrentPlanAsync(cancellationToken);
            return current is null ? Results.NotFound() : Results.Ok(current);
        });

    plannerApi.MapGet("/versions",
        async (int? take, IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
            Results.Ok(await planner.GetRecentPlanVersionsAsync(take ?? 20, cancellationToken)));

    plannerApi.MapGet("/versions/{planVersionId:guid}/context",
        async (Guid planVersionId, IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var context = await planner.GetPlanContextAsync(planVersionId, cancellationToken);
            return context is null ? Results.NotFound() : Results.Ok(context);
        });

    plannerApi.MapGet("/control-tower",
        async (IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var view = await planner.GetControlTowerAsync(null, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

    plannerApi.MapGet("/control-tower/{planVersionId:guid}",
        async (Guid planVersionId, IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var view = await planner.GetControlTowerAsync(planVersionId, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

    plannerApi.MapGet("/demand-supply",
        async (IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var view = await planner.GetDemandSupplyAsync(null, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

    plannerApi.MapGet("/demand-supply/{planVersionId:guid}",
        async (Guid planVersionId, IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var view = await planner.GetDemandSupplyAsync(planVersionId, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

    plannerApi.MapGet("/campaigns",
        async (IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var view = await planner.GetCampaignStudioAsync(null, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

    plannerApi.MapGet("/campaigns/{planVersionId:guid}",
        async (Guid planVersionId, IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var view = await planner.GetCampaignStudioAsync(planVersionId, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

    plannerApi.MapGet("/steelmaking-casting",
        async (IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var view = await planner.GetSteelmakingCastingAsync(null, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

    plannerApi.MapGet("/steelmaking-casting/{planVersionId:guid}",
        async (Guid planVersionId, IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var view = await planner.GetSteelmakingCastingAsync(planVersionId, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

    plannerApi.MapGet("/schedule",
        async (IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var view = await planner.GetFiniteScheduleAsync(null, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

    plannerApi.MapGet("/schedule/{planVersionId:guid}",
        async (Guid planVersionId, IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var view = await planner.GetFiniteScheduleAsync(planVersionId, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

    plannerApi.MapGet("/work-orders",
        async (IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var view = await planner.GetWorkOrdersAsync(null, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

    plannerApi.MapGet("/work-orders/{planVersionId:guid}",
        async (Guid planVersionId, IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var view = await planner.GetWorkOrdersAsync(planVersionId, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

    plannerApi.MapGet("/compare/{baselinePlanVersionId:guid}/{newPlanVersionId:guid}",
        async (Guid baselinePlanVersionId, Guid newPlanVersionId, IPlannerWorkspaceQueryService planner, CancellationToken cancellationToken) =>
        {
            var view = await planner.GetPlanComparisonAsync(baselinePlanVersionId, newPlanVersionId, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        });

    async Task<IResult> CalculateProductionAsync(
        PlanningCalculationRequest request,
        IPlanningLifecycleService lifecycle,
        CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await lifecycle.CalculateAsync(request, cancellationToken);
            return outcome.Plan.IsFeasible ? Results.Ok(outcome) : Results.UnprocessableEntity(outcome);
        }
        catch (ValidationException ex)
        {
            return Results.ValidationProblem(ex.Errors
                .GroupBy(x => x.PropertyName)
                .ToDictionary(x => x.Key, x => x.Select(y => y.ErrorMessage).ToArray()));
        }
        catch (PlanningConfigurationException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "APS production planning configuration is incomplete",
                detail: string.Join(" ", ex.Issues));
        }
    }

    app.MapPost("/api/planning/calculate", CalculateProductionAsync);

    // Compatibility alias only. It uses the exact same canonical lifecycle as /calculate.
    app.MapPost("/api/planning/run", CalculateProductionAsync);

    app.MapPost("/api/planning/replan/{baselinePlanVersionId:guid}",
        async (Guid baselinePlanVersionId,
            PlanningRecalculationRequest request,
            IPlanningLifecycleService lifecycle,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var outcome = await lifecycle.ReplanAsync(baselinePlanVersionId, request, cancellationToken);
                return outcome.Plan.IsFeasible ? Results.Ok(outcome) : Results.UnprocessableEntity(outcome);
            }
            catch (ValidationException ex)
            {
                return Results.ValidationProblem(ex.Errors
                    .GroupBy(x => x.PropertyName)
                    .ToDictionary(x => x.Key, x => x.Select(y => y.ErrorMessage).ToArray()));
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
            catch (PlanningConfigurationException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "APS production planning configuration is incomplete",
                    detail: string.Join(" ", ex.Issues));
            }
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

    async Task<IResult> ReleasePersistedPlanAsync(
        Guid planVersionId,
        IPersistedPlanReleaseService releaseService,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await releaseService.ReleaseAsync(planVersionId, cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.UnprocessableEntity(new { message = ex.Message });
        }
    }

    app.MapPost("/api/planning/versions/{planVersionId:guid}/release", ReleasePersistedPlanAsync);

    // Compatibility alias; still identity-only and backed by persisted Plan Version truth.
    app.MapPost("/api/planning/release/{planVersionId:guid}", ReleasePersistedPlanAsync);
}
else
{
    static IResult PlanningUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "APS production planning is unavailable",
        detail: "The APS SQL database is not configured. Production calculation, Plan Version persistence, release, replanning and demand orchestration require the canonical persisted backend.");

    app.MapPost("/api/demand/sales-orders/reconcile", () => PlanningUnavailable());
    app.MapGet("/api/demand/mto", () => PlanningUnavailable());
    app.MapPost("/api/planning/calculate", () => PlanningUnavailable());
    app.MapPost("/api/planning/run", () => PlanningUnavailable());
    app.MapPost("/api/planning/replan/{baselinePlanVersionId:guid}", (Guid baselinePlanVersionId) => PlanningUnavailable());
    app.MapPost("/api/planning/versions/{planVersionId:guid}/release", (Guid planVersionId) => PlanningUnavailable());
    app.MapPost("/api/planning/release/{planVersionId:guid}", (Guid planVersionId) => PlanningUnavailable());
}

if (demoModeEnabled)
{
    var demoPlanning = app.MapGroup("/api/demo/planning");

    demoPlanning.MapPost("/run",
        (PlanningRunRequest request, IPlanningEngine planningEngine) =>
        {
            var result = planningEngine.Run(request);
            return result.IsFeasible ? Results.Ok(result) : Results.UnprocessableEntity(result);
        });

    demoPlanning.MapPost("/mts/production-order",
        (MtsProductionOrderRequest request, IMtsProductionOrderService service) =>
            Results.Ok(service.Propose(request.Policy, request.Inventory, request.AlreadyFirmedSupplyMt)));

    demoPlanning.MapPost("/campaigns/form",
        (CampaignPlanningRequest request, ICampaignPlanningService service) =>
            Results.Ok(service.FormCampaigns(request)));

    demoPlanning.MapPost("/structure/build",
        (ProductionStructurePlanningRequest request, IProductionStructurePlanningService service) =>
            Results.Ok(service.Build(request)));

    demoPlanning.MapPost("/schedule/solve",
        (FiniteScheduleRequest request, IFiniteScheduleOptimizer optimizer) =>
        {
            var result = optimizer.Solve(request);
            return result.IsFeasible ? Results.Ok(result) : Results.UnprocessableEntity(result);
        });

    demoPlanning.MapPost("/release/build",
        (PlanReleaseBuildRequest request, IPlanReleaseBuilder releaseBuilder) =>
        {
            if (!request.Schedule.IsFeasible)
                return Results.UnprocessableEntity(new { message = "Cannot build Work Orders from an infeasible demo schedule." });
            return Results.Ok(releaseBuilder.Build(request));
        });
}

if (hasApsDatabase)
{
    app.MapPost("/api/execution/work-orders/{workOrderId:guid}",
        async (Guid workOrderId, ManualWorkOrderExecutionRequest request, IWorkOrderExecutionService execution, CancellationToken cancellationToken) =>
        {
            var snapshot = await execution.ApplyAsync(new WorkOrderExecutionUpdate(
                workOrderId, null, request.Status, request.ActualStart, request.ActualEnd,
                request.ActualQuantityMt, request.ChangedOnUtc ?? DateTime.UtcNow,
                ExecutionUpdateSource.Manual, null, request.Comment, request.IsCorrection), cancellationToken);
            return Results.Ok(snapshot);
        });

    app.MapPost("/api/execution/operations",
        async (OperationExecutionUpdate update, IOperationExecutionService execution, CancellationToken cancellationToken) =>
            Results.Ok(await execution.ApplyAsync(update with { Source = ExecutionUpdateSource.Manual }, cancellationToken)));

    app.MapPost("/api/execution/heats",
        async (ManualHeatExecutionRequest request, IHeatExecutionService execution, CancellationToken cancellationToken) =>
        {
            var snapshot = await execution.ApplyAsync(new HeatExecutionUpdate(
                request.PlanVersionId, request.PlanningKey, request.Status,
                request.ChangedOnUtc ?? DateTime.UtcNow, ExecutionUpdateSource.Manual, null,
                request.ExternalHeatNumber, request.ExternalCastNumber, request.CasterResourceId,
                request.ActualStartUtc, request.ActualEndUtc, request.ActualQuantityMt,
                request.MaterialOutputs, request.Comment, request.IsCorrection), cancellationToken);
            return Results.Ok(snapshot);
        });

    app.MapPost("/api/integration/xstudio/operation-events",
        async (OperationExecutionUpdate update, IOperationExecutionService execution, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(update.ExternalEventId))
                return Results.BadRequest(new { message = "ExternalEventId is required for MES operation events." });
            return Results.Ok(await execution.ApplyAsync(update with { Source = ExecutionUpdateSource.MesApi }, cancellationToken));
        });

    app.MapPost("/api/integration/xstudio/execution-events",
        async (WorkOrderExecutionUpdate update, IWorkOrderExecutionService execution, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(update.ExternalExecutionId))
                return Results.BadRequest(new { message = "ExternalExecutionId is required for MES execution events." });
            var snapshot = await execution.ApplyAsync(update with { WorkOrderId = null, Source = ExecutionUpdateSource.MesApi }, cancellationToken);
            return Results.Ok(snapshot);
        });

    app.MapPost("/api/integration/xstudio/heat-events",
        async (HeatExecutionUpdate update, IHeatExecutionService execution, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(update.ExternalEventId))
                return Results.BadRequest(new { message = "ExternalEventId is required for MES heat events." });
            return Results.Ok(await execution.ApplyAsync(update with { Source = ExecutionUpdateSource.MesApi }, cancellationToken));
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