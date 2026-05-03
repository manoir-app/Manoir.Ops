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
}