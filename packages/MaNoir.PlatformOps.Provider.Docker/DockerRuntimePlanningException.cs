using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Provider.Docker;

public sealed class DockerRuntimePlanningException : Exception
{
	public DockerRuntimePlanningException(IReadOnlyList<string> errors)
		: base("The Docker runtime plan is invalid.")
	{
		Errors = errors ?? Array.Empty<string>();
	}

	public IReadOnlyList<string> Errors { get; }
}