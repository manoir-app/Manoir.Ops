using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Provider.Docker;

public sealed class DockerComposeValidationException : Exception
{
	public DockerComposeValidationException(IReadOnlyList<string> errors)
		: base("The Docker Compose file is invalid.")
	{
		Errors = errors ?? Array.Empty<string>();
	}

	public DockerComposeValidationException(IReadOnlyList<string> errors, Exception innerException)
		: base("The Docker Compose file is invalid.", innerException)
	{
		Errors = errors ?? Array.Empty<string>();
	}

	public IReadOnlyList<string> Errors { get; }
}