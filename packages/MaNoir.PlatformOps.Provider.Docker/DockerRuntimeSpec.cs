using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Provider.Docker;

public enum DockerRuntimeMountKind
{
	Bind = 0,
	Volume = 1
}

public sealed class DockerRuntimeSpec
{
	public string PluginId { get; set; }

	public string NetworkName { get; set; }

	public IReadOnlyList<DockerRuntimeServiceSpec> Services { get; set; } = Array.Empty<DockerRuntimeServiceSpec>();
}

public sealed class DockerRuntimeServiceSpec
{
	public string ServiceName { get; set; }

	public string ContainerName { get; set; }

	public IReadOnlyList<string> NetworkAliases { get; set; } = Array.Empty<string>();

	public string Image { get; set; }

	public string RestartPolicy { get; set; }

	public IReadOnlyList<string> Environment { get; set; } = Array.Empty<string>();

	public IReadOnlyList<DockerRuntimePortBinding> PortBindings { get; set; } = Array.Empty<DockerRuntimePortBinding>();

	public IReadOnlyList<DockerRuntimeMount> Mounts { get; set; } = Array.Empty<DockerRuntimeMount>();
}

public sealed class DockerRuntimePortBinding
{
	public string Protocol { get; set; }

	public int ContainerPort { get; set; }

	public int HostPort { get; set; }
}

public sealed class DockerRuntimeMount
{
	public DockerRuntimeMountKind Kind { get; set; }

	public string Source { get; set; }

	public string Target { get; set; }

	public bool IsReadOnly { get; set; }

	public string ToBindString()
	{
		return IsReadOnly
			? Source + ":" + Target + ":ro"
			: Source + ":" + Target;
	}
}