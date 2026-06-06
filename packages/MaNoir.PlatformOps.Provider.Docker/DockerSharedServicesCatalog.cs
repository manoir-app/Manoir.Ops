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

	public const string MongoImageEnvironmentVariableName = "MANOIR_MONGO_IMAGE";

	public const string DefaultMongoImage = "mongo:8";

	public const string DefaultTraefikImage = "traefik:v3.7";

	private static readonly string MosquittoConfigurationFileContent = "listener 1883 0.0.0.0\nallow_anonymous true\npersistence false\n";
	private static readonly string TraefikConfigurationFileContent = "entryPoints:\n  web:\n    address: \":80\"\nproviders:\n  docker:\n    endpoint: \"unix:///var/run/docker.sock\"\n    exposedByDefault: false\n    network: \"manoir\"\n";

	public static DockerDeploymentPlan CreateDeploymentPlan(string sharedServicesRootPath, IEnumerable<string> serviceNames = null)
	{
		string resolvedRootPath = ResolveSharedServicesRootPath(sharedServicesRootPath);
		string dockerHostSharedServicesRootPath = ResolveDockerHostSharedServicesRootPath(resolvedRootPath);
		EnsureMosquittoConfiguration(resolvedRootPath);
		EnsureTraefikConfiguration(resolvedRootPath);
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
				MatchesExpectedImage = string.Equals(container?.Image, service.Image, StringComparison.OrdinalIgnoreCase),
				PublishedPorts = DockerPublishedPortFormatter.Format(container?.Ports)
			});
		}

		return statuses;
	}

	public static IReadOnlyList<string> GetDataVolumeNames()
	{
		return ["manoir-shared-mongo", "manoir-shared-redis"];
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

	public static string ResolvePluginRepositoriesRootPath(string sharedServicesRootPath)
	{
		return Path.Combine(ResolveDefaultHomeAutomationRootPath(ResolveSharedServicesRootPath(sharedServicesRootPath)), "plugins");
	}

	private static IReadOnlyList<DockerDeploymentServicePlan> GetRequiredServiceDefinitions(string sharedServicesRootPath, bool isDevelopmentInstance)
	{
		string mongoImage = ResolveMongoImage();
		string mqttRootPath = Path.Combine(sharedServicesRootPath, "mqtt");
		string mqttConfigPath = Path.Combine(mqttRootPath, "config");
		string mqttDataPath = Path.Combine(mqttRootPath, "data");
		string mqttLogPath = Path.Combine(mqttRootPath, "log");
		string traefikConfigPath = Path.Combine(sharedServicesRootPath, "traefik", "config", "traefik.yml");

		return
		[
			new DockerDeploymentServicePlan()
			{
				Name = "mongo",
				ContainerName = "manoir-shared-mongo",
				Image = mongoImage,
				RestartPolicy = "unless-stopped",
				ImagePullPolicy = DockerImagePullPolicy.Always,
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
				ImagePullPolicy = DockerImagePullPolicy.Always,
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
				ImagePullPolicy = DockerImagePullPolicy.Always,
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
				ImagePullPolicy = DockerImagePullPolicy.Always,
				Volumes = ["manoir-shared-redis:/data"],
				Environment = Array.Empty<DockerComposeEnvironmentEntry>(),
				ResolvedEnvironment = Array.Empty<DockerResolvedEnvironmentEntry>()
			},
			new DockerDeploymentServicePlan()
			{
				Name = "traefik",
				ContainerName = "manoir-shared-traefik",
				Image = DefaultTraefikImage,
				RestartPolicy = "unless-stopped",
				ImagePullPolicy = DockerImagePullPolicy.Always,
				Ports = ["80"],
				Volumes =
				[
					traefikConfigPath + ":/etc/traefik/traefik.yml:ro",
					"/var/run/docker.sock:/var/run/docker.sock"
				],
				Environment = Array.Empty<DockerComposeEnvironmentEntry>(),
				ResolvedEnvironment = Array.Empty<DockerResolvedEnvironmentEntry>()
			}
		];
	}

	public static string ResolveMongoImage()
	{
		string configuredMongoImage = Environment.GetEnvironmentVariable(MongoImageEnvironmentVariableName);
		if (!string.IsNullOrWhiteSpace(configuredMongoImage))
			return configuredMongoImage.Trim();

		return DefaultMongoImage;
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
			ResolvedEnvironment = source.ResolvedEnvironment?.ToArray() ?? Array.Empty<DockerResolvedEnvironmentEntry>(),
			Labels = source.Labels == null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(source.Labels, StringComparer.Ordinal)
		};
	}

	private static string ResolveDefaultHomeAutomationRootPath(string resolvedSharedServicesRootPath)
	{
		string normalizedPath = resolvedSharedServicesRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string homeAutomationRootPath = TryGetParentPath(normalizedPath);

		return string.IsNullOrWhiteSpace(homeAutomationRootPath)
			? normalizedPath
			: homeAutomationRootPath;
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

	private static void EnsureTraefikConfiguration(string sharedServicesRootPath)
	{
		string traefikConfigDirectoryPath = Path.Combine(sharedServicesRootPath, "traefik", "config");
		Directory.CreateDirectory(traefikConfigDirectoryPath);

		string configurationFilePath = Path.Combine(traefikConfigDirectoryPath, "traefik.yml");
		if (!File.Exists(configurationFilePath) || !string.Equals(File.ReadAllText(configurationFilePath), TraefikConfigurationFileContent, StringComparison.Ordinal))
			File.WriteAllText(configurationFilePath, TraefikConfigurationFileContent);
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

		return Path.Combine(Path.DirectorySeparatorChar.ToString(), "srv", "manoir", "home-automation");
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