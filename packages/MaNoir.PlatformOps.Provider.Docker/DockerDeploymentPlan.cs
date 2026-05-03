using System;
using System.Collections.Generic;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Provider.Docker;

public enum DockerImagePullPolicy
{
	IfNotPresent = 0,
	Always = 1
}

public sealed class DockerDeploymentPlan
{
	public string PluginId { get; set; }

	public string DeploymentGroup { get; set; }

	public string RepositoryRootPath { get; set; }

	public string ComposeFilePath { get; set; }

	public IReadOnlyList<PluginEnvironmentVariable> SharedEnvironmentVariables { get; set; } = Array.Empty<PluginEnvironmentVariable>();

	public IReadOnlyList<PluginResolvedEnvironmentVariable> ResolvedSharedEnvironmentVariables { get; set; } = Array.Empty<PluginResolvedEnvironmentVariable>();

	public IReadOnlyList<DockerDeploymentServicePlan> Services { get; set; } = Array.Empty<DockerDeploymentServicePlan>();
}

public sealed class DockerDeploymentServicePlan
{
	public string Name { get; set; }

	public string Image { get; set; }

	public string BuildContext { get; set; }

	public string ContainerName { get; set; }

	public string RestartPolicy { get; set; }

	public DockerImagePullPolicy ImagePullPolicy { get; set; }

	public IReadOnlyList<string> Ports { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> Volumes { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> DependsOn { get; set; } = Array.Empty<string>();

	public IReadOnlyList<DockerComposeEnvironmentEntry> Environment { get; set; } = Array.Empty<DockerComposeEnvironmentEntry>();

	public IReadOnlyList<DockerResolvedEnvironmentEntry> ResolvedEnvironment { get; set; } = Array.Empty<DockerResolvedEnvironmentEntry>();
}

public sealed class DockerResolvedEnvironmentEntry
{
	public string Name { get; set; }

	public string Value { get; set; }
}