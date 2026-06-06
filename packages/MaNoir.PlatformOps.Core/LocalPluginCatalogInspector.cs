using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MaNoir.PlatformOps.Core;

public sealed class RequiredPluginAvailabilityEvaluation
{
	public IReadOnlyList<string> RequiredPluginIds { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> AvailablePluginIds { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> MissingRequiredPluginIds { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();

	public bool HasAllRequiredPluginsAvailable => MissingRequiredPluginIds.Count == 0;
}

public static class LocalPluginCatalogInspector
{
	public static RequiredPluginAvailabilityEvaluation EvaluateRequiredPlugins(string pluginCatalogRootPath, IEnumerable<string> requiredPluginIds)
	{
		string[] normalizedRequiredPluginIds = (requiredPluginIds ?? Array.Empty<string>())
			.Where(pluginId => !string.IsNullOrWhiteSpace(pluginId))
			.Select(pluginId => pluginId.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(pluginId => pluginId, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		List<string> availablePluginIds = new List<string>();
		List<string> errors = new List<string>();

		if (!string.IsNullOrWhiteSpace(pluginCatalogRootPath) && Directory.Exists(pluginCatalogRootPath))
		{
			foreach (string manifestPath in Directory.EnumerateFiles(pluginCatalogRootPath, PluginRepositoryDeploymentLoader.DefaultManifestFileName, SearchOption.AllDirectories))
			{
				try
				{
					PluginManifest manifest = PluginManifestParser.ParseFile(manifestPath);
					if (!string.IsNullOrWhiteSpace(manifest?.Plugin?.PluginId))
						availablePluginIds.Add(manifest.Plugin.PluginId.Trim());
				}
				catch (Exception exception)
				{
					errors.Add("The plugin manifest '" + manifestPath + "' could not be read: " + exception.Message);
				}
			}
		}

		string[] normalizedAvailablePluginIds = availablePluginIds
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(pluginId => pluginId, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		string[] missingRequiredPluginIds = normalizedRequiredPluginIds
			.Where(requiredPluginId => !normalizedAvailablePluginIds.Contains(requiredPluginId, StringComparer.OrdinalIgnoreCase))
			.ToArray();

		return new RequiredPluginAvailabilityEvaluation()
		{
			RequiredPluginIds = normalizedRequiredPluginIds,
			AvailablePluginIds = normalizedAvailablePluginIds,
			MissingRequiredPluginIds = missingRequiredPluginIds,
			Errors = errors
		};
	}
}