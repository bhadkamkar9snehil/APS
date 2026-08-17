using APS.Application;
using APS.Infrastructure;
using APS.Planning;
using APS.UI.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<IMtsProductionOrderService, MtsProductionOrderService>();
builder.Services.AddScoped<ICampaignPlanningService, CampaignPlanningService>();

var apsConnection = builder.Configuration.GetConnectionString("APS");
if (!string.IsNullOrWhiteSpace(apsConnection))
{
    builder.Services.AddDbContext<ApsDbContext>(options => options.UseSqlServer(apsConnection));
}

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapGet("/api/health", () => Results.Ok(new
{
    service = "APS.Service",
    status = "ok",
    utc = DateTime.UtcNow
}));

app.MapPost("/api/planning/mts/production-order",
    (MtsProductionOrderRequest request, IMtsProductionOrderService service) =>
        Results.Ok(service.Propose(request.Policy, request.Inventory, request.AlreadyFirmedSupplyMt)));

app.MapPost("/api/planning/campaigns/form",
    (CampaignPlanningRequest request, ICampaignPlanningService service) =>
        Results.Ok(service.FormCampaigns(request)));

app.MapPost("/api/integration/xstudio/execution-events",
    (ExecutionActual actual) => Results.Accepted(value: new
    {
        received = true,
        actual.ExternalWorkOrderId,
        actual.ChangedOnUtc
    }));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public sealed record MtsProductionOrderRequest(
    StockPolicy Policy,
    APS.Domain.InventoryPosition Inventory,
    decimal AlreadyFirmedSupplyMt = 0m);
