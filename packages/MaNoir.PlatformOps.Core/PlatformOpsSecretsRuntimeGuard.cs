using System;
using System.Collections.Generic;

namespace MaNoir.PlatformOps.Core;

public static class PlatformOpsSecretsRuntimeGuard
{
	public const string ApiKeyEnvironmentVariableName = "HOMEAUTOMATION_APIKEY";

	public const string SecretsSaltEnvironmentVariableName = "HOMEAUTOMATION_SECRETS_SALT";

	public const string AuthJwtSigningKeyEnvironmentVariableName = "HOMEAUTOMATION_AUTH_JWT_SIGNING_KEY";

	public static PlatformOpsSecretsRuntimeConfiguration EnsureConfigured()
	{
		if (!TryEnsureConfigured(out PlatformOpsSecretsRuntimeConfiguration configuration, out IReadOnlyList<string> errors))
			throw new PlatformOpsSecretsRuntimeConfigurationException(errors);

		return configuration;
	}

	public static bool TryEnsureConfigured(out PlatformOpsSecretsRuntimeConfiguration configuration, out IReadOnlyList<string> errors)
	{
		List<string> collectedErrors = new List<string>();
		string apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariableName);
		string saltBase64 = Environment.GetEnvironmentVariable(SecretsSaltEnvironmentVariableName);
		string authJwtSigningKey = Environment.GetEnvironmentVariable(AuthJwtSigningKeyEnvironmentVariableName)?.Trim();
		byte[] saltBytes = null;

		if (string.IsNullOrWhiteSpace(apiKey))
			collectedErrors.Add(ApiKeyEnvironmentVariableName + " is required for PlatformOps secret operations.");

		if (string.IsNullOrWhiteSpace(saltBase64))
		{
			collectedErrors.Add(SecretsSaltEnvironmentVariableName + " is required for PlatformOps secret operations.");
		}
		else
		{
			try
			{
				saltBytes = Convert.FromBase64String(saltBase64);
			}
			catch (FormatException)
			{
				collectedErrors.Add(SecretsSaltEnvironmentVariableName + " must contain a valid Base64 payload.");
			}

			if (saltBytes != null && saltBytes.Length < 16)
				collectedErrors.Add(SecretsSaltEnvironmentVariableName + " must decode to at least 16 bytes.");
		}

		if (string.IsNullOrWhiteSpace(authJwtSigningKey))
		{
			collectedErrors.Add(AuthJwtSigningKeyEnvironmentVariableName + " is required for PlatformOps secret operations.");
		}
		else if (authJwtSigningKey.Length < 32)
		{
			collectedErrors.Add(AuthJwtSigningKeyEnvironmentVariableName + " must contain at least 32 characters.");
		}

		if (collectedErrors.Count > 0)
		{
			configuration = null;
			errors = collectedErrors;
			return false;
		}

		configuration = new PlatformOpsSecretsRuntimeConfiguration()
		{
			ApiKey = apiKey,
			SaltBase64 = saltBase64,
			AuthJwtSigningKey = authJwtSigningKey,
			SaltBytes = saltBytes
		};
		errors = Array.Empty<string>();
		return true;
	}
}