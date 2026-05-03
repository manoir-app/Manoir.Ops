using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace MaNoir.PlatformOps.Core;

public static class PluginEnvironmentTemplateParser
{
	private static readonly Regex SecretReferenceRegex = new Regex(@"^\$\{\{\s*secrets\.([A-Za-z0-9_\-\.]+)\s*\}\}$", RegexOptions.Compiled);

	public static PluginEnvironmentTemplate Parse(string templateText)
	{
		if (templateText == null)
			throw new ArgumentNullException(nameof(templateText));

		List<string> errors = new List<string>();
		List<PluginEnvironmentVariable> variables = new List<PluginEnvironmentVariable>();
		HashSet<string> variableNames = new HashSet<string>(StringComparer.Ordinal);
		string[] lines = templateText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

		for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
		{
			string line = lines[lineIndex];
			string trimmedLine = line.Trim();

			if (trimmedLine.Length == 0 || trimmedLine.StartsWith("#", StringComparison.Ordinal))
				continue;

			int separatorIndex = line.IndexOf('=');

			if (separatorIndex <= 0)
			{
				errors.Add($"Line {lineIndex + 1} must be in NAME=VALUE format.");
				continue;
			}

			string name = line.Substring(0, separatorIndex).Trim();
			string rawValue = line.Substring(separatorIndex + 1);

			if (string.IsNullOrWhiteSpace(name))
			{
				errors.Add($"Line {lineIndex + 1} contains an empty variable name.");
				continue;
			}

			if (!variableNames.Add(name))
			{
				errors.Add($"Variable '{name}' is declared more than once.");
				continue;
			}

			if (rawValue.IndexOf('\r') >= 0)
				rawValue = rawValue.Replace("\r", string.Empty, StringComparison.Ordinal);

			PluginEnvironmentVariable variable = ParseVariable(name, rawValue);
			variables.Add(variable);
		}

		if (errors.Count > 0)
			throw new PluginEnvironmentTemplateValidationException(errors);

		return new PluginEnvironmentTemplate()
		{
			Variables = variables
		};
	}

	public static PluginEnvironmentTemplate ParseFile(string templatePath)
	{
		if (string.IsNullOrWhiteSpace(templatePath))
			throw new ArgumentException("A template path is required.", nameof(templatePath));

		return Parse(File.ReadAllText(templatePath));
	}

	private static PluginEnvironmentVariable ParseVariable(string name, string rawValue)
	{
		Match secretReferenceMatch = SecretReferenceRegex.Match(rawValue.Trim());

		if (secretReferenceMatch.Success)
		{
			return new PluginEnvironmentVariable()
			{
				Name = name,
				RawValue = rawValue,
				ValueKind = PluginEnvironmentValueKind.SecretReference,
				SecretName = secretReferenceMatch.Groups[1].Value
			};
		}

		return new PluginEnvironmentVariable()
		{
			Name = name,
			RawValue = rawValue,
			ValueKind = PluginEnvironmentValueKind.Literal,
			LiteralValue = rawValue
		};
	}
}