using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Core;

public sealed class PluginEnvironmentSecretsResolutionException : Exception
{
	public PluginEnvironmentSecretsResolutionException(IReadOnlyList<string> errors)
		: base("The plugin environment secrets could not be resolved.")
	{
		Errors = errors ?? Array.Empty<string>();
	}

	public IReadOnlyList<string> Errors { get; }
}