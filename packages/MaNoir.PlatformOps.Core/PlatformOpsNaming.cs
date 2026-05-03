using System;
using System.Text;

namespace MaNoir.PlatformOps.Core;

public static class PlatformOpsNaming
{
	public static string NormalizeSegment(string value, string fallbackValue = null)
	{
		string effectiveValue = string.IsNullOrWhiteSpace(value) ? fallbackValue : value;
		if (string.IsNullOrWhiteSpace(effectiveValue))
			return null;

		StringBuilder builder = new StringBuilder();
		bool previousWasSeparator = false;

		foreach (char character in effectiveValue.Trim().ToLowerInvariant())
		{
			if (char.IsLetterOrDigit(character))
			{
				builder.Append(character);
				previousWasSeparator = false;
				continue;
			}

			if (character == '-' || character == '_' || character == '.' || char.IsWhiteSpace(character))
			{
				if (builder.Length == 0 || previousWasSeparator)
					continue;

				builder.Append('-');
				previousWasSeparator = true;
			}
		}

		string normalizedValue = builder.ToString().Trim('-');
		return normalizedValue.Length == 0 ? null : normalizedValue;
	}

	public static string RequireSegment(string value, string fallbackValue, string parameterName)
	{
		string normalizedValue = NormalizeSegment(value, fallbackValue);
		if (normalizedValue == null)
			throw new ArgumentException("A normalized segment value is required.", parameterName);

		return normalizedValue;
	}
}
