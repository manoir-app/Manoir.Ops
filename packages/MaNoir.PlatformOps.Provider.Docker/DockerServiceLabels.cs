using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Provider.Docker;

/// <summary>
/// English labels shown in the Gaia console for the technical services (the page translates them).
/// The technical names (mongo, nats...) remain the reference; an unknown name is returned as is.
/// </summary>
public static class DockerServiceLabels
{
	private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["core"] = "Administration console",
		["traefik"] = "Traefik router",
		["mongo"] = "Database",
		["nats"] = "Messaging",
		["mqtt"] = "Sensors and devices",
		["redis"] = "Cache",
		["loki"] = "Logs",
		["tempo"] = "Traces",
		["prometheus"] = "Metrics",
		["grafana"] = "Monitoring dashboards"
	};

	public static string GetLabel(string serviceName)
	{
		if (string.IsNullOrWhiteSpace(serviceName))
			return string.Empty;

		return Labels.TryGetValue(serviceName.Trim(), out string label) ? label : serviceName;
	}
}
