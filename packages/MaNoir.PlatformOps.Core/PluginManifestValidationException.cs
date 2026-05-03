using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Core;

public sealed class PluginManifestValidationException : Exception
{
	public PluginManifestValidationException(IReadOnlyList<string> errors)
		: base("The plugin manifest is invalid.")
	{
		Errors = errors ?? Array.Empty<string>();
	}

	public PluginManifestValidationException(IReadOnlyList<string> errors, Exception innerException)
		: base("The plugin manifest is invalid.", innerException)
	{
		Errors = errors ?? Array.Empty<string>();
	}

	public IReadOnlyList<string> Errors { get; }
}