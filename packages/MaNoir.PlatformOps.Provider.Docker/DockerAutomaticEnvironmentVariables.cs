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
}