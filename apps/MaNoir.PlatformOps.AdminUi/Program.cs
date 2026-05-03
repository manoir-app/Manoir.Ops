using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MaNoir.PlatformOps.AdminUi;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

GaiaOptions gaiaOptions = new GaiaOptions();
builder.Configuration.GetSection("Gaia").Bind(gaiaOptions);

builder.Services.AddSingleton(gaiaOptions);
builder.Services.AddSingleton<GaiaOperationsService>();
builder.Services.AddHostedService<GaiaHostedService>();

WebApplication app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/ops/gaia/state", async (GaiaOperationsService gaia, CancellationToken cancellationToken) => Results.Ok(await gaia.GetStateAsync(cancellationToken)));
app.MapPost("/api/ops/gaia/inspect", async (GaiaOperationsService gaia, CancellationToken cancellationToken) => Results.Ok(await gaia.InspectAsync(cancellationToken)));
app.MapPost("/api/ops/gaia/ensure-minimum-vital", async (GaiaOperationsService gaia, CancellationToken cancellationToken) => Results.Ok(await gaia.EnsureMinimumVitalAsync(cancellationToken)));
app.MapPost("/api/ops/gaia/ensure-shared-services", async (GaiaOperationsService gaia, CancellationToken cancellationToken) => Results.Ok(await gaia.EnsureSharedServicesAsync(cancellationToken)));
app.MapPost("/api/ops/gaia/refresh-and-restart-all-plugins", async (GaiaOperationsService gaia, CancellationToken cancellationToken) => Results.Ok(await gaia.RefreshAndRestartAllPluginsAsync(cancellationToken)));

app.MapFallbackToFile("index.html");

app.Run();