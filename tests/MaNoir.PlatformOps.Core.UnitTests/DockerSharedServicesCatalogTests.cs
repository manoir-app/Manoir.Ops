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
		using EnvironmentVariableScope apiKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName, "test-primary-key");

		try
		{
			DockerDeploymentPlan plan = DockerSharedServicesCatalog.CreateDeploymentPlan(sharedServicesRootPath);
			DockerDeploymentServicePlan loki = plan.Services.Single(service => service.Name == "loki");
			DockerDeploymentServicePlan tempo = plan.Services.Single(service => service.Name == "tempo");
			DockerDeploymentServicePlan prometheus = plan.Services.Single(service => service.Name == "prometheus");
			DockerDeploymentServicePlan grafana = plan.Services.Single(service => service.Name == "grafana");

			Assert.AreEqual("shared-services", plan.PluginId);
			Assert.AreEqual("shared-services", plan.DeploymentGroup);
			Assert.AreEqual(9, plan.Services.Count);
			CollectionAssert.AreEqual(new[] { "mongo", "nats", "mqtt", "redis", "traefik", "loki", "tempo", "prometheus", "grafana" }, plan.Services.Select(service => service.Name).ToArray());
			CollectionAssert.AreEqual(new[] { "mongo", "nats", "mqtt", "redis", "traefik" }, plan.Services.Where(service => service.IsRequiredForMinimumVital).Select(service => service.Name).ToArray());
			Assert.AreEqual(DockerSharedServicesCatalog.DefaultMongoImage, plan.Services[0].Image);
			Assert.AreEqual("nats:2.14.0", plan.Services[1].Image);
			Assert.AreEqual("eclipse-mosquitto:2", plan.Services[2].Image);
			Assert.AreEqual(DockerSharedServicesCatalog.DefaultTraefikImage, plan.Services[4].Image);
			Assert.AreEqual(DockerSharedServicesCatalog.DefaultLokiImage, plan.Services[5].Image);
			Assert.AreEqual(DockerSharedServicesCatalog.DefaultTempoImage, plan.Services[6].Image);
			Assert.AreEqual(DockerSharedServicesCatalog.DefaultPrometheusImage, plan.Services[7].Image);
			Assert.AreEqual(DockerSharedServicesCatalog.DefaultGrafanaImage, plan.Services[8].Image);
			CollectionAssert.Contains(loki.Volumes.ToArray(), Path.Combine(sharedServicesRootPath, "loki", "config", "loki.yml") + ":/etc/loki/local-config.yaml:ro");
			CollectionAssert.Contains(tempo.Volumes.ToArray(), Path.Combine(sharedServicesRootPath, "tempo", "config", "tempo.yml") + ":/etc/tempo.yaml:ro");
			Assert.AreEqual(0, loki.Labels.Count);
			Assert.AreEqual(0, tempo.Labels.Count);
			Assert.AreEqual(0, prometheus.Labels.Count);
			Assert.AreEqual(0, grafana.Labels.Count);
			Assert.AreEqual("manoir", grafana.Environment.Single(entry => entry.Name == "GF_SECURITY_ADMIN_USER").Value);
			Assert.AreEqual("test-primary-key", grafana.Environment.Single(entry => entry.Name == "GF_SECURITY_ADMIN_PASSWORD").Value);
			Assert.AreEqual("false", grafana.Environment.Single(entry => entry.Name == "GF_AUTH_ANONYMOUS_ENABLED").Value);
			Assert.IsFalse(grafana.Environment.Any(entry => entry.Name == "GF_SERVER_ROOT_URL"));
			Assert.IsFalse(grafana.Environment.Any(entry => entry.Name == "GF_SERVER_SERVE_FROM_SUB_PATH"));
			Assert.IsTrue(plan.Services.All(service => service.ImagePullPolicy == DockerImagePullPolicy.Always));
			CollectionAssert.AreEqual(new[] { "1883:1883" }, plan.Services[2].Ports.ToArray());
			CollectionAssert.AreEqual(new[] { "80" }, plan.Services[4].Ports.ToArray());
			Assert.AreEqual(0, plan.Services[0].Ports.Count);
			Assert.AreEqual(0, plan.Services[1].Ports.Count);
			Assert.IsTrue(File.Exists(Path.Combine(sharedServicesRootPath, "mqtt", "config", "mosquitto.conf")));
			Assert.IsTrue(File.Exists(Path.Combine(sharedServicesRootPath, "traefik", "config", "traefik.yml")));
			Assert.IsTrue(File.Exists(Path.Combine(sharedServicesRootPath, "loki", "config", "loki.yml")));
			Assert.IsTrue(File.Exists(Path.Combine(sharedServicesRootPath, "tempo", "config", "tempo.yml")));
			Assert.IsTrue(File.Exists(Path.Combine(sharedServicesRootPath, "prometheus", "config", "prometheus.yml")));
			Assert.IsTrue(File.Exists(Path.Combine(sharedServicesRootPath, "grafana", "provisioning", "datasources", "datasources.yml")));
			Assert.IsTrue(Directory.Exists(Path.Combine(sharedServicesRootPath, "loki", "data")));
			Assert.IsTrue(Directory.Exists(Path.Combine(sharedServicesRootPath, "tempo", "data")));
			Assert.IsTrue(Directory.Exists(Path.Combine(sharedServicesRootPath, "prometheus", "data")));
			Assert.IsTrue(Directory.Exists(Path.Combine(sharedServicesRootPath, "grafana", "data")));
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
			CollectionAssert.AreEqual(new[] { "80" }, plan.Services.Single(service => service.Name == "traefik").Ports.ToArray());
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

		Assert.AreEqual(9, statuses.Count);
		CollectionAssert.AreEqual(new[] { "mongo", "nats", "mqtt", "redis", "traefik" }, statuses.Where(service => service.IsRequiredForMinimumVital).Select(service => service.ServiceName).ToArray());
		Assert.IsTrue(statuses.Single(service => service.ServiceName == "mongo").IsRunning);
		Assert.IsFalse(statuses.Single(service => service.ServiceName == "nats").MatchesExpectedImage);
		Assert.IsFalse(statuses.Single(service => service.ServiceName == "mqtt").IsPresent);
		Assert.IsFalse(statuses.Single(service => service.ServiceName == "redis").IsRunning);
		Assert.IsFalse(statuses.Single(service => service.ServiceName == "traefik").IsPresent);
		Assert.IsFalse(statuses.Single(service => service.ServiceName == "loki").IsPresent);
		Assert.IsFalse(statuses.Single(service => service.ServiceName == "tempo").IsPresent);
		Assert.IsFalse(statuses.Single(service => service.ServiceName == "prometheus").IsPresent);
		Assert.IsFalse(statuses.Single(service => service.ServiceName == "grafana").IsPresent);
	}
}