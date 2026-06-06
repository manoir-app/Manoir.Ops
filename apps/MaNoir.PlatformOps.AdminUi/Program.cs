using System;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MaNoir.Core.Observability;
using MaNoir.PlatformOps.AdminUi;

EnsureGaiaObservabilityDefaults();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddMaNoirWebObservability("manoir-gaia");

GaiaOptions gaiaOptions = new GaiaOptions();
builder.Configuration.GetSection("Gaia").Bind(gaiaOptions);

builder.Services.AddSingleton(gaiaOptions);
builder.Services.AddSingleton<GaiaOperationsService>();
builder.Services.AddSingleton<GaiaAgentRuntime>();
builder.Services.AddSingleton<GaiaAgentLifecycleService>();
builder.Services.AddSingleton<GaiaMessageRouter>();
builder.Services.AddSingleton<GaiaMessagePumpService>();
builder.Services.AddHostedService<GaiaHostedService>();
builder.Services.AddHostedService(static serviceProvider => serviceProvider.GetRequiredService<GaiaAgentLifecycleService>());
builder.Services.AddHostedService(static serviceProvider => serviceProvider.GetRequiredService<GaiaMessagePumpService>());

WebApplication app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapMaNoirWebObservability();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/ops/gaia/state", async (GaiaOperationsService gaia, CancellationToken cancellationToken) => Results.Ok(await gaia.GetStateAsync(cancellationToken)));
app.MapGet("/api/ops/gaia/admin-ui-deployments", async (GaiaOperationsService gaia, CancellationToken cancellationToken) => Results.Ok(await gaia.GetAdminUiDeploymentsAsync(cancellationToken)));
app.MapGet("/api/ops/gaia/admin-ui-deployment-diffs", async (GaiaOperationsService gaia, CancellationToken cancellationToken) => Results.Ok(await gaia.GetAdminUiDeploymentDiffsAsync(cancellationToken)));
app.MapGet("/api/ops/gaia/admin-ui-route-diagnostics", async (GaiaOperationsService gaia, CancellationToken cancellationToken) => Results.Ok(await gaia.GetAdminUiRouteDiagnosticsAsync(cancellationToken)));
app.MapGet("/api/ops/gaia/plugin-repositories", async (GaiaOperationsService gaia, CancellationToken cancellationToken) => Results.Ok(await gaia.GetPluginRepositoriesAsync(cancellationToken)));
app.MapPost("/api/ops/gaia/inspect", async (GaiaOperationsService gaia, CancellationToken cancellationToken) => Results.Ok(await gaia.InspectAsync(cancellationToken)));
app.MapPost("/api/ops/gaia/ensure-minimum-vital", async (GaiaOperationsService gaia, CancellationToken cancellationToken) => Results.Ok(await gaia.EnsureMinimumVitalAsync(cancellationToken)));
app.MapPost("/api/ops/gaia/ensure-shared-services", async (GaiaOperationsService gaia, CancellationToken cancellationToken) => Results.Ok(await gaia.EnsureSharedServicesAsync(cancellationToken)));
app.MapPost("/api/ops/gaia/refresh-and-restart-all-plugins", async (GaiaOperationsService gaia, CancellationToken cancellationToken) => Results.Ok(await gaia.RefreshAndRestartAllPluginsAsync(cancellationToken)));
app.MapPost("/api/ops/gaia/plugin-repositories", async (GaiaOperationsService gaia, UpdatePluginRepositoriesRequest request, CancellationToken cancellationToken) => Results.Ok(await gaia.UpdatePluginRepositoriesAsync(request?.RepositoryUrls, cancellationToken)));
app.MapPost("/api/ops/gaia/plugin-repositories/resync", async (GaiaOperationsService gaia, CancellationToken cancellationToken) => Results.Ok(await gaia.ResyncPluginRepositoriesAsync(cancellationToken)));
app.MapPost("/api/ops/gaia/reset-shared-services", async (GaiaOperationsService gaia, ResetSharedServicesRequest request, CancellationToken cancellationToken) => Results.Ok(await gaia.ResetSharedServicesAsync(request?.WipeData == true, cancellationToken)));

app.MapFallbackToFile("index.html");

app.Run();

static void EnsureGaiaObservabilityDefaults()
{
	string enabledValue = Environment.GetEnvironmentVariable("MANOIR_OBSERVABILITY_ENABLED");
	if (string.IsNullOrWhiteSpace(enabledValue))
		Environment.SetEnvironmentVariable("MANOIR_OBSERVABILITY_ENABLED", "true");

	if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MANOIR_OTEL_TRACES_ENDPOINT")))
		Environment.SetEnvironmentVariable("MANOIR_OTEL_TRACES_ENDPOINT", "http://tempo:4318/v1/traces");

	if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MANOIR_OTEL_LOGS_ENDPOINT")))
		Environment.SetEnvironmentVariable("MANOIR_OTEL_LOGS_ENDPOINT", "http://loki:3100/otlp/v1/logs");

	if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MANOIR_PROMETHEUS_METRICS_PATH")))
		Environment.SetEnvironmentVariable("MANOIR_PROMETHEUS_METRICS_PATH", "/metrics");
}

public sealed class ResetSharedServicesRequest
{
	public bool WipeData { get; set; }
}

public sealed class UpdatePluginRepositoriesRequest
{
	public string[] RepositoryUrls { get; set; } = [];
}