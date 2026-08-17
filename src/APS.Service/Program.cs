using System.Text.Json.Serialization;
using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using APS.Planning;
using APS.UI.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
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
}

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapGet("/api/health", () => Results.Ok(new
{
    service = "APS.Service",
    status = "ok",
    databaseConfigured = hasApsDatabase,
    utc = DateTime.UtcNow
}));

app.MapPost("/api/planning/run",
    (PlanningRunRequest request, IPlanningEngine planningEngine) =>
    {
        var result = planningEngine.Run(request);
        return result.IsFeasible ? Results.Ok(result) : Results.UnprocessableEntity(result);
    });

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
    .AddInteractiveServerRenderMode();

app.Run();

public sealed record MtsProductionOrderRequest(
    StockPolicy Policy,
    InventoryPosition Inventory,
    decimal AlreadyFirmedSupplyMt = 0m);

public sealed record ManualWorkOrderExecutionRequest(
    WorkOrderStatus Status,
    DateTime? ActualStart = null,
    DateTime? ActualEnd = null,
    decimal? ActualQuantityMt = null,
    DateTime? ChangedOnUtc = null,
    string? Comment = null,
    bool IsCorrection = false);
