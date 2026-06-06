using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Docker.DotNet.Models;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Provider.Docker;

public static class DockerSharedServicesCatalog
{
	private const string DefaultGrafanaAdminUser = "manoir";
	private const string DefaultGrafanaAdminPassword = "manoir";

	public const string SharedServicesPluginId = "shared-services";

	public const string SharedServicesGroup = "shared-services";

	public const string HomeAutomationRootContainerPath = "/home-automation";

	public const string DevelopmentInstanceEnvironmentVariableName = DockerPlatformRuntimeEnvironment.DevelopmentInstanceEnvironmentVariableName;

	public const string SharedServicesHostRootPathEnvironmentVariableName = "MANOIR_SHARED_SERVICES_HOST_ROOT_PATH";

	public const string MongoImageEnvironmentVariableName = "MANOIR_MONGO_IMAGE";

	public const string DefaultMongoImage = "mongo:8";

	public const string DefaultTraefikImage = "traefik:v3.7";

	public const string DefaultLokiImage = "grafana/loki:3.5.0";

	public const string DefaultTempoImage = "grafana/tempo:2.8.2";

	public const string DefaultPrometheusImage = "prom/prometheus:v3.4.1";

	public const string DefaultGrafanaImage = "grafana/grafana:12.0.1";

	private static readonly string MosquittoConfigurationFileContent = "listener 1883 0.0.0.0\nallow_anonymous true\npersistence false\n";
	private static readonly string TraefikConfigurationFileContent = "entryPoints:\n  web:\n    address: \":80\"\nproviders:\n  docker:\n    endpoint: \"unix:///var/run/docker.sock\"\n    exposedByDefault: false\n    network: \"manoir\"\n";
	private static readonly string LokiConfigurationFileContent = "auth_enabled: false\nserver:\n  http_listen_port: 3100\ncommon:\n  path_prefix: /loki\n  replication_factor: 1\n  ring:\n    instance_addr: 127.0.0.1\n    kvstore:\n      store: inmemory\nschema_config:\n  configs:\n    - from: 2025-01-01\n      store: tsdb\n      object_store: filesystem\n      schema: v13\n      index:\n        prefix: index_\n        period: 24h\nstorage_config:\n  filesystem:\n    directory: /loki/chunks\nruler:\n  storage:\n    type: local\n    local:\n      directory: /loki/rules\n  rule_path: /loki/rules-temp\n  ring:\n    kvstore:\n      store: inmemory\n  enable_api: true\nlimits_config:\n  allow_structured_metadata: true\n  volume_enabled: true\npattern_ingester:\n  enabled: true\n";
	private static readonly string TempoConfigurationFileContent = "stream_over_http_enabled: true\nserver:\n  http_listen_port: 3200\ndistributor:\n  receivers:\n    otlp:\n      protocols:\n        grpc:\n          endpoint: 0.0.0.0:4317\n        http:\n          endpoint: 0.0.0.0:4318\nstorage:\n  trace:\n    backend: local\n    wal:\n      path: /var/tempo/wal\n    local:\n      path: /var/tempo/traces\nusage_report:\n  reporting_enabled: false\n";
	private static readonly string PrometheusConfigurationFileContent = "global:\n  scrape_interval: 15s\n  evaluation_interval: 15s\nscrape_configs:\n  - job_name: prometheus\n    static_configs:\n      - targets: ['prometheus:9090']\n  - job_name: manoir-platform-core\n    metrics_path: /metrics\n    static_configs:\n      - targets: ['manoir-platform-core:8080']\n  - job_name: manoir-gaia\n    metrics_path: /metrics\n    static_configs:\n      - targets: ['manoir-agents-gaia:8080']\n";
	private static readonly string GrafanaDataSourcesConfigurationFileContent = "apiVersion: 1\ndatasources:\n  - name: Prometheus\n    uid: prometheus\n    type: prometheus\n    access: proxy\n    url: http://prometheus:9090\n    isDefault: true\n  - name: Loki\n    uid: loki\n    type: loki\n    access: proxy\n    url: http://loki:3100\n  - name: Tempo\n    uid: tempo\n    type: tempo\n    access: proxy\n    url: http://tempo:3200\n    jsonData:\n      tracesToLogsV2:\n        datasourceUid: loki\n      serviceMap:\n        datasourceUid: prometheus\n";

	public static DockerDeploymentPlan CreateDeploymentPlan(string sharedServicesRootPath, IEnumerable<string> serviceNames = null)
	{
		string resolvedRootPath = ResolveSharedServicesRootPath(sharedServicesRootPath);
		string dockerHostSharedServicesRootPath = ResolveDockerHostSharedServicesRootPath(resolvedRootPath);
		EnsureMosquittoConfiguration(resolvedRootPath);
		EnsureTraefikConfiguration(resolvedRootPath);
		EnsureObservabilityConfiguration(resolvedRootPath);
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
				IsRequiredForMinimumVital = service.IsRequiredForMinimumVital,
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
		string lokiConfigPath = Path.Combine(sharedServicesRootPath, "loki", "config", "loki.yml");
		string lokiDataPath = Path.Combine(sharedServicesRootPath, "loki", "data");
		string tempoConfigPath = Path.Combine(sharedServicesRootPath, "tempo", "config", "tempo.yml");
		string tempoDataPath = Path.Combine(sharedServicesRootPath, "tempo", "data");
		string prometheusConfigPath = Path.Combine(sharedServicesRootPath, "prometheus", "config", "prometheus.yml");
		string prometheusDataPath = Path.Combine(sharedServicesRootPath, "prometheus", "data");
		string grafanaDatasourcesPath = Path.Combine(sharedServicesRootPath, "grafana", "provisioning", "datasources");
		string grafanaDataPath = Path.Combine(sharedServicesRootPath, "grafana", "data");

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
			},
			new DockerDeploymentServicePlan()
			{
				Name = "loki",
				ContainerName = "manoir-shared-loki",
				Image = DefaultLokiImage,
				RestartPolicy = "unless-stopped",
				ImagePullPolicy = DockerImagePullPolicy.Always,
				IsRequiredForMinimumVital = false,
				Ports = isDevelopmentInstance ? ["3100:3100"] : Array.Empty<string>(),
				Volumes =
				[
					lokiConfigPath + ":/etc/loki/local-config.yaml:ro",
					lokiDataPath + ":/loki"
				],
				Environment = Array.Empty<DockerComposeEnvironmentEntry>(),
				ResolvedEnvironment = Array.Empty<DockerResolvedEnvironmentEntry>()
			},
			new DockerDeploymentServicePlan()
			{
				Name = "tempo",
				ContainerName = "manoir-shared-tempo",
				Image = DefaultTempoImage,
				RestartPolicy = "unless-stopped",
				ImagePullPolicy = DockerImagePullPolicy.Always,
				IsRequiredForMinimumVital = false,
				Ports = isDevelopmentInstance ? ["3200:3200", "4318:4318"] : Array.Empty<string>(),
				Volumes =
				[
					tempoConfigPath + ":/etc/tempo.yaml:ro",
					tempoDataPath + ":/var/tempo"
				],
				Environment = Array.Empty<DockerComposeEnvironmentEntry>(),
				ResolvedEnvironment = Array.Empty<DockerResolvedEnvironmentEntry>()
			},
			new DockerDeploymentServicePlan()
			{
				Name = "prometheus",
				ContainerName = "manoir-shared-prometheus",
				Image = DefaultPrometheusImage,
				RestartPolicy = "unless-stopped",
				ImagePullPolicy = DockerImagePullPolicy.Always,
				IsRequiredForMinimumVital = false,
				Ports = isDevelopmentInstance ? ["9090:9090"] : Array.Empty<string>(),
				Volumes =
				[
					prometheusConfigPath + ":/etc/prometheus/prometheus.yml:ro",
					prometheusDataPath + ":/prometheus"
				],
				Environment = Array.Empty<DockerComposeEnvironmentEntry>(),
				ResolvedEnvironment = Array.Empty<DockerResolvedEnvironmentEntry>()
			},
			new DockerDeploymentServicePlan()
			{
				Name = "grafana",
				ContainerName = "manoir-shared-grafana",
				Image = DefaultGrafanaImage,
				RestartPolicy = "unless-stopped",
				ImagePullPolicy = DockerImagePullPolicy.Always,
				IsRequiredForMinimumVital = false,
				Ports = isDevelopmentInstance ? ["3000:3000"] : Array.Empty<string>(),
				Volumes =
				[
					grafanaDatasourcesPath + ":/etc/grafana/provisioning/datasources:ro",
					grafanaDataPath + ":/var/lib/grafana"
				],
				Environment =
				[
					new DockerComposeEnvironmentEntry() { Name = "GF_SECURITY_ADMIN_USER", Value = DefaultGrafanaAdminUser },
					new DockerComposeEnvironmentEntry() { Name = "GF_SECURITY_ADMIN_PASSWORD", Value = ResolveGrafanaAdminPassword() },
					new DockerComposeEnvironmentEntry() { Name = "GF_AUTH_ANONYMOUS_ENABLED", Value = "false" }
				],
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

	private static string ResolveGrafanaAdminPassword()
	{
		string apiKey = Environment.GetEnvironmentVariable(PlatformOpsSecretsRuntimeGuard.ApiKeyEnvironmentVariableName);
		return string.IsNullOrWhiteSpace(apiKey)
			? DefaultGrafanaAdminPassword
			: apiKey.Trim();
	}

	private static DockerDeploymentServicePlan CloneServicePlan(DockerDeploymentServicePlan source)
	{
		return new DockerDeploymentServicePlan()
		{
			Name = source.Name,
			IsRequiredForMinimumVital = source.IsRequiredForMinimumVital,
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

	private static void EnsureObservabilityConfiguration(string sharedServicesRootPath)
	{
		EnsureConfigurationFile(sharedServicesRootPath, Path.Combine("loki", "config"), "loki.yml", LokiConfigurationFileContent);
		EnsureConfigurationFile(sharedServicesRootPath, Path.Combine("tempo", "config"), "tempo.yml", TempoConfigurationFileContent);
		EnsureConfigurationFile(sharedServicesRootPath, Path.Combine("prometheus", "config"), "prometheus.yml", PrometheusConfigurationFileContent);
		EnsureConfigurationFile(sharedServicesRootPath, Path.Combine("grafana", "provisioning", "datasources"), "datasources.yml", GrafanaDataSourcesConfigurationFileContent);

		EnsureWritableDataDirectory(Path.Combine(sharedServicesRootPath, "loki", "data"));
		EnsureWritableDataDirectory(Path.Combine(sharedServicesRootPath, "tempo", "data"));
		EnsureWritableDataDirectory(Path.Combine(sharedServicesRootPath, "prometheus", "data"));
		EnsureWritableDataDirectory(Path.Combine(sharedServicesRootPath, "grafana", "data"));
	}

	private static void EnsureConfigurationFile(string sharedServicesRootPath, string relativeDirectoryPath, string fileName, string content)
	{
		string directoryPath = Path.Combine(sharedServicesRootPath, relativeDirectoryPath);
		Directory.CreateDirectory(directoryPath);

		string configurationFilePath = Path.Combine(directoryPath, fileName);
		if (!File.Exists(configurationFilePath) || !string.Equals(File.ReadAllText(configurationFilePath), content, StringComparison.Ordinal))
			File.WriteAllText(configurationFilePath, content);
	}

	private static void EnsureWritableDataDirectory(string directoryPath)
	{
		Directory.CreateDirectory(directoryPath);

		if (OperatingSystem.IsWindows())
			return;

		File.SetUnixFileMode(
			directoryPath,
			UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
			UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
			UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
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