using System;
using System.IO;

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

		return descriptor;
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