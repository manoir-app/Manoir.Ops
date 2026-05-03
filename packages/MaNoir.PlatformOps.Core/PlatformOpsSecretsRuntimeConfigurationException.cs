using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Core;

public sealed class PlatformOpsSecretsRuntimeConfigurationException : Exception
{
	public PlatformOpsSecretsRuntimeConfigurationException(IReadOnlyList<string> errors)
		: base("The PlatformOps secret runtime configuration is invalid.")
	{
		Errors = errors ?? Array.Empty<string>();
	}

	public IReadOnlyList<string> Errors { get; }
}