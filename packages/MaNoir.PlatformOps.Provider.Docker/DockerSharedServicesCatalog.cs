using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Docker.DotNet.Models;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Provider.Docker;

public static class DockerSharedServicesCatalog
{
	public const string SharedServicesPluginId = "shared-services";

	public const string SharedServicesGroup = "shared-services";

	public const string HomeAutomationRootContainerPath = "/home-automation";

	public const string DevelopmentInstanceEnvironmentVariableName = DockerPlatformRuntimeEnvironment.DevelopmentInstanceEnvironmentVariableName;

	public const string SharedServicesHostRootPathEnvironmentVariableName = "MANOIR_SHARED_SERVICES_HOST_ROOT_PATH";

	private static readonly string MosquittoConfigurationFileContent = "listener 1883 0.0.0.0\nallow_anonymous true\npersistence false\n";

	public static DockerDeploymentPlan CreateDeploymentPlan(string sharedServicesRootPath, IEnumerable<string> serviceNames = null)
	{
		string resolvedRootPath = ResolveSharedServicesRootPath(sharedServicesRootPath);
		string dockerHostSharedServicesRootPath = ResolveDockerHostSharedServicesRootPath(resolvedRootPath);
		EnsureMosquittoConfiguration(resolvedRootPath);
		bool isDevelopmentInstance = DockerPlatformRuntimeEnvironment.IsDevelopmentInstance();

		HashSet<string> requestedServiceNames = serviceNames == null
			? null
			: serviceNames.Where(name => !string.IsNullOrWhiteSpace(name)).ToHashSet(StringComparer.OrdinalIgnoreCase);

		List<DockerDeploymentServicePlan> services = GetRequiredServiceDefinitions(dockerHostSharedServicesRootPath, isDevelopmentInstance)
			.Where(service => requestedServiceNames == null || requestedServiceNames.Contains(service.Name))
			.Select(CloneServicePlan)
			.ToList();

		return new DockerDeploymentPlan()
		{
			PluginId = SharedServicesPluginId,
			DeploymentGroup = SharedServicesGroup,
			RepositoryRootPath = resolvedRootPath,
			ComposeFilePath = Path.Combine(resolvedRootPath, "shared-services.generated.yml"),
			SharedEnvironmentVariables = Array.Empty<PluginEnvironmentVariable>(),
			ResolvedSharedEnvironmentVariables = Array.Empty<PluginResolvedEnvironmentVariable>(),
			Services = services
		};
	}

	public static IReadOnlyList<DockerSharedServiceStatus> Evaluate(IReadOnlyList<ContainerListResponse> containers, string sharedServicesRootPath)
	{
		IReadOnlyList<DockerDeploymentServicePlan> requiredServices = GetRequiredServiceDefinitions(ResolveSharedServicesRootPath(sharedServicesRootPath), DockerPlatformRuntimeEnvironment.IsDevelopmentInstance());
		List<DockerSharedServiceStatus> statuses = new List<DockerSharedServiceStatus>();

		foreach (DockerDeploymentServicePlan service in requiredServices)
		{
			ContainerListResponse container = containers?
				.FirstOrDefault(candidate => HasContainerName(candidate, ResolveContainerName(service.Name)));

			statuses.Add(new DockerSharedServiceStatus()
			{
				ServiceName = service.Name,
				ContainerName = ResolveContainerName(service.Name),
				ExpectedImage = service.Image,
				CurrentImage = container?.Image,
				CurrentImageId = container?.ImageID,
				IsPresent = container != null,
				IsRunning = string.Equals(container?.State, "running", StringComparison.OrdinalIgnoreCase),
				MatchesExpectedImage = string.Equals(container?.Image, service.Image, StringComparison.OrdinalIgnoreCase)
			});
		}

		return statuses;
	}

	public static string ResolveSharedServicesRootPath(string sharedServicesRootPath)
	{
		if (!string.IsNullOrWhiteSpace(sharedServicesRootPath))
			return Path.GetFullPath(sharedServicesRootPath);

		return Path.Combine(ResolveDefaultHomeAutomationRootPath(), "shared-services");
	}

	public static string ResolveDockerHostSharedServicesRootPath(string sharedServicesRootPath)
	{
		string configuredHostRootPath = Environment.GetEnvironmentVariable(SharedServicesHostRootPathEnvironmentVariableName);
		return string.IsNullOrWhiteSpace(configuredHostRootPath)
			? sharedServicesRootPath
			: configuredHostRootPath.Trim();
	}

	public static string ResolveDockerHostHomeAutomationRootPath(string sharedServicesRootPath)
	{
		string dockerHostSharedServicesRootPath = ResolveDockerHostSharedServicesRootPath(ResolveSharedServicesRootPath(sharedServicesRootPath));
		string normalizedPath = dockerHostSharedServicesRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string homeAutomationRootPath = TryGetParentPath(normalizedPath);

		return string.IsNullOrWhiteSpace(homeAutomationRootPath)
			? normalizedPath
			: homeAutomationRootPath;
	}

	private static IReadOnlyList<DockerDeploymentServicePlan> GetRequiredServiceDefinitions(string sharedServicesRootPath, bool isDevelopmentInstance)
	{
		string mqttRootPath = Path.Combine(sharedServicesRootPath, "mqtt");
		string mqttConfigPath = Path.Combine(mqttRootPath, "config");
		string mqttDataPath = Path.Combine(mqttRootPath, "data");
		string mqttLogPath = Path.Combine(mqttRootPath, "log");

		return
		[
			new DockerDeploymentServicePlan()
			{
				Name = "mongo",
				ContainerName = "manoir-shared-mongo",
				Image = "mongo:8",
				RestartPolicy = "unless-stopped",
				ImagePullPolicy = DockerImagePullPolicy.IfNotPresent,
				Ports = isDevelopmentInstance ? ["27017:27017"] : Array.Empty<string>(),
				Volumes = ["manoir-shared-mongo:/data/db"],
				Environment = Array.Empty<DockerComposeEnvironmentEntry>(),
				ResolvedEnvironment = Array.Empty<DockerResolvedEnvironmentEntry>()
			},
			new DockerDeploymentServicePlan()
			{
				Name = "nats",
				ContainerName = "manoir-shared-nats",
				Image = "nats:2.14.0",
				RestartPolicy = "unless-stopped",
				ImagePullPolicy = DockerImagePullPolicy.IfNotPresent,
				Ports = isDevelopmentInstance ? ["4222:4222"] : Array.Empty<string>(),
				Environment = Array.Empty<DockerComposeEnvironmentEntry>(),
				ResolvedEnvironment = Array.Empty<DockerResolvedEnvironmentEntry>()
			},
			new DockerDeploymentServicePlan()
			{
				Name = "mqtt",
				ContainerName = "manoir-shared-mqtt",
				Image = "eclipse-mosquitto:2",
				RestartPolicy = "unless-stopped",
				ImagePullPolicy = DockerImagePullPolicy.IfNotPresent,
				Ports = ["1883:1883"],
				Volumes =
				[
					mqttConfigPath + ":/mosquitto/config:ro",
					mqttDataPath + ":/mosquitto/data",
					mqttLogPath + ":/mosquitto/log"
				],
				Environment = Array.Empty<DockerComposeEnvironmentEntry>(),
				ResolvedEnvironment = Array.Empty<DockerResolvedEnvironmentEntry>()
			},
			new DockerDeploymentServicePlan()
			{
				Name = "redis",
				ContainerName = "manoir-shared-redis",
				Image = "redis:7.4",
				RestartPolicy = "unless-stopped",
				ImagePullPolicy = DockerImagePullPolicy.IfNotPresent,
				Volumes = ["manoir-shared-redis:/data"],
				Environment = Array.Empty<DockerComposeEnvironmentEntry>(),
				ResolvedEnvironment = Array.Empty<DockerResolvedEnvironmentEntry>()
			}
		];
	}

	private static DockerDeploymentServicePlan CloneServicePlan(DockerDeploymentServicePlan source)
	{
		return new DockerDeploymentServicePlan()
		{
			Name = source.Name,
			Image = source.Image,
			BuildContext = source.BuildContext,
			ContainerName = source.ContainerName,
			RestartPolicy = source.RestartPolicy,
			ImagePullPolicy = source.ImagePullPolicy,
			Ports = source.Ports?.ToArray() ?? Array.Empty<string>(),
			Volumes = source.Volumes?.ToArray() ?? Array.Empty<string>(),
			DependsOn = source.DependsOn?.ToArray() ?? Array.Empty<string>(),
			Environment = source.Environment?.ToArray() ?? Array.Empty<DockerComposeEnvironmentEntry>(),
			ResolvedEnvironment = source.ResolvedEnvironment?.ToArray() ?? Array.Empty<DockerResolvedEnvironmentEntry>()
		};
	}

	private static string ResolveContainerName(string serviceName)
	{
		return DockerRuntimeSpecFactory.ResolveContainerName(SharedServicesPluginId, new DockerDeploymentServicePlan()
		{
			Name = serviceName,
			ContainerName = "manoir-shared-" + PlatformOpsNaming.RequireSegment(serviceName, "service", nameof(serviceName))
		});
	}

	private static bool HasContainerName(ContainerListResponse container, string containerName)
	{
		return container?.Names != null
			&& container.Names.Any(name => string.Equals(name?.TrimStart('/'), containerName, StringComparison.OrdinalIgnoreCase));
	}

	private static void EnsureMosquittoConfiguration(string sharedServicesRootPath)
	{
		string mqttConfigDirectoryPath = Path.Combine(sharedServicesRootPath, "mqtt", "config");
		string mqttDataDirectoryPath = Path.Combine(sharedServicesRootPath, "mqtt", "data");
		string mqttLogDirectoryPath = Path.Combine(sharedServicesRootPath, "mqtt", "log");
		Directory.CreateDirectory(mqttConfigDirectoryPath);
		Directory.CreateDirectory(mqttDataDirectoryPath);
		Directory.CreateDirectory(mqttLogDirectoryPath);

		string configurationFilePath = Path.Combine(mqttConfigDirectoryPath, "mosquitto.conf");
		if (!File.Exists(configurationFilePath) || !string.Equals(File.ReadAllText(configurationFilePath), MosquittoConfigurationFileContent, StringComparison.Ordinal))
			File.WriteAllText(configurationFilePath, MosquittoConfigurationFileContent);
	}

	private static string ResolveDefaultHomeAutomationRootPath()
	{
		if (OperatingSystem.IsWindows())
		{
			string programDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
			if (string.IsNullOrWhiteSpace(programDataPath))
				programDataPath = AppContext.BaseDirectory;

			return Path.Combine(programDataPath, "MaNoir", "home-automation");
		}

		return Path.Combine(Path.DirectorySeparatorChar.ToString(), "home-automation");
	}

	private static string TryGetParentPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return null;

		if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
		{
			int lastSeparatorIndex = path.LastIndexOfAny(['\\', '/']);
			return lastSeparatorIndex <= 2 ? path : path.Substring(0, lastSeparatorIndex);
		}

		return Path.GetDirectoryName(path);
	}

}