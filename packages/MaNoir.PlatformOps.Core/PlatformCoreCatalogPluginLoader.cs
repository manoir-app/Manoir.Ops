using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.RepresentationModel;

namespace MaNoir.PlatformOps.Core;

public static class PlatformCoreCatalogPluginLoader
{
	public const string PlatformPluginId = "platform";

	public const string PlatformRepositoryUrl = "https://github.com/manoir-app/MaNoir.Platform";

	private const string PluginDefinitionFileName = "plugin.yaml";

	public static bool TryLoad(string pluginRepositoriesRootPath, out PluginDeploymentDescriptor descriptor, out string error)
	{
		descriptor = null;
		error = null;

		string normalizedRootPath = string.IsNullOrWhiteSpace(pluginRepositoriesRootPath)
			? null
			: Path.GetFullPath(pluginRepositoriesRootPath);

		if (string.IsNullOrWhiteSpace(normalizedRootPath) || !Directory.Exists(normalizedRootPath))
		{
			error = "Platform Core plugin definition was not found because the plugin repository root '" + (pluginRepositoriesRootPath ?? string.Empty) + "' does not exist.";
			return false;
		}

		foreach (string candidatePath in EnumerateCandidatePluginDefinitionPaths(normalizedRootPath))
		{
			if (!File.Exists(candidatePath))
				continue;

			try
			{
				if (TryParsePlatformDescriptor(candidatePath, out descriptor))
					return true;
			}
			catch (Exception exception)
			{
				error = "Platform Core plugin definition '" + candidatePath + "' could not be read: " + exception.Message;
				return false;
			}
		}

		error = "Platform Core plugin definition was not found under '" + normalizedRootPath + "'.";
		return false;
	}

	private static IEnumerable<string> EnumerateCandidatePluginDefinitionPaths(string pluginRepositoriesRootPath)
	{
		yield return Path.Combine(pluginRepositoriesRootPath, "plugins", "Core", "MainServices", "Platform", PluginDefinitionFileName);

		string managedRepositoriesRootPath = Path.Combine(pluginRepositoriesRootPath, "_managed");
		if (!Directory.Exists(managedRepositoriesRootPath))
			yield break;

		foreach (string repositoryRootPath in Directory.EnumerateDirectories(managedRepositoriesRootPath))
			yield return Path.Combine(repositoryRootPath, "plugins", "Core", "MainServices", "Platform", PluginDefinitionFileName);
	}

	private static bool TryParsePlatformDescriptor(string pluginDefinitionPath, out PluginDeploymentDescriptor descriptor)
	{
		YamlStream yamlStream = new YamlStream();
		using StreamReader reader = File.OpenText(pluginDefinitionPath);
		yamlStream.Load(reader);

		if (yamlStream.Documents.Count == 0 || yamlStream.Documents[0].RootNode is not YamlMappingNode root)
			throw new InvalidOperationException("The plugin definition YAML document is empty.");

		string kind = GetScalarValue(root, "kind");
		if (!string.Equals(kind, "AvailablePlugin", StringComparison.Ordinal))
		{
			descriptor = null;
			return false;
		}

		string repoUrl = GetScalarValue(root, "repoUrl");
		if (!string.Equals(repoUrl, PlatformRepositoryUrl, StringComparison.OrdinalIgnoreCase))
		{
			descriptor = null;
			return false;
		}

		YamlMappingNode deployment = GetRequiredMapping(root, "deployment");
		YamlMappingNode adminUi = GetRequiredMapping(deployment, "adminUi");
		string composeArtifactPath = GetRequiredComposeArtifactPath(deployment);

		string pluginRootPath = Path.GetDirectoryName(pluginDefinitionPath) ?? throw new InvalidOperationException("The plugin definition path is invalid.");
		string composeArtifactFullPath = Path.GetFullPath(Path.Combine(pluginRootPath, composeArtifactPath));
		if (!File.Exists(composeArtifactFullPath))
			throw new FileNotFoundException("The declared Platform Core compose artifact was not found.", composeArtifactFullPath);

		descriptor = new PluginDeploymentDescriptor()
		{
			PluginId = PlatformPluginId,
			RepositoryRootPath = pluginRootPath,
			ManifestPath = pluginDefinitionPath,
			RepoUrl = repoUrl,
			DisplayName = GetScalarValue(root, "displayName"),
			Version = GetScalarValue(root, "version"),
			MinimumMaNoirVersion = GetScalarValue(root, "minimumMaNoirVersion"),
			DeploymentGroup = GetScalarValue(deployment, "group"),
			AdminUiPathPrefix = GetRequiredScalarValue(adminUi, "pathPrefix"),
			AdminUiServiceName = GetScalarValue(adminUi, "composeService") ?? GetScalarValue(adminUi, "service"),
			AdminUiServicePort = GetRequiredIntValue(adminUi, "port"),
			ComposeArtifactPath = composeArtifactPath,
			ComposeArtifactFullPath = composeArtifactFullPath,
			EnvironmentVariables = Array.Empty<PluginEnvironmentVariable>()
		};

		return true;
	}

	private static string GetRequiredComposeArtifactPath(YamlMappingNode deployment)
	{
		if (!deployment.Children.TryGetValue(new YamlScalarNode("artifacts"), out YamlNode artifactsNode) || artifactsNode is not YamlSequenceNode artifacts)
			throw new InvalidOperationException("deployment.artifacts is required for Platform Core.");

		foreach (YamlNode artifactNode in artifacts.Children)
		{
			if (artifactNode is not YamlMappingNode artifact)
				continue;

			if (!string.Equals(GetScalarValue(artifact, "kind"), "compose", StringComparison.Ordinal))
				continue;

			return GetRequiredScalarValue(artifact, "path");
		}

		throw new InvalidOperationException("deployment.artifacts must declare a compose artifact for Platform Core.");
	}

	private static YamlMappingNode GetRequiredMapping(YamlMappingNode parent, string key)
	{
		if (!parent.Children.TryGetValue(new YamlScalarNode(key), out YamlNode value) || value is not YamlMappingNode mapping)
			throw new InvalidOperationException(key + " is required.");

		return mapping;
	}

	private static string GetRequiredScalarValue(YamlMappingNode parent, string key)
	{
		string value = GetScalarValue(parent, key);
		if (string.IsNullOrWhiteSpace(value))
			throw new InvalidOperationException(key + " is required.");

		return value.Trim();
	}

	private static int GetRequiredIntValue(YamlMappingNode parent, string key)
	{
		string value = GetRequiredScalarValue(parent, key);
		if (!int.TryParse(value, out int parsedValue))
			throw new InvalidOperationException(key + " must be an integer.");

		return parsedValue;
	}

	private static string GetScalarValue(YamlMappingNode parent, string key)
	{
		if (!parent.Children.TryGetValue(new YamlScalarNode(key), out YamlNode valueNode))
			return null;

		return (valueNode as YamlScalarNode)?.Value?.Trim();
	}
}