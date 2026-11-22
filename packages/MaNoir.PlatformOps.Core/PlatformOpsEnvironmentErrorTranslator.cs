using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Core;

/// <summary>
/// Reformule en français les erreurs de configuration produites par <see cref="PlatformOpsSecretsRuntimeGuard"/>.
/// Un message non reconnu est renvoyé inchangé.
/// </summary>
public static class PlatformOpsEnvironmentErrorTranslator
{
	private static readonly (string Suffix, string Format)[] Rules =
	[
		(" is required for PlatformOps secret operations.", "La variable {0} est absente ou vide."),
		(" must contain a valid Base64 payload.", "La variable {0} doit contenir une valeur Base64 valide."),
		(" must decode to at least 16 bytes.", "La variable {0} doit faire au moins 16 octets une fois décodée."),
		(" must contain at least 32 characters.", "La variable {0} doit contenir au moins 32 caractères.")
	];

	public static string ToFrench(string error)
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

	public static IReadOnlyList<string> ToFrench(IEnumerable<string> errors)
	{
		List<string> translated = new List<string>();
		if (errors == null)
			return translated;

		foreach (string error in errors)
			translated.Add(ToFrench(error));

		return translated;
	}
}
