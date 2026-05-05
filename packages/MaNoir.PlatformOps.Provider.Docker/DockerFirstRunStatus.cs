using System;
using System.Collections.Generic;
using System.Linq;

namespace MaNoir.PlatformOps.Provider.Docker;

public sealed class DockerFirstRunStatus
{
	public bool IsDockerAvailable { get; set; }

	public string DockerServerVersion { get; set; }

	public string DockerError { get; set; }

	public IReadOnlyList<string> EnvironmentErrors { get; set; } = Array.Empty<string>();

	public IReadOnlyList<DockerSharedServiceStatus> SharedServices { get; set; } = Array.Empty<DockerSharedServiceStatus>();

	public IReadOnlyList<DockerSharedServiceStatus> CoreServices { get; set; } = Array.Empty<DockerSharedServiceStatus>();

	public IReadOnlyList<string> DeployedSharedServices { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> DeployedCoreServices { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> DeployedPlugins { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> RemovedSharedServices { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> RemovedDataVolumes { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> OperationErrors { get; set; } = Array.Empty<string>();

	public IReadOnlyList<string> OperationMessages { get; set; } = Array.Empty<string>();

	public bool HasRequiredEnvironment => EnvironmentErrors.Count == 0;

	public bool HasOperationErrors => OperationErrors.Count > 0;

	public bool NeedsSharedServicesDeployment => SharedServices.Any(service => !service.IsRunning || !service.MatchesExpectedImage);

	public bool NeedsCoreServicesDeployment => CoreServices.Any(service => !service.IsRunning || !service.MatchesExpectedImage);

	public bool NeedsMinimumVitalDeployment => NeedsSharedServicesDeployment || NeedsCoreServicesDeployment;

	public bool HasMinimumVital => IsDockerAvailable
		&& SharedServices.Count > 0
		&& CoreServices.Count > 0
		&& SharedServices.All(service => service.IsRunning && service.MatchesExpectedImage)
		&& CoreServices.All(service => service.IsRunning && service.MatchesExpectedImage);
}

public sealed class DockerSharedServiceStatus
{
	public string ServiceName { get; set; }

	public string ContainerName { get; set; }

	public string ExpectedImage { get; set; }

	public string CurrentImage { get; set; }

	public string CurrentImageId { get; set; }

	public bool IsPresent { get; set; }

	public bool IsRunning { get; set; }

	public bool MatchesExpectedImage { get; set; }
}