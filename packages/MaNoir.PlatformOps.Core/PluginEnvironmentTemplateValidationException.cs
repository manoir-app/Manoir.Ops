using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Core;

public sealed class PluginEnvironmentTemplateValidationException : Exception
{
	public PluginEnvironmentTemplateValidationException(IReadOnlyList<string> errors)
		: base("The plugin environment template is invalid.")
	{
		Errors = errors ?? Array.Empty<string>();
	}

	public IReadOnlyList<string> Errors { get; }
}