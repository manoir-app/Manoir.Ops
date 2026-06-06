using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class PluginRepositoryDeploymentLoaderTests
{
	[TestMethod]
	public void Load_ShouldReadManifestAndArtifactsFromRepositoryRoot()
	{
		string repositoryRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, "test-primary-key");
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, "AAECAwQFBgcICQoLDA0ODxAREhM=");
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, "12345678901234567890123456789012");

		try
		{
			Directory.CreateDirectory(repositoryRootPath);
			Directory.CreateDirectory(Path.Combine(repositoryRootPath, "deploy"));

			File.WriteAllText(Path.Combine(repositoryRootPath, PluginRepositoryDeploymentLoader.DefaultManifestFileName),
				"apiVersion: manoir/v1\n"
				+ "kind: PluginManifest\n"
				+ "plugin:\n"
				+ "  pluginId: sarah\n"
				+ "  repoUrl: https://github.com/manoir-app/manoir-plugin-sarah\n"
				+ "  displayName: Sarah Home Agent\n"
				+ "  publisher: MaNoir\n"
				+ "  version: 2.3.1\n"
				+ "  minimumMaNoirVersion: 1.8.0\n"
				+ "deployment:\n"
				+ "  adminUi:\n"
				+ "    pathPrefix: /sarah\n"
				+ "    port: 8080\n"
				+ "  artifacts:\n"
				+ "    - kind: compose\n"
				+ "      path: deploy/docker-compose.yml\n"
				+ "    - kind: env-template\n"
				+ "      path: deploy/.env.template\n");
			File.WriteAllText(Path.Combine(repositoryRootPath, "deploy", "docker-compose.yml"), "services:\n  sarah:\n    image: manoir/sarah:latest\n");
			File.WriteAllText(Path.Combine(repositoryRootPath, "deploy", ".env.template"), "SARAH_PLUGIN_ID=sarah\nSARAH_API_KEY=${{ secrets.SARAH_API_KEY }}\n");

			PluginDeploymentDescriptor descriptor = PluginRepositoryDeploymentLoader.Load(repositoryRootPath);

			Assert.AreEqual(Path.GetFullPath(repositoryRootPath), descriptor.RepositoryRootPath);
			Assert.AreEqual(Path.Combine(Path.GetFullPath(repositoryRootPath), PluginRepositoryDeploymentLoader.DefaultManifestFileName), descriptor.ManifestPath);
			Assert.AreEqual("/sarah", descriptor.AdminUiPathPrefix);
			Assert.AreEqual("sarah", descriptor.AdminUiServiceName);
			Assert.AreEqual(8080, descriptor.AdminUiServicePort);
			Assert.AreEqual(Path.Combine(Path.GetFullPath(repositoryRootPath), "deploy", "docker-compose.yml"), descriptor.ComposeArtifactFullPath);
			Assert.AreEqual(Path.Combine(Path.GetFullPath(repositoryRootPath), "deploy", ".env.template"), descriptor.EnvironmentTemplateFullPath);
			Assert.AreEqual(2, descriptor.EnvironmentVariables.Count);
			Assert.AreEqual(PluginEnvironmentValueKind.SecretReference, descriptor.EnvironmentVariables[1].ValueKind);
		}
		finally
		{
			if (Directory.Exists(repositoryRootPath))
				Directory.Delete(repositoryRootPath, true);
		}
	}

	[TestMethod]
	public void Load_ShouldRejectMissingComposeServiceWhenComposeDeclaresMultipleServices()
	{
		string repositoryRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, "test-primary-key");
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, "AAECAwQFBgcICQoLDA0ODxAREhM=");
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, "12345678901234567890123456789012");

		try
		{
			Directory.CreateDirectory(repositoryRootPath);
			Directory.CreateDirectory(Path.Combine(repositoryRootPath, "deploy"));

			File.WriteAllText(Path.Combine(repositoryRootPath, PluginRepositoryDeploymentLoader.DefaultManifestFileName),
				"apiVersion: manoir/v1\n"
				+ "kind: PluginManifest\n"
				+ "plugin:\n"
				+ "  pluginId: sarah\n"
				+ "  repoUrl: https://github.com/manoir-app/manoir-plugin-sarah\n"
				+ "  displayName: Sarah Home Agent\n"
				+ "  publisher: MaNoir\n"
				+ "  version: 2.3.1\n"
				+ "  minimumMaNoirVersion: 1.8.0\n"
				+ "deployment:\n"
				+ "  adminUi:\n"
				+ "    pathPrefix: /sarah\n"
				+ "    port: 8080\n"
				+ "  artifacts:\n"
				+ "    - kind: compose\n"
				+ "      path: deploy/docker-compose.yml\n");
			File.WriteAllText(Path.Combine(repositoryRootPath, "deploy", "docker-compose.yml"),
				"services:\n"
				+ "  web:\n"
				+ "    image: manoir/sarah-web:latest\n"
				+ "  worker:\n"
				+ "    image: manoir/sarah-worker:latest\n");

			InvalidOperationException exception = Assert.ThrowsException<InvalidOperationException>(() => PluginRepositoryDeploymentLoader.Load(repositoryRootPath));

			Assert.AreEqual("deployment.adminUi.composeService is required when the compose artifact declares multiple services.", exception.Message);
		}
		finally
		{
			if (Directory.Exists(repositoryRootPath))
				Directory.Delete(repositoryRootPath, true);
		}
	}

	[TestMethod]
	public void Load_ShouldRejectArtifactOutsideRepositoryRoot()
	{
		string baseRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		string repositoryRootPath = Path.Combine(baseRootPath, "repo");
		string outsideTemplatePath = Path.Combine(baseRootPath, "outside.env");
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, "test-primary-key");
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, "AAECAwQFBgcICQoLDA0ODxAREhM=");
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, "12345678901234567890123456789012");

		try
		{
			Directory.CreateDirectory(repositoryRootPath);
			File.WriteAllText(outsideTemplatePath, "SARAH_API_KEY=${{ secrets.SARAH_API_KEY }}\n");
			File.WriteAllText(Path.Combine(repositoryRootPath, PluginRepositoryDeploymentLoader.DefaultManifestFileName), @"
apiVersion: manoir/v1
kind: PluginManifest
plugin:
  pluginId: sarah
  repoUrl: https://github.com/manoir-app/manoir-plugin-sarah
  displayName: Sarah Home Agent
  publisher: MaNoir
  version: 2.3.1
  minimumMaNoirVersion: 1.8.0
deployment:
  artifacts:
    - kind: env-template
      path: ../outside.env
");

			InvalidOperationException exception = Assert.ThrowsException<InvalidOperationException>(() => PluginRepositoryDeploymentLoader.Load(repositoryRootPath));

			Assert.AreEqual("The env-template artifact resolves outside the repository root.", exception.Message);
		}
		finally
		{
			if (Directory.Exists(baseRootPath))
				Directory.Delete(baseRootPath, true);
		}
	}

	[TestMethod]
	public void Load_ShouldRejectOperationsWhenSecretRuntimeConfigurationIsMissing()
	{
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, null);
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, null);
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, null);

		PlatformOpsSecretsRuntimeConfigurationException exception = Assert.ThrowsException<PlatformOpsSecretsRuntimeConfigurationException>(() => PluginRepositoryDeploymentLoader.Load("C:\\temp\\does-not-matter"));

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "HOMEAUTOMATION_APIKEY is required for PlatformOps secret operations.");
		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "HOMEAUTOMATION_SECRETS_SALT is required for PlatformOps secret operations.");
		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "HOMEAUTOMATION_AUTH_JWT_SIGNING_KEY is required for PlatformOps secret operations.");
	}
}