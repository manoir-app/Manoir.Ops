using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Provider.Docker;

/// <summary>
/// Libellés français affichés dans la console Gaia pour les services techniques.
/// Les noms techniques (mongo, nats...) restent la référence ; un nom inconnu est renvoyé tel quel.
/// </summary>
public static class DockerServiceLabels
{
	private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["core"] = "Console d’administration",
		["traefik"] = "Routeur Traefik",
		["mongo"] = "Base de données",
		["nats"] = "Messagerie",
		["mqtt"] = "Capteurs et appareils",
		["redis"] = "Cache",
		["loki"] = "Journaux",
		["tempo"] = "Traces",
		["prometheus"] = "Métriques",
		["grafana"] = "Tableaux de supervision"
	};

	public static string GetLabel(string serviceName)
	{
		if (string.IsNullOrWhiteSpace(serviceName))
			return string.Empty;

		return Labels.TryGetValue(serviceName.Trim(), out string label) ? label : serviceName;
	}
}
