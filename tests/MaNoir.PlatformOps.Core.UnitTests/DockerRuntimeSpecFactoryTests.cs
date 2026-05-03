using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;
using MaNoir.PlatformOps.Provider.Docker;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class DockerRuntimeSpecFactoryTests
{
	[TestMethod]
	public void Create_ShouldAllocateFirstAvailableHostPorts()
	{
		DockerDeploymentPlan plan = new DockerDeploymentPlan()
		{
			PluginId = "sarah",
			RepositoryRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
			ComposeFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "deploy", "docker-compose.yml"),
			Services =
			[
				new DockerDeploymentServicePlan()
				{
					Name = "api",
					Image = "manoir/sarah-api:1.0.0",
					Ports = ["8080"],
					ResolvedEnvironment = []
				},
				new DockerDeploymentServicePlan()
				{
					Name = "worker",
					Image = "manoir/sarah-worker:1.0.0",
					Ports = ["8080/tcp"],
					ResolvedEnvironment = []
				}
			]
		};

		DockerRuntimeSpec spec = DockerRuntimeSpecFactory.Create(plan, new HashSet<int>() { 8080, 8081 });

		Assert.AreEqual(2, spec.Services.Count);
		Assert.AreEqual("sarah-api", spec.Services[0].ContainerName);
		CollectionAssert.AreEqual(new[] { "sarah-api", "api" }, spec.Services[0].NetworkAliases.ToArray());
		Assert.AreEqual(8082, spec.Services[0].PortBindings[0].HostPort);
		Assert.AreEqual(8080, spec.Services[0].PortBindings[0].ContainerPort);
		Assert.AreEqual(8083, spec.Services[1].PortBindings[0].HostPort);
	}

	[TestMethod]
	public void Create_ShouldRejectReservedExplicitHostPort()
	{
		DockerDeploymentPlan plan = new DockerDeploymentPlan()
		{
			PluginId = "sarah",
			Services =
			[
				new DockerDeploymentServicePlan()
				{
					Name = "api",
					Image = "manoir/sarah-api:1.0.0",
					Ports = ["8081:8080"],
					ResolvedEnvironment = []
				}
			]
		};

		DockerRuntimePlanningException exception = Assert.ThrowsException<DockerRuntimePlanningException>(() => DockerRuntimeSpecFactory.Create(plan, new HashSet<int>() { 8081 }));

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "services.api.ports[0] requires host port '8081', but it is already reserved.");
	}

	[TestMethod]
	public void Create_ShouldResolveRelativeBindMountsAndNamedVolumes()
	{
		string repositoryRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		string composeFilePath = Path.Combine(repositoryRootPath, "deploy", "docker-compose.yml");
		using EnvironmentVariableScope homeAutomationRootScope = new EnvironmentVariableScope(DockerSharedServicesCatalog.SharedServicesHostRootPathEnvironmentVariableName, @"C:\ProgramData\MaNoir\home-automation\shared-services");

		DockerDeploymentPlan plan = new DockerDeploymentPlan()
		{
			PluginId = "sarah",
			RepositoryRootPath = repositoryRootPath,
			ComposeFilePath = composeFilePath,
			Services =
			[
				new DockerDeploymentServicePlan()
				{
					Name = "api",
					Image = "manoir/sarah-api:1.0.0",
					Volumes = ["./data:/var/lib/sarah:ro", "cache:/cache"],
					ResolvedEnvironment = []
				}
			]
		};

		DockerRuntimeSpec spec = DockerRuntimeSpecFactory.Create(plan, Array.Empty<int>());

		Assert.AreEqual(3, spec.Services[0].Mounts.Count);
		Assert.AreEqual(DockerRuntimeMountKind.Bind, spec.Services[0].Mounts[0].Kind);
		Assert.AreEqual(Path.GetFullPath(Path.Combine(repositoryRootPath, "deploy", "data")), spec.Services[0].Mounts[0].Source);
		Assert.AreEqual("/var/lib/sarah", spec.Services[0].Mounts[0].Target);
		Assert.IsTrue(spec.Services[0].Mounts[0].IsReadOnly);
		Assert.AreEqual(DockerRuntimeMountKind.Volume, spec.Services[0].Mounts[1].Kind);
		Assert.AreEqual("cache", spec.Services[0].Mounts[1].Source);
		Assert.AreEqual(DockerRuntimeMountKind.Bind, spec.Services[0].Mounts[2].Kind);
		Assert.AreEqual(@"C:\ProgramData\MaNoir\home-automation", spec.Services[0].Mounts[2].Source);
		Assert.AreEqual(DockerSharedServicesCatalog.HomeAutomationRootContainerPath, spec.Services[0].Mounts[2].Target);
	}

	[TestMethod]
	public void Create_ShouldRejectBuildContextBecauseExecutorOnlySupportsImages()
	{
		DockerDeploymentPlan plan = new DockerDeploymentPlan()
		{
			PluginId = "sarah",
			Services =
			[
				new DockerDeploymentServicePlan()
				{
					Name = "worker",
					Image = "manoir/sarah-worker:1.0.0",
					BuildContext = ".",
					ResolvedEnvironment = []
				}
			]
		};

		DockerRuntimePlanningException exception = Assert.ThrowsException<DockerRuntimePlanningException>(() => DockerRuntimeSpecFactory.Create(plan, Array.Empty<int>()));

		CollectionAssert.Contains((System.Collections.ICollection)exception.Errors, "services.worker.build is not supported by the Docker runtime executor.");
	}

	[TestMethod]
	public void Create_ShouldInjectAutomaticPlatformEnvironmentVariablesForPlugins()
	{
		using EnvironmentVariableScope authJwtSigningKeyScope = new EnvironmentVariableScope(PlatformOpsSecretsRuntimeGuard.AuthJwtSigningKeyEnvironmentVariableName, "12345678901234567890123456789012");

		DockerDeploymentPlan plan = new DockerDeploymentPlan()
		{
			PluginId = "sarah",
			Services =
			[
				new DockerDeploymentServicePlan()
				{
					Name = "api",
					Image = "manoir/sarah-api:1.0.0",
					ResolvedEnvironment =
					[
						new DockerResolvedEnvironmentEntry() { Name = "CUSTOM_SETTING", Value = "ok" }
					]
				}
			]
		};

		DockerRuntimeSpec spec = DockerRuntimeSpecFactory.Create(plan, Array.Empty<int>());

		CollectionAssert.Contains((System.Collections.ICollection)spec.Services[0].Environment, "MONGODB_SERVICE_HOST=mongo");
		CollectionAssert.Contains((System.Collections.ICollection)spec.Services[0].Environment, "NATS_SERVICE_PORT=4222");
		CollectionAssert.Contains((System.Collections.ICollection)spec.Services[0].Environment, "MQTT_SERVICE_PORT=1883");
		CollectionAssert.Contains((System.Collections.ICollection)spec.Services[0].Environment, "REDIS_SERVICE_PORT=6379");
		CollectionAssert.Contains((System.Collections.ICollection)spec.Services[0].Environment, "HOMEAUTOMATION_AUTH_JWT_SIGNING_KEY=12345678901234567890123456789012");
		CollectionAssert.Contains((System.Collections.ICollection)spec.Services[0].Environment, "CUSTOM_SETTING=ok");
	}

	[TestMethod]
	public void Create_ShouldMountHomeAutomationRootForNonSharedPlugins()
	{
		using EnvironmentVariableScope homeAutomationRootScope = new EnvironmentVariableScope(DockerSharedServicesCatalog.SharedServicesHostRootPathEnvironmentVariableName, @"C:\ProgramData\MaNoir\home-automation\shared-services");

		DockerDeploymentPlan plan = new DockerDeploymentPlan()
		{
			PluginId = "sarah",
			RepositoryRootPath = @"C:\ProgramData\MaNoir\home-automation\shared-services",
			Services =
			[
				new DockerDeploymentServicePlan()
				{
					Name = "api",
					Image = "manoir/sarah-api:1.0.0",
					ResolvedEnvironment = []
				}
			]
		};

		DockerRuntimeSpec spec = DockerRuntimeSpecFactory.Create(plan, Array.Empty<int>());

		Assert.AreEqual(1, spec.Services[0].Mounts.Count);
		Assert.AreEqual(DockerRuntimeMountKind.Bind, spec.Services[0].Mounts[0].Kind);
		Assert.AreEqual(@"C:\ProgramData\MaNoir\home-automation", spec.Services[0].Mounts[0].Source);
		Assert.AreEqual(DockerSharedServicesCatalog.HomeAutomationRootContainerPath, spec.Services[0].Mounts[0].Target);
		Assert.IsFalse(spec.Services[0].Mounts[0].IsReadOnly);
	}

	[TestMethod]
	public void Create_ShouldNotMountHomeAutomationRootForSharedServicesPlugin()
	{
		using EnvironmentVariableScope homeAutomationRootScope = new EnvironmentVariableScope(DockerSharedServicesCatalog.SharedServicesHostRootPathEnvironmentVariableName, @"C:\ProgramData\MaNoir\home-automation\shared-services");

		DockerDeploymentPlan plan = new DockerDeploymentPlan()
		{
			PluginId = DockerSharedServicesCatalog.SharedServicesPluginId,
			RepositoryRootPath = @"C:\ProgramData\MaNoir\home-automation\shared-services",
			Services =
			[
				new DockerDeploymentServicePlan()
				{
					Name = "mqtt",
					Image = "eclipse-mosquitto:2",
					ResolvedEnvironment = []
				}
			]
		};

		DockerRuntimeSpec spec = DockerRuntimeSpecFactory.Create(plan, Array.Empty<int>());

		Assert.AreEqual(0, spec.Services[0].Mounts.Count);
	}
}