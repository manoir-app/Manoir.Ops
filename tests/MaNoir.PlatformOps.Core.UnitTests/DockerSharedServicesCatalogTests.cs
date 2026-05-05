using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Docker.DotNet.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Provider.Docker;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class DockerSharedServicesCatalogTests
{
	[TestMethod]
	public void GetDataVolumeNames_ShouldReturnNamedDataVolumesForSharedServices()
	{
		IReadOnlyList<string> volumeNames = DockerSharedServicesCatalog.GetDataVolumeNames();

		Assert.AreEqual(2, volumeNames.Count);
		CollectionAssert.Contains(volumeNames.ToArray(), "manoir-shared-mongo");
		CollectionAssert.Contains(volumeNames.ToArray(), "manoir-shared-redis");
	}

	[TestMethod]
	public void GetDataVolumeNames_ShouldNotContainBindMountSources()
	{
		IReadOnlyList<string> volumeNames = DockerSharedServicesCatalog.GetDataVolumeNames();

		foreach (string volumeName in volumeNames)
		{
			Assert.IsFalse(volumeName.Contains('/'), "Volume name should not be a path: " + volumeName);
			Assert.IsFalse(volumeName.Contains('\\'), "Volume name should not be a path: " + volumeName);
		}
	}

	[TestMethod]
	public void CreateDeploymentPlan_ShouldProjectRequiredSharedServicesAndMosquittoConfig()
	{
		string sharedServicesRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		using EnvironmentVariableScope developmentInstanceScope = new EnvironmentVariableScope(DockerSharedServicesCatalog.DevelopmentInstanceEnvironmentVariableName, null);
		using EnvironmentVariableScope hostRootScope = new EnvironmentVariableScope(DockerSharedServicesCatalog.SharedServicesHostRootPathEnvironmentVariableName, null);
		using EnvironmentVariableScope mongoImageScope = new EnvironmentVariableScope(DockerSharedServicesCatalog.MongoImageEnvironmentVariableName, null);

		try
		{
			DockerDeploymentPlan plan = DockerSharedServicesCatalog.CreateDeploymentPlan(sharedServicesRootPath);

			Assert.AreEqual("shared-services", plan.PluginId);
			Assert.AreEqual("shared-services", plan.DeploymentGroup);
			Assert.AreEqual(4, plan.Services.Count);
			CollectionAssert.AreEqual(new[] { "mongo", "nats", "mqtt", "redis" }, plan.Services.Select(service => service.Name).ToArray());
			Assert.AreEqual(DockerSharedServicesCatalog.DefaultMongoImage, plan.Services[0].Image);
			Assert.AreEqual("nats:2.14.0", plan.Services[1].Image);
			Assert.AreEqual("eclipse-mosquitto:2", plan.Services[2].Image);
			Assert.IsTrue(plan.Services.All(service => service.ImagePullPolicy == DockerImagePullPolicy.Always));
			CollectionAssert.AreEqual(new[] { "1883:1883" }, plan.Services[2].Ports.ToArray());
			Assert.AreEqual(0, plan.Services[0].Ports.Count);
			Assert.AreEqual(0, plan.Services[1].Ports.Count);
			Assert.IsTrue(File.Exists(Path.Combine(sharedServicesRootPath, "mqtt", "config", "mosquitto.conf")));
		}
		finally
		{
			if (Directory.Exists(sharedServicesRootPath))
				Directory.Delete(sharedServicesRootPath, true);
		}
	}

	[TestMethod]
	public void CreateDeploymentPlan_ShouldExposeMongoAndNatsPortsForDevelopmentInstance()
	{
		string sharedServicesRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		using EnvironmentVariableScope developmentInstanceScope = new EnvironmentVariableScope(DockerSharedServicesCatalog.DevelopmentInstanceEnvironmentVariableName, "true");
		using EnvironmentVariableScope hostRootScope = new EnvironmentVariableScope(DockerSharedServicesCatalog.SharedServicesHostRootPathEnvironmentVariableName, null);
		using EnvironmentVariableScope mongoImageScope = new EnvironmentVariableScope(DockerSharedServicesCatalog.MongoImageEnvironmentVariableName, null);

		try
		{
			DockerDeploymentPlan plan = DockerSharedServicesCatalog.CreateDeploymentPlan(sharedServicesRootPath);

			CollectionAssert.AreEqual(new[] { "27017:27017" }, plan.Services.Single(service => service.Name == "mongo").Ports.ToArray());
			CollectionAssert.AreEqual(new[] { "4222:4222" }, plan.Services.Single(service => service.Name == "nats").Ports.ToArray());
			CollectionAssert.AreEqual(new[] { "1883:1883" }, plan.Services.Single(service => service.Name == "mqtt").Ports.ToArray());
		}
		finally
		{
			if (Directory.Exists(sharedServicesRootPath))
				Directory.Delete(sharedServicesRootPath, true);
		}
	}

	[TestMethod]
	public void CreateDeploymentPlan_ShouldUseConfiguredMongoImageOverride()
	{
		string sharedServicesRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		using EnvironmentVariableScope developmentInstanceScope = new EnvironmentVariableScope(DockerSharedServicesCatalog.DevelopmentInstanceEnvironmentVariableName, null);
		using EnvironmentVariableScope hostRootScope = new EnvironmentVariableScope(DockerSharedServicesCatalog.SharedServicesHostRootPathEnvironmentVariableName, null);
		using EnvironmentVariableScope mongoImageScope = new EnvironmentVariableScope(DockerSharedServicesCatalog.MongoImageEnvironmentVariableName, "mongo:4.4.18");

		try
		{
			DockerDeploymentPlan plan = DockerSharedServicesCatalog.CreateDeploymentPlan(sharedServicesRootPath);

			Assert.AreEqual("mongo:4.4.18", plan.Services.Single(service => service.Name == "mongo").Image);
		}
		finally
		{
			if (Directory.Exists(sharedServicesRootPath))
				Directory.Delete(sharedServicesRootPath, true);
		}
	}

	[TestMethod]
	public void CreateDeploymentPlan_ShouldUseConfiguredDockerHostRootPathForMqttMounts()
	{
		string sharedServicesRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		string dockerHostRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "host-root");
		using EnvironmentVariableScope developmentInstanceScope = new EnvironmentVariableScope(DockerSharedServicesCatalog.DevelopmentInstanceEnvironmentVariableName, null);
		using EnvironmentVariableScope hostRootScope = new EnvironmentVariableScope(DockerSharedServicesCatalog.SharedServicesHostRootPathEnvironmentVariableName, dockerHostRootPath);

		try
		{
			DockerDeploymentPlan plan = DockerSharedServicesCatalog.CreateDeploymentPlan(sharedServicesRootPath);
			DockerDeploymentServicePlan mqttPlan = plan.Services.Single(service => service.Name == "mqtt");

			CollectionAssert.AreEqual(
				new[]
				{
					Path.Combine(dockerHostRootPath, "mqtt", "config") + ":/mosquitto/config:ro",
					Path.Combine(dockerHostRootPath, "mqtt", "data") + ":/mosquitto/data",
					Path.Combine(dockerHostRootPath, "mqtt", "log") + ":/mosquitto/log"
				},
				mqttPlan.Volumes.ToArray());
			Assert.IsTrue(File.Exists(Path.Combine(sharedServicesRootPath, "mqtt", "config", "mosquitto.conf")));
		}
		finally
		{
			if (Directory.Exists(sharedServicesRootPath))
				Directory.Delete(sharedServicesRootPath, true);
		}
	}

	[TestMethod]
	public void ResolveDockerHostHomeAutomationRootPath_ShouldReturnParentOfWindowsSharedServicesPath()
	{
		using EnvironmentVariableScope hostRootScope = new EnvironmentVariableScope(DockerSharedServicesCatalog.SharedServicesHostRootPathEnvironmentVariableName, @"C:\ProgramData\MaNoir\home-automation\shared-services");

		string homeAutomationRootPath = DockerSharedServicesCatalog.ResolveDockerHostHomeAutomationRootPath("/home-automation/shared-services");

		Assert.AreEqual(@"C:\ProgramData\MaNoir\home-automation", homeAutomationRootPath);
	}

	[TestMethod]
	public void Evaluate_ShouldReportMissingAndRunningSharedServices()
	{
		using EnvironmentVariableScope mongoImageScope = new EnvironmentVariableScope(DockerSharedServicesCatalog.MongoImageEnvironmentVariableName, null);

		ContainerListResponse[] containers =
		[
			new ContainerListResponse()
			{
				Names = ["/manoir-shared-mongo"],
				Image = DockerSharedServicesCatalog.DefaultMongoImage,
				State = "running"
			},
			new ContainerListResponse()
			{
				Names = ["/manoir-shared-nats"],
				Image = "nats:2.13.1",
				State = "running"
			},
			new ContainerListResponse()
			{
				Names = ["/manoir-shared-redis"],
				Image = "redis:7.4",
				State = "exited"
			}
		];

		IReadOnlyList<DockerSharedServiceStatus> statuses = DockerSharedServicesCatalog.Evaluate(containers, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

		Assert.AreEqual(4, statuses.Count);
		Assert.IsTrue(statuses.Single(service => service.ServiceName == "mongo").IsRunning);
		Assert.IsFalse(statuses.Single(service => service.ServiceName == "nats").MatchesExpectedImage);
		Assert.IsFalse(statuses.Single(service => service.ServiceName == "mqtt").IsPresent);
		Assert.IsFalse(statuses.Single(service => service.ServiceName == "redis").IsRunning);
	}
}