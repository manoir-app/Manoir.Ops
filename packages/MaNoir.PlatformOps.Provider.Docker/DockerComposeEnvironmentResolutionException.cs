using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Provider.Docker;

public sealed class DockerComposeEnvironmentResolutionException : Exception
{
	public DockerComposeEnvironmentResolutionException(IReadOnlyList<string> errors)
		: base("The Docker Compose environment could not be resolved.")
	{
		Errors = errors ?? Array.Empty<string>();
	}

	public IReadOnlyList<string> Errors { get; }
}