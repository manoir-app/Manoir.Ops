using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.RepresentationModel;

namespace MaNoir.PlatformOps.Core;

public static class PluginRepositoryDeploymentLoader
{
	public const string DefaultManifestFileName = "manoir.plugin.yaml";

	public static PluginDeploymentDescriptor Load(string repositoryRootPath)
	{
		PlatformOpsSecretsRuntimeGuard.EnsureConfigured();

		if (string.IsNullOrWhiteSpace(repositoryRootPath))
			throw new ArgumentException("A repository root path is required.", nameof(repositoryRootPath));

		string normalizedRepositoryRootPath = Path.GetFullPath(repositoryRootPath);

		if (!Directory.Exists(normalizedRepositoryRootPath))
			throw new DirectoryNotFoundException("The plugin repository root directory does not exist.");

		string manifestPath = Path.Combine(normalizedRepositoryRootPath, DefaultManifestFileName);

		if (!File.Exists(manifestPath))
			throw new FileNotFoundException("The plugin manifest file was not found.", manifestPath);

		PluginManifest manifest = PluginManifestParser.ParseFile(manifestPath);
		PluginDeploymentDescriptor descriptor = PluginDeploymentDescriptorFactory.Create(manifest);
		PluginEnvironmentTemplate environmentTemplate = null;

		string composeArtifactFullPath = ResolveArtifactPath(normalizedRepositoryRootPath, descriptor.ComposeArtifactPath, "compose");
		string environmentTemplateFullPath = ResolveArtifactPath(normalizedRepositoryRootPath, descriptor.EnvironmentTemplatePath, "env-template");

		if (!string.IsNullOrWhiteSpace(environmentTemplateFullPath))
		{
			environmentTemplate = PluginEnvironmentTemplateParser.ParseFile(environmentTemplateFullPath);
			descriptor = PluginDeploymentDescriptorFactory.Create(manifest, environmentTemplate);
		}

		descriptor.RepositoryRootPath = normalizedRepositoryRootPath;
		descriptor.ManifestPath = manifestPath;
		descriptor.ComposeArtifactFullPath = composeArtifactFullPath;
		descriptor.EnvironmentTemplateFullPath = environmentTemplateFullPath;
		ResolveAdminUiComposeServiceName(descriptor);

		return descriptor;
	}

	private static void ResolveAdminUiComposeServiceName(PluginDeploymentDescriptor descriptor)
	{
		if (descriptor == null)
			throw new ArgumentNullException(nameof(descriptor));

		if (!string.IsNullOrWhiteSpace(descriptor.AdminUiServiceName))
			return;

		if (string.IsNullOrWhiteSpace(descriptor.AdminUiPathPrefix) || !descriptor.AdminUiServicePort.HasValue)
			return;

		if (string.IsNullOrWhiteSpace(descriptor.ComposeArtifactFullPath))
			throw new InvalidOperationException("deployment.adminUi.composeService is required when no compose artifact is available to infer the exposed AdminUi service.");

		List<string> composeServiceNames = ReadComposeServiceNames(descriptor.ComposeArtifactFullPath);
		if (composeServiceNames.Count == 1)
		{
			descriptor.AdminUiServiceName = composeServiceNames[0];
			return;
		}

		if (composeServiceNames.Count == 0)
			throw new InvalidOperationException("deployment.adminUi.composeService is required because the compose artifact does not declare any service.");

		throw new InvalidOperationException("deployment.adminUi.composeService is required when the compose artifact declares multiple services.");
	}

	private static List<string> ReadComposeServiceNames(string composeArtifactFullPath)
	{
		YamlStream yamlStream = new YamlStream();
		using StreamReader reader = File.OpenText(composeArtifactFullPath);
		yamlStream.Load(reader);

		if (yamlStream.Documents.Count == 0 || yamlStream.Documents[0].RootNode is not YamlMappingNode rootMapping)
			return [];

		if (!rootMapping.Children.TryGetValue(new YamlScalarNode("services"), out YamlNode servicesNode) || servicesNode is not YamlMappingNode servicesMapping)
			return [];

		List<string> serviceNames = new List<string>();
		foreach (KeyValuePair<YamlNode, YamlNode> entry in servicesMapping.Children)
		{
			string serviceName = (entry.Key as YamlScalarNode)?.Value;
			if (!string.IsNullOrWhiteSpace(serviceName))
				serviceNames.Add(serviceName.Trim());
		}

		return serviceNames;
	}

	private static string ResolveArtifactPath(string repositoryRootPath, string artifactRelativePath, string artifactKind)
	{
		if (string.IsNullOrWhiteSpace(artifactRelativePath))
			return null;

		string resolvedArtifactPath = Path.GetFullPath(Path.Combine(repositoryRootPath, artifactRelativePath));

		if (!IsPathInsideRoot(repositoryRootPath, resolvedArtifactPath))
			throw new InvalidOperationException($"The {artifactKind} artifact resolves outside the repository root.");

		if (!File.Exists(resolvedArtifactPath))
			throw new FileNotFoundException($"The declared {artifactKind} artifact was not found.", resolvedArtifactPath);

		return resolvedArtifactPath;
	}

	private static bool IsPathInsideRoot(string repositoryRootPath, string candidatePath)
	{
		string normalizedRepositoryRootPath = repositoryRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

		return candidatePath.StartsWith(normalizedRepositoryRootPath, comparison);
	}
}