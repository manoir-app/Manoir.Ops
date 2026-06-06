using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;
using MaNoir.PlatformOps.Provider.Docker;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class DockerDeploymentPlanFactoryTests
{
	[TestMethod]
	public void Create_ShouldProjectPlanFromRepositoryDescriptor()
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
				+ "  group: home-automation\n"
				+ "  artifacts:\n"
				+ "    - kind: compose\n"
				+ "      path: deploy/docker-compose.yml\n"
				+ "    - kind: env-template\n"
				+ "      path: deploy/.env.template\n");
			File.WriteAllText(Path.Combine(repositoryRootPath, "deploy", "docker-compose.yml"), @"
services:
  api:
    image: manoir/sarah-api:2.3.1
    ports:
      - 8080:8080
    environment:
      SARAH_PLUGIN_ID: ${SARAH_PLUGIN_ID}
  worker:
    build: .
    depends_on:
      api:
        condition: service_started
");
			File.WriteAllText(Path.Combine(repositoryRootPath, "deploy", ".env.template"), "SARAH_PLUGIN_ID=sarah\nSARAH_API_KEY=${{ secrets.SARAH_API_KEY }}\n");

			PluginDeploymentDescriptor descriptor = PluginRepositoryDeploymentLoader.Load(repositoryRootPath);
			DockerDeploymentPlan plan = DockerDeploymentPlanFactory.Create(descriptor);

			Assert.AreEqual("sarah", plan.PluginId);
			Assert.AreEqual("home-automation", plan.DeploymentGroup);
			Assert.AreEqual(2, plan.SharedEnvironmentVariables.Count);
			Assert.AreEqual(2, plan.Services.Count);
			Assert.AreEqual("api", plan.Services[0].Name);
			Assert.AreEqual("manoir/sarah-api:2.3.1", plan.Services[0].Image);
			Assert.AreEqual(DockerImagePullPolicy.Always, plan.Services[0].ImagePullPolicy);
			Assert.AreEqual(DockerImagePullPolicy.Always, plan.Services[1].ImagePullPolicy);
			Assert.AreEqual("api", plan.Services[1].DependsOn[0]);
		}
		finally
		{
			if (Directory.Exists(repositoryRootPath))
				Directory.Delete(repositoryRootPath, true);
		}
	}

	[TestMethod]
	public void Create_ShouldRejectDescriptorWithoutComposePath()
	{
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, "test-primary-key");
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, "AAECAwQFBgcICQoLDA0ODxAREhM=");
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, "12345678901234567890123456789012");

		ArgumentException exception = Assert.ThrowsException<ArgumentException>(() => DockerDeploymentPlanFactory.Create(new PluginDeploymentDescriptor()
		{
			PluginId = "sarah"
		}));

		Assert.AreEqual("The deployment descriptor does not declare a resolved Docker Compose artifact path. (Parameter 'descriptor')", exception.Message);
	}

	[TestMethod]
	public void Create_ShouldRejectOperationsWhenSecretRuntimeConfigurationIsMissing()
	{
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, null);
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, null);
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, null);

		PlatformOpsSecretsRuntimeConfigurationException exception = Assert.ThrowsException<PlatformOpsSecretsRuntimeConfigurationException>(() => DockerDeploymentPlanFactory.Create(new PluginDeploymentDescriptor()
		{
			PluginId = "sarah",
			ComposeArtifactFullPath = "compose.yml"
		}));

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "HOMEAUTOMATION_APIKEY is required for PlatformOps secret operations.");
		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "HOMEAUTOMATION_SECRETS_SALT is required for PlatformOps secret operations.");
		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "HOMEAUTOMATION_AUTH_JWT_SIGNING_KEY is required for PlatformOps secret operations.");
	}

	[TestMethod]
	public async Task CreateAsync_ShouldResolveSharedSecretsIntoPlan()
	{
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, "test-primary-key");
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, "AAECAwQFBgcICQoLDA0ODxAREhM=");
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, "12345678901234567890123456789012");

		PluginDeploymentDescriptor descriptor = new PluginDeploymentDescriptor()
		{
			PluginId = "sarah",
			ComposeArtifactFullPath = "compose.yml",
			EnvironmentVariables =
			[
				new PluginEnvironmentVariable() { Name = "PLUGIN_ID", ValueKind = PluginEnvironmentValueKind.Literal, LiteralValue = "sarah" },
				new PluginEnvironmentVariable() { Name = "API_KEY", ValueKind = PluginEnvironmentValueKind.SecretReference, SecretName = "SARAH_API_KEY" }
			]
		};

		DockerComposeFile composeFile = DockerComposeParser.Parse("services:\n  api:\n    image: manoir/sarah-api:2.3.1\n    environment:\n      PLUGIN_ID: ${PLUGIN_ID}\n      API_KEY: ${API_KEY}\n      MQTT_HOST: broker.internal\n");

		DockerDeploymentPlan plan = await DockerDeploymentPlanFactory.CreateAsync(
			descriptor,
			composeFile,
			(secretName, cancellationToken) => Task.FromResult(secretName == "SARAH_API_KEY" ? "resolved-secret" : null),
			default);

		Assert.AreEqual(2, plan.ResolvedSharedEnvironmentVariables.Count);
		Assert.AreEqual("sarah", plan.ResolvedSharedEnvironmentVariables[0].Value);
		Assert.AreEqual(PluginEnvironmentValueKind.SecretReference, plan.ResolvedSharedEnvironmentVariables[1].ValueKind);
		Assert.AreEqual("resolved-secret", plan.ResolvedSharedEnvironmentVariables[1].Value);
		Assert.AreEqual(3, plan.Services[0].ResolvedEnvironment.Count);
		Assert.AreEqual("sarah", plan.Services[0].ResolvedEnvironment[0].Value);
		Assert.AreEqual("resolved-secret", plan.Services[0].ResolvedEnvironment[1].Value);
		Assert.AreEqual("broker.internal", plan.Services[0].ResolvedEnvironment[2].Value);
	}

	[TestMethod]
	public async Task CreateAsync_ShouldResolveAutomaticPlatformVariablesInComposeEnvironment()
	{
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, "test-primary-key");
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, "AAECAwQFBgcICQoLDA0ODxAREhM=");
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, "12345678901234567890123456789012");

		PluginDeploymentDescriptor descriptor = new PluginDeploymentDescriptor()
		{
			PluginId = "sarah",
			ComposeArtifactFullPath = "compose.yml",
			EnvironmentVariables = Array.Empty<PluginEnvironmentVariable>()
		};

		DockerComposeFile composeFile = DockerComposeParser.Parse(@"
services:
  api:
    image: manoir/sarah-api:2.3.1
    environment:
      MONGO_HOST: ${MONGODB_SERVICE_HOST}
      REDIS_PORT: ${REDIS_SERVICE_PORT}
");

		DockerDeploymentPlan plan = await DockerDeploymentPlanFactory.CreateAsync(
			descriptor,
			composeFile,
			(secretName, cancellationToken) => Task.FromResult<string>(null),
			default);

		Assert.AreEqual(2, plan.Services[0].ResolvedEnvironment.Count);
		Assert.AreEqual("mongo", plan.Services[0].ResolvedEnvironment[0].Value);
		Assert.AreEqual("6379", plan.Services[0].ResolvedEnvironment[1].Value);
	}

	[TestMethod]
	public async Task CreateAsync_ShouldRejectWhenComposeReferencesMissingVariable()
	{
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, "test-primary-key");
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, "AAECAwQFBgcICQoLDA0ODxAREhM=");
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, "12345678901234567890123456789012");

		PluginDeploymentDescriptor descriptor = new PluginDeploymentDescriptor()
		{
			PluginId = "sarah",
			ComposeArtifactFullPath = "compose.yml",
			EnvironmentVariables =
			[
				new PluginEnvironmentVariable() { Name = "PLUGIN_ID", ValueKind = PluginEnvironmentValueKind.Literal, LiteralValue = "sarah" }
			]
		};

		DockerComposeFile composeFile = DockerComposeParser.Parse(@"
services:
  api:
    image: manoir/sarah-api:2.3.1
    environment:
      API_KEY: ${API_KEY}
");

		DockerComposeEnvironmentResolutionException exception = await Assert.ThrowsExceptionAsync<DockerComposeEnvironmentResolutionException>(() => DockerDeploymentPlanFactory.CreateAsync(
			descriptor,
			composeFile,
			(secretName, cancellationToken) => Task.FromResult<string>(null),
			default));

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "services.api.environment.API_KEY references missing variable 'API_KEY'.");
	}

	[TestMethod]
	public void Create_ShouldFallbackDeploymentGroupToPluginId()
	{
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, "test-primary-key");
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, "AAECAwQFBgcICQoLDA0ODxAREhM=");
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, "12345678901234567890123456789012");

		DockerDeploymentPlan plan = DockerDeploymentPlanFactory.Create(new PluginDeploymentDescriptor()
		{
			PluginId = "sarah",
			ComposeArtifactFullPath = "compose.yml"
		}, DockerComposeParser.Parse("services:\n  api:\n    image: manoir/sarah-api:2.3.1\n"));

		Assert.AreEqual("sarah", plan.DeploymentGroup);
	}

	[TestMethod]
	public void Create_ShouldAddTraefikLabelsToAdminUiService()
	{
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, "test-primary-key");
		using EnvironmentVariableScope saltScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.SecretsSaltEnvironmentVariableName, "AAECAwQFBgcICQoLDA0ODxAREhM=");
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, "12345678901234567890123456789012");

		DockerDeploymentPlan plan = DockerDeploymentPlanFactory.Create(new PluginDeploymentDescriptor()
		{
			PluginId = "sarah",
			ComposeArtifactFullPath = "compose.yml",
			AdminUiPathPrefix = "/sarah",
			AdminUiServiceName = "api",
			AdminUiServicePort = 8080
		}, DockerComposeParser.Parse(@"
services:
  api:
    image: manoir/sarah-api:2.3.1
  worker:
    image: manoir/sarah-worker:2.3.1
"));

		Assert.AreEqual("true", plan.Services[0].Labels["traefik.enable"]);
		Assert.AreEqual(DockerRuntimeSpecFactory.SharedNetworkName, plan.Services[0].Labels["traefik.docker.network"]);
		Assert.AreEqual("PathPrefix(`/sarah`)", plan.Services[0].Labels["traefik.http.routers.sarah-api-admin-ui.rule"]);
		Assert.AreEqual("sarah-api-admin-ui", plan.Services[0].Labels["traefik.http.routers.sarah-api-admin-ui.service"]);
		Assert.AreEqual("8080", plan.Services[0].Labels["traefik.http.services.sarah-api-admin-ui.loadbalancer.server.port"]);
		Assert.AreEqual(0, plan.Services[1].Labels.Count);
	}
}