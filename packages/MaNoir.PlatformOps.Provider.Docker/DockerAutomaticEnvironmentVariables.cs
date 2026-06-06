using System;
using System.Collections.Generic;
using System.Linq;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Provider.Docker;

public static class DockerAutomaticEnvironmentVariables
{
	public static IReadOnlyList<DockerResolvedEnvironmentEntry> CreateResolvedEntries(string pluginId)
	{
		if (string.Equals(pluginId, DockerSharedServicesCatalog.SharedServicesPluginId, StringComparison.OrdinalIgnoreCase))
			return Array.Empty<DockerResolvedEnvironmentEntry>();

		List<DockerResolvedEnvironmentEntry> entries =
		[
			new DockerResolvedEnvironmentEntry() { Name = "MONGODB_SERVICE_HOST", Value = "mongo" },
			new DockerResolvedEnvironmentEntry() { Name = "MONGODB_SERVICE_PORT", Value = "27017" },
			new DockerResolvedEnvironmentEntry() { Name = "NATS_SERVICE_HOST", Value = "nats" },
			new DockerResolvedEnvironmentEntry() { Name = "NATS_SERVICE_PORT", Value = "4222" },
			new DockerResolvedEnvironmentEntry() { Name = "MQTT_SERVICE_HOST", Value = "mqtt" },
			new DockerResolvedEnvironmentEntry() { Name = "MQTT_SERVICE_PORT", Value = "1883" },
			new DockerResolvedEnvironmentEntry() { Name = "REDIS_SERVICE_HOST", Value = "redis" },
			new DockerResolvedEnvironmentEntry() { Name = "REDIS_SERVICE_PORT", Value = "6379" }
		];

		if (IsObservabilityEnabled())
		{
			entries.Add(new DockerResolvedEnvironmentEntry() { Name = "MANOIR_OBSERVABILITY_ENABLED", Value = "true" });
			entries.Add(new DockerResolvedEnvironmentEntry() { Name = "MANOIR_OTEL_TRACES_ENDPOINT", Value = ResolveEnvironmentValue("MANOIR_OTEL_TRACES_ENDPOINT", "http://tempo:4318/v1/traces") });
			entries.Add(new DockerResolvedEnvironmentEntry() { Name = "MANOIR_OTEL_LOGS_ENDPOINT", Value = ResolveEnvironmentValue("MANOIR_OTEL_LOGS_ENDPOINT", "http://loki:3100/otlp/v1/logs") });
			entries.Add(new DockerResolvedEnvironmentEntry() { Name = "MANOIR_PROMETHEUS_METRICS_PATH", Value = ResolveEnvironmentValue("MANOIR_PROMETHEUS_METRICS_PATH", "/metrics") });
		}

		string authJwtSigningKey = Environment.GetEnvironmentVariable(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName);
		if (!string.IsNullOrWhiteSpace(authJwtSigningKey))
		{
			entries.Add(new DockerResolvedEnvironmentEntry()
			{
				Name = PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName,
				Value = authJwtSigningKey.Trim()
			});
		}

		return entries;
	}

	public static IReadOnlyDictionary<string, string> CreateVariablesByName(string pluginId)
	{
		return CreateResolvedEntries(pluginId)
			.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
	}

	private static bool IsObservabilityEnabled()
	{
		string rawValue = Environment.GetEnvironmentVariable("MANOIR_OBSERVABILITY_ENABLED");
		return string.Equals(rawValue?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(rawValue?.Trim(), "1", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(rawValue?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
	}

	private static string ResolveEnvironmentValue(string environmentVariableName, string defaultValue)
	{
		string rawValue = Environment.GetEnvironmentVariable(environmentVariableName);
		return string.IsNullOrWhiteSpace(rawValue) ? defaultValue : rawValue.Trim();
	}
}