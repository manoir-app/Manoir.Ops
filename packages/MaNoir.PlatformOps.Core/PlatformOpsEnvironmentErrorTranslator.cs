using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Core;

/// <summary>
/// Rewrites the configuration errors produced by <see cref="PlatformOpsSecretsRuntimeGuard"/> as readable English sentences.
/// The Gaia page translates them for other browser languages. An unrecognised message is returned unchanged.
/// </summary>
public static class PlatformOpsEnvironmentErrorTranslator
{
	private static readonly (string Suffix, string Format)[] Rules =
	[
		(" is required for PlatformOps secret operations.", "The variable {0} is missing or empty."),
		(" must contain a valid Base64 payload.", "The variable {0} must contain a valid Base64 value."),
		(" must decode to at least 16 bytes.", "The variable {0} must be at least 16 bytes once decoded."),
		(" must contain at least 32 characters.", "The variable {0} must contain at least 32 characters.")
	];

	public static string ToReadable(string error)
	{
		if (string.IsNullOrWhiteSpace(error))
			return error;

		foreach ((string suffix, string format) in Rules)
		{
			if (error.EndsWith(suffix, StringComparison.Ordinal))
				return string.Format(format, error.Substring(0, error.Length - suffix.Length));
		}

		return error;
	}

	public static IReadOnlyList<string> ToReadable(IEnumerable<string> errors)
	{
		List<string> translated = new List<string>();
		if (errors == null)
			return translated;

		foreach (string error in errors)
			translated.Add(ToReadable(error));

		return translated;
	}
}
