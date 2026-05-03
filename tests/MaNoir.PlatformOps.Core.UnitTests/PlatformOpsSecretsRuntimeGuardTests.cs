using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class PlatformOpsSecretsRuntimeGuardTests
{
	[TestMethod]
	public void EnsureConfigured_ShouldReadApiKeyAndDecodedSalt()
	{
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, "test-primary-key");
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, "AAECAwQFBgcICQoLDA0ODxAREhM=");
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, "12345678901234567890123456789012");

		PlatformOpsSecretsRuntimeConfiguration configuration = PlatformOpsSecretsRuntimeGuard.EnsureConfigured();

		Assert.AreEqual("test-primary-key", configuration.ApiKey);
		Assert.AreEqual("AAECAwQFBgcICQoLDA0ODxAREhM=", configuration.SaltBase64);
		Assert.AreEqual("12345678901234567890123456789012", configuration.AuthJwtSigningKey);
		Assert.AreEqual(20, configuration.SaltBytes.Length);
	}

	[TestMethod]
	public void TryEnsureConfigured_ShouldReturnConfigurationWithoutThrowing()
	{
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, "test-primary-key");
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, "AAECAwQFBgcICQoLDA0ODxAREhM=");
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, "12345678901234567890123456789012");

		bool isConfigured = PlatformOpsSecretsRuntimeGuard.TryEnsureConfigured(out PlatformOpsSecretsRuntimeConfiguration configuration, out IReadOnlyList<string> errors);

		Assert.IsTrue(isConfigured);
		Assert.IsNotNull(configuration);
		Assert.AreEqual(0, errors.Count);
	}

	[TestMethod]
	public void TryEnsureConfigured_ShouldReturnErrorsWithoutThrowing()
	{
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, null);
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, null);
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, null);

		bool isConfigured = PlatformOpsSecretsRuntimeGuard.TryEnsureConfigured(out PlatformOpsSecretsRuntimeConfiguration configuration, out IReadOnlyList<string> errors);

		Assert.IsFalse(isConfigured);
		Assert.IsNull(configuration);
		CollectionAssert.Contains((System.Collections.ICollection)errors, "HOMEAUTOMATION_APIKEY is required for PlatformOps secret operations.");
		CollectionAssert.Contains((System.Collections.ICollection)errors, "HOMEAUTOMATION_SECRETS_SALT is required for PlatformOps secret operations.");
		CollectionAssert.Contains((System.Collections.ICollection)errors, "HOMEAUTOMATION_AUTH_JWT_SIGNING_KEY is required for PlatformOps secret operations.");
	}

	[TestMethod]
	public void EnsureConfigured_ShouldRejectMissingVariables()
	{
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, null);
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, null);
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, null);

		PlatformOpsSecretsRuntimeConfigurationException exception = Assert.ThrowsException<PlatformOpsSecretsRuntimeConfigurationException>(() => PlatformOpsSecretsRuntimeGuard.EnsureConfigured());

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "HOMEAUTOMATION_APIKEY is required for PlatformOps secret operations.");
		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "HOMEAUTOMATION_SECRETS_SALT is required for PlatformOps secret operations.");
		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "HOMEAUTOMATION_AUTH_JWT_SIGNING_KEY is required for PlatformOps secret operations.");
	}

	[TestMethod]
	public void EnsureConfigured_ShouldRejectInvalidBase64Salt()
	{
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, "test-primary-key");
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, "not-base64");
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, "12345678901234567890123456789012");

		PlatformOpsSecretsRuntimeConfigurationException exception = Assert.ThrowsException<PlatformOpsSecretsRuntimeConfigurationException>(() => PlatformOpsSecretsRuntimeGuard.EnsureConfigured());

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "HOMEAUTOMATION_SECRETS_SALT must contain a valid Base64 payload.");
	}

	[TestMethod]
	public void EnsureConfigured_ShouldRejectShortDecodedSalt()
	{
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, "test-primary-key");
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, "AAECAwQFBgcICQoLDA0O");
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, "12345678901234567890123456789012");

		PlatformOpsSecretsRuntimeConfigurationException exception = Assert.ThrowsException<PlatformOpsSecretsRuntimeConfigurationException>(() => PlatformOpsSecretsRuntimeGuard.EnsureConfigured());

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "HOMEAUTOMATION_SECRETS_SALT must decode to at least 16 bytes.");
	}

	[TestMethod]
	public void EnsureConfigured_ShouldRejectShortJwtSigningKey()
	{
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, "test-primary-key");
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, "AAECAwQFBgcICQoLDA0ODxAREhM=");
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, "too-short");

		PlatformOpsSecretsRuntimeConfigurationException exception = Assert.ThrowsException<PlatformOpsSecretsRuntimeConfigurationException>(() => PlatformOpsSecretsRuntimeGuard.EnsureConfigured());

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "HOMEAUTOMATION_AUTH_JWT_SIGNING_KEY must contain at least 32 characters.");
	}
}