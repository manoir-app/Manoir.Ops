using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet.Models;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Provider.Docker;

public static class DockerCoreServiceCatalog
{
	public const string CorePluginId = "core";

	public const string PlatformPluginId = "platform";

	public const string CoreGroup = "core";

	public const string CoreServiceName = "core";

	public const string DefaultCoreAdminUiPathPrefix = "/platform";

	public const int DefaultCoreAdminUiServicePort = 8080;

	public const string CoreAdminUiHostPortEnvironmentVariableName = "MANOIR_CORE_ADMINUI_HOST_PORT";

	public const int DefaultCoreAdminUiHostPort = 81;

	private const string CoreImageRepository = "ghcr.io/manoir-app/manoir-core-adminui";

	public static DockerDeploymentPlan CreateDeploymentPlan(string platformRootPath)
	{
		if (TryLoadPlatformCoreDescriptor(platformRootPath, out PluginDeploymentDescriptor descriptor, out _))
			return CreatePlanFromPlatformDescriptor(descriptor);

		return CreateFallbackDeploymentPlan(platformRootPath);
	}

	public static async Task<DockerDeploymentPlan> CreateDeploymentPlanAsync(string platformRootPath, CancellationToken cancellationToken = default)
	{
		if (TryLoadPlatformCoreDescriptor(platformRootPath, out PluginDeploymentDescriptor descriptor, out _))
			return await DockerDeploymentPlanFactory.CreateAsync(descriptor, cancellationToken);

		return CreateFallbackDeploymentPlan(platformRootPath);
	}

	public static string GetPlatformCorePluginAvailabilityError(string platformRootPath)
	{
		return TryLoadPlatformCoreDescriptor(platformRootPath, out _, out string error)
			? null
			: error;
	}

	private static DockerDeploymentPlan CreateFallbackDeploymentPlan(string platformRootPath)
	{
		string resolvedRootPath = DockerSharedServicesCatalog.ResolveSharedServicesRootPath(platformRootPath);
		bool isDevelopmentInstance = DockerPlatformRuntimeEnvironment.IsDevelopmentInstance();
		string imageTag = isDevelopmentInstance ? "dev" : "latest";
		int hostPort = ResolveCoreAdminUiHostPort();

		return new DockerDeploymentPlan()
		{
			PluginId = CorePluginId,
			DeploymentGroup = CoreGroup,
			RepositoryRootPath = resolvedRootPath,
			ComposeFilePath = Path.Combine(resolvedRootPath, "core.generated.yml"),
			SharedEnvironmentVariables = Array.Empty<PluginEnvironmentVariable>(),
			ResolvedSharedEnvironmentVariables = Array.Empty<PluginResolvedEnvironmentVariable>(),
			Services =
			[
				new DockerDeploymentServicePlan()
				{
					Name = CoreServiceName,
					ContainerName = CoreServiceName,
					Image = CoreImageRepository + ":" + imageTag,
					RestartPolicy = "unless-stopped",
					ImagePullPolicy = DockerImagePullPolicy.Always,
					Ports = [hostPort.ToString() + ":8080"],
					Environment = Array.Empty<DockerComposeEnvironmentEntry>(),
					ResolvedEnvironment = Array.Empty<DockerResolvedEnvironmentEntry>(),
					Labels = CreateAdminUiLabels()
				}
			]
		};
	}

	public static IReadOnlyList<DockerSharedServiceStatus> Evaluate(IReadOnlyList<ContainerListResponse> containers, string platformRootPath)
	{
		DockerDeploymentPlan plan = CreateDeploymentPlan(platformRootPath);
		DockerDeploymentServicePlan service = plan.Services.Single();
		ContainerListResponse container = containers?
			.FirstOrDefault(candidate => HasContainerName(candidate, service.ContainerName));

		return
		[
			new DockerSharedServiceStatus()
			{
				ServiceName = service.Name,
				ContainerName = service.ContainerName,
				ExpectedImage = service.Image,
				CurrentImage = container?.Image,
				CurrentImageId = container?.ImageID,
				IsPresent = container != null,
				IsRunning = string.Equals(container?.State, "running", StringComparison.OrdinalIgnoreCase),
				MatchesExpectedImage = string.Equals(container?.Image, service.Image, StringComparison.OrdinalIgnoreCase),
				PublishedPorts = DockerPublishedPortFormatter.Format(container?.Ports)
			}
		];
	}

	private static bool HasContainerName(ContainerListResponse container, string containerName)
	{
		return container?.Names != null
			&& container.Names.Any(name => string.Equals(name?.TrimStart('/'), containerName, StringComparison.OrdinalIgnoreCase));
	}

	private static int ResolveCoreAdminUiHostPort()
	{
		string rawValue = Environment.GetEnvironmentVariable(CoreAdminUiHostPortEnvironmentVariableName);
		if (string.IsNullOrWhiteSpace(rawValue))
			return DefaultCoreAdminUiHostPort;

		if (!int.TryParse(rawValue.Trim(), out int hostPort) || hostPort is <= 0 or > 65535)
			throw new InvalidOperationException(CoreAdminUiHostPortEnvironmentVariableName + " must contain a valid TCP port number.");

		return hostPort;
	}

	private static IReadOnlyDictionary<string, string> CreateAdminUiLabels()
	{
		DockerAdminUiRoutePlan routePlan = DockerDeploymentPlanFactory.CreateAdminUiRoutePlan(new PluginDeploymentDescriptor()
		{
			PluginId = PlatformPluginId,
			AdminUiPathPrefix = DefaultCoreAdminUiPathPrefix,
			AdminUiServiceName = CoreServiceName,
			AdminUiServicePort = DefaultCoreAdminUiServicePort
		});

		return routePlan?.Labels == null
			? new Dictionary<string, string>(StringComparer.Ordinal)
			: new Dictionary<string, string>(routePlan.Labels, StringComparer.Ordinal);
	}

	private static bool TryLoadPlatformCoreDescriptor(string platformRootPath, out PluginDeploymentDescriptor descriptor, out string error)
	{
		string pluginRepositoriesRootPath = DockerSharedServicesCatalog.ResolvePluginRepositoriesRootPath(platformRootPath);
		return PlatformCoreCatalogPluginLoader.TryLoad(pluginRepositoriesRootPath, out descriptor, out error);
	}

	private static DockerDeploymentPlan CreatePlanFromPlatformDescriptor(PluginDeploymentDescriptor descriptor)
	{
		DockerComposeFile composeFile = DockerComposeParser.ParseFile(descriptor.ComposeArtifactFullPath);
		DockerAdminUiRoutePlan routePlan = DockerDeploymentPlanFactory.CreateAdminUiRoutePlan(descriptor);
		List<DockerDeploymentServicePlan> services = new List<DockerDeploymentServicePlan>();

		foreach (DockerComposeService service in composeFile.Services)
		{
			bool isAdminUiService = routePlan != null && string.Equals(service.Name, routePlan.ComposeServiceName, StringComparison.OrdinalIgnoreCase);
			services.Add(new DockerDeploymentServicePlan()
			{
				Name = service.Name,
				Image = DockerDeploymentPlanFactory.NormalizeImageForRuntime(service.Image),
				BuildContext = service.BuildContext,
				ContainerName = service.ContainerName,
				RestartPolicy = service.RestartPolicy,
				ImagePullPolicy = DockerImagePullPolicy.Always,
				Ports = service.Ports,
				Volumes = service.Volumes,
				DependsOn = service.DependsOn,
				Environment = service.Environment,
				ResolvedEnvironment = Array.Empty<DockerResolvedEnvironmentEntry>(),
				Labels = isAdminUiService && routePlan?.Labels != null
					? new Dictionary<string, string>(routePlan.Labels, StringComparer.Ordinal)
					: new Dictionary<string, string>(StringComparer.Ordinal)
			});
		}

		return new DockerDeploymentPlan()
		{
			PluginId = descriptor.PluginId,
			DeploymentGroup = string.IsNullOrWhiteSpace(descriptor.DeploymentGroup) ? descriptor.PluginId : descriptor.DeploymentGroup,
			RepositoryRootPath = descriptor.RepositoryRootPath,
			ComposeFilePath = descriptor.ComposeArtifactFullPath,
			SharedEnvironmentVariables = Array.Empty<PluginEnvironmentVariable>(),
			ResolvedSharedEnvironmentVariables = Array.Empty<PluginResolvedEnvironmentVariable>(),
			Services = services
		};
	}
}