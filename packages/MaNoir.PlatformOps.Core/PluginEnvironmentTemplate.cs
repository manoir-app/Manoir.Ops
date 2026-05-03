using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Core;

public enum PluginEnvironmentValueKind
{
	Literal = 0,
	SecretReference = 1
}

public sealed class PluginEnvironmentTemplate
{
	public IReadOnlyList<PluginEnvironmentVariable> Variables { get; set; } = Array.Empty<PluginEnvironmentVariable>();
}

public sealed class PluginEnvironmentVariable
{
	public string Name { get; set; }

	public string RawValue { get; set; }

	public PluginEnvironmentValueKind ValueKind { get; set; }

	public string LiteralValue { get; set; }

	public string SecretName { get; set; }
}

public sealed class PluginResolvedEnvironmentVariable
{
	public string Name { get; set; }

	public string Value { get; set; }

	public PluginEnvironmentValueKind ValueKind { get; set; }

	public string SecretName { get; set; }
}