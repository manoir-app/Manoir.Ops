using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Docker.DotNet.Models;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Provider.Docker;

public static class DockerCoreServiceCatalog
{
	public const string CorePluginId = "core";

	public const string CoreGroup = "core";

	public const string CoreServiceName = "core";

	public const string CoreAdminUiHostPortEnvironmentVariableName = "MANOIR_CORE_ADMINUI_HOST_PORT";

	public const int DefaultCoreAdminUiHostPort = 81;

	private const string CoreImageRepository = "ghcr.io/manoir-app/manoir-core-adminui";

	public static DockerDeploymentPlan CreateDeploymentPlan(string platformRootPath)
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
					ResolvedEnvironment = Array.Empty<DockerResolvedEnvironmentEntry>()
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
}