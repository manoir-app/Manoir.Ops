using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Provider.Docker;

public sealed class DockerDeploymentExecutionResult
{
	public IReadOnlyList<string> PulledImages { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> RecreatedContainers { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> StartedContainers { get; set; } = Array.Empty<string>();
}