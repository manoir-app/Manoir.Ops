using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Core;

public sealed class PluginDeploymentDescriptor
{
	public string PluginId { get; set; }

	public string RepositoryRootPath { get; set; }

	public string ManifestPath { get; set; }

	public string RepoUrl { get; set; }

	public string DisplayName { get; set; }

	public string Version { get; set; }

	public string MinimumMaNoirVersion { get; set; }

	public string DeploymentGroup { get; set; }

	public string ComposeArtifactPath { get; set; }

	public string ComposeArtifactFullPath { get; set; }

	public string EnvironmentTemplatePath { get; set; }

	public string EnvironmentTemplateFullPath { get; set; }

	public IReadOnlyList<PluginEnvironmentVariable> EnvironmentVariables { get; set; } = Array.Empty<PluginEnvironmentVariable>();
}