using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Docker.DotNet.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Provider.Docker;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class DockerCoreServiceCatalogTests
{
	[TestMethod]
	public void CreateDeploymentPlan_ShouldUseLatestTagAndHostPort81ByDefault()
	{
		string rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		using EnvironmentVariableScope developmentScope = new EnvironmentVariableScope(DockerPlatformRuntimeEnvironment.DevelopmentInstanceEnvironmentVariableName, null);
		using EnvironmentVariableScope coreHostPortScope = new EnvironmentVariableScope(DockerCoreServiceCatalog.CoreAdminUiHostPortEnvironmentVariableName, null);

		DockerDeploymentPlan plan = DockerCoreServiceCatalog.CreateDeploymentPlan(rootPath);

		Assert.AreEqual("core", plan.PluginId);
		Assert.AreEqual("core", plan.DeploymentGroup);
		Assert.AreEqual(1, plan.Services.Count);
		Assert.AreEqual("core", plan.Services[0].ContainerName);
		Assert.AreEqual("ghcr.io/manoir-app/manoir-core-adminui:latest", plan.Services[0].Image);
		Assert.AreEqual("true", plan.Services[0].Labels["traefik.enable"]);
		Assert.AreEqual("PathPrefix(`/platform`)", plan.Services[0].Labels["traefik.http.routers.platform-core-admin-ui.rule"]);
		Assert.AreEqual("platform-core-admin-ui", plan.Services[0].Labels["traefik.http.routers.platform-core-admin-ui.service"]);
		Assert.AreEqual("8080", plan.Services[0].Labels["traefik.http.services.platform-core-admin-ui.loadbalancer.server.port"]);
		Assert.AreEqual(DockerImagePullPolicy.Always, plan.Services[0].ImagePullPolicy);
		CollectionAssert.AreEqual(new[] { "81:8080" }, plan.Services[0].Ports.ToArray());
	}

	[TestMethod]
	public void CreateDeploymentPlan_ShouldUseDevTagForDevelopmentInstance()
	{
		string rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		using EnvironmentVariableScope developmentScope = new EnvironmentVariableScope(DockerPlatformRuntimeEnvironment.DevelopmentInstanceEnvironmentVariableName, "true");
		using EnvironmentVariableScope coreHostPortScope = new EnvironmentVariableScope(DockerCoreServiceCatalog.CoreAdminUiHostPortEnvironmentVariableName, null);

		DockerDeploymentPlan plan = DockerCoreServiceCatalog.CreateDeploymentPlan(rootPath);

		Assert.AreEqual("ghcr.io/manoir-app/manoir-core-adminui:dev", plan.Services[0].Image);
	}

	[TestMethod]
	public void CreateDeploymentPlan_ShouldUseConfiguredHostPortWhenEnvironmentVariableIsPresent()
	{
		string rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		using EnvironmentVariableScope developmentScope = new EnvironmentVariableScope(DockerPlatformRuntimeEnvironment.DevelopmentInstanceEnvironmentVariableName, null);
		using EnvironmentVariableScope coreHostPortScope = new EnvironmentVariableScope(DockerCoreServiceCatalog.CoreAdminUiHostPortEnvironmentVariableName, "18081");

		DockerDeploymentPlan plan = DockerCoreServiceCatalog.CreateDeploymentPlan(rootPath);

		CollectionAssert.AreEqual(new[] { "18081:8080" }, plan.Services[0].Ports.ToArray());
	}

	[TestMethod]
	public void CreateDeploymentPlan_ShouldPreferPlatformCatalogDefinitionWhenAvailable()
	{
		string homeAutomationRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		string sharedServicesRootPath = Path.Combine(homeAutomationRootPath, "shared-services");
		string pluginDefinitionRootPath = Path.Combine(homeAutomationRootPath, "plugins", "_managed", "manoir-plugincatalog", "plugins", "Core", "MainServices", "Platform");
		using EnvironmentVariableScope developmentScope = new EnvironmentVariableScope(DockerPlatformRuntimeEnvironment.DevelopmentInstanceEnvironmentVariableName, null);
		using EnvironmentVariableScope coreHostPortScope = new EnvironmentVariableScope(DockerCoreServiceCatalog.CoreAdminUiHostPortEnvironmentVariableName, null);

		try
		{
			Directory.CreateDirectory(sharedServicesRootPath);
			Directory.CreateDirectory(Path.Combine(pluginDefinitionRootPath, "deploy"));
			File.WriteAllText(Path.Combine(pluginDefinitionRootPath, "plugin.yaml"), @"
apiVersion: manoir/v1
kind: AvailablePlugin
repoUrl: https://github.com/manoir-app/MaNoir.Platform
displayName: MaNoir Platform Core
version: 0.0.0-dev
minimumMaNoirVersion: 0.0.0
deployment:
  group: platform
  adminUi:
    pathPrefix: /platform
    composeService: core
    port: 8080
  artifacts:
    - kind: compose
      path: deploy/docker-compose.yml
");
			File.WriteAllText(Path.Combine(pluginDefinitionRootPath, "deploy", "docker-compose.yml"), @"
services:
  core:
    image: ghcr.io/manoir-app/manoir-core-adminui:latest
    container_name: manoir-platform-core
    environment:
      MANOIR_ADMINUI_PUBLIC_BASE_PATH: /platform
");

			DockerDeploymentPlan plan = DockerCoreServiceCatalog.CreateDeploymentPlan(sharedServicesRootPath);

			Assert.AreEqual("platform", plan.PluginId);
			Assert.AreEqual("platform", plan.DeploymentGroup);
			Assert.AreEqual("manoir-platform-core", plan.Services[0].ContainerName);
			Assert.AreEqual("ghcr.io/manoir-app/manoir-core-adminui:latest", plan.Services[0].Image);
			Assert.AreEqual("true", plan.Services[0].Labels["traefik.enable"]);
			Assert.AreEqual("PathPrefix(`/platform`)", plan.Services[0].Labels["traefik.http.routers.platform-core-admin-ui.rule"]);
			Assert.AreEqual(0, plan.Services[0].Ports.Count);
		}
		finally
		{
			if (Directory.Exists(homeAutomationRootPath))
				Directory.Delete(homeAutomationRootPath, true);
		}
	}

	[TestMethod]
	public void Evaluate_ShouldReportCoreState()
	{
		using EnvironmentVariableScope developmentScope = new EnvironmentVariableScope(DockerPlatformRuntimeEnvironment.DevelopmentInstanceEnvironmentVariableName, null);
		using EnvironmentVariableScope coreHostPortScope = new EnvironmentVariableScope(DockerCoreServiceCatalog.CoreAdminUiHostPortEnvironmentVariableName, null);

		IReadOnlyList<DockerSharedServiceStatus> statuses = DockerCoreServiceCatalog.Evaluate(
			[
				new ContainerListResponse()
				{
					Names = ["/core"],
					Image = "ghcr.io/manoir-app/manoir-core-adminui:latest",
					State = "running"
				}
			],
			Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

		Assert.AreEqual(1, statuses.Count);
		Assert.IsTrue(statuses[0].IsRunning);
		Assert.IsTrue(statuses[0].MatchesExpectedImage);
	}

	[TestMethod]
	public void GetPlatformCorePluginAvailabilityError_ShouldReportMissingCatalogDefinition()
	{
		string homeAutomationRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		string sharedServicesRootPath = Path.Combine(homeAutomationRootPath, "shared-services");

		try
		{
			Directory.CreateDirectory(sharedServicesRootPath);

			string error = DockerCoreServiceCatalog.GetPlatformCorePluginAvailabilityError(sharedServicesRootPath);

			Assert.AreEqual("Platform Core plugin definition was not found because the plugin repository root '" + Path.Combine(homeAutomationRootPath, "plugins") + "' does not exist.", error);
		}
		finally
		{
			if (Directory.Exists(homeAutomationRootPath))
				Directory.Delete(homeAutomationRootPath, true);
		}
	}
}