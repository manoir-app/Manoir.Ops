using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Provider.Docker;

public sealed class DockerComposeFile
{
	public string Name { get; set; }

	public string Version { get; set; }

	public IReadOnlyList<DockerComposeService> Services { get; set; } = Array.Empty<DockerComposeService>();
}

public sealed class DockerComposeService
{
	public string Name { get; set; }

	public string Image { get; set; }

	public string BuildContext { get; set; }

	public string ContainerName { get; set; }

	public string RestartPolicy { get; set; }

	public IReadOnlyList<string> Ports { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> Volumes { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> DependsOn { get; set; } = Array.Empty<string>();

	public IReadOnlyList<DockerComposeEnvironmentEntry> Environment { get; set; } = Array.Empty<DockerComposeEnvironmentEntry>();
}

public sealed class DockerComposeEnvironmentEntry
{
	public string Name { get; set; }

	public string Value { get; set; }
}