using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Provider.Docker;

public sealed class DockerDeploymentExecutor : IDisposable
{
	private const int ImagePullRetryCount = 3;

	private readonly DockerClient _dockerClient;

	public DockerDeploymentExecutor()
		: this(DockerClientFactory.CreateClient())
	{
	}

	public DockerDeploymentExecutor(DockerClient dockerClient)
	{
		_dockerClient = dockerClient ?? throw new ArgumentNullException(nameof(dockerClient));
	}

	public async Task<DockerDeploymentExecutionResult> ApplyAsync(DockerDeploymentPlan plan, CancellationToken cancellationToken = default)
	{
		if (plan == null)
			throw new ArgumentNullException(nameof(plan));

		PlatformOpsSecretsRuntimeGuard.EnsureConfigured();

		HashSet<string> targetContainerNames = plan.Services
			.Where(service => service != null)
			.Select(service => DockerRuntimeSpecFactory.ResolveContainerName(plan.PluginId, service))
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		IList<ContainerListResponse> containers = await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters()
		{
			All = true
		}, cancellationToken);

		HashSet<int> usedHostPorts = GetUsedHostPorts(containers, targetContainerNames);
		DockerRuntimeSpec runtimeSpec = DockerRuntimeSpecFactory.Create(plan, usedHostPorts);
		await EnsureNetworkAsync(runtimeSpec.NetworkName, cancellationToken);

		List<string> pulledImages = new List<string>();
		List<string> recreatedContainers = new List<string>();
		List<string> startedContainers = new List<string>();

		foreach (DockerRuntimeServiceSpec service in runtimeSpec.Services)
		{
			DockerDeploymentServicePlan planService = plan.Services.First(candidate => string.Equals(DockerRuntimeSpecFactory.ResolveContainerName(plan.PluginId, candidate), service.ContainerName, StringComparison.OrdinalIgnoreCase));
			await EnsureImageAsync(service.Image, planService.ImagePullPolicy, pulledImages, cancellationToken);

			ContainerListResponse existingContainer = containers.FirstOrDefault(container => ContainerHasName(container, service.ContainerName));
			if (existingContainer != null)
			{
				await RemoveContainerAsync(existingContainer.ID, cancellationToken);
				recreatedContainers.Add(service.ContainerName);
			}

			CreateContainerResponse createdContainer = await _dockerClient.Containers.CreateContainerAsync(BuildCreateContainerParameters(plan, runtimeSpec, service, planService), cancellationToken);
			bool started = await _dockerClient.Containers.StartContainerAsync(createdContainer.ID, new ContainerStartParameters(), cancellationToken);
			if (!started)
				throw new InvalidOperationException("The Docker container could not be started: " + service.ContainerName + ".");

			startedContainers.Add(service.ContainerName);
		}

		return new DockerDeploymentExecutionResult()
		{
			PulledImages = pulledImages,
			RecreatedContainers = recreatedContainers,
			StartedContainers = startedContainers
		};
	}

	public void Dispose()
	{
		_dockerClient?.Dispose();
	}

	private async Task EnsureNetworkAsync(string networkName, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(networkName))
			throw new ArgumentException("A Docker network name is required.", nameof(networkName));

		IList<NetworkResponse> networks = await _dockerClient.Networks.ListNetworksAsync(new NetworksListParameters(), cancellationToken);
		if (networks.Any(network => string.Equals(network?.Name, networkName, StringComparison.OrdinalIgnoreCase)))
			return;

		await _dockerClient.Networks.CreateNetworkAsync(new NetworksCreateParameters()
		{
			Name = networkName,
			Driver = "bridge",
			CheckDuplicate = true,
			Labels = new Dictionary<string, string>()
			{
				["manoir.managed-by"] = "platformops"
			}
		}, cancellationToken);
	}

	private async Task EnsureImageAsync(string imageReference, DockerImagePullPolicy imagePullPolicy, List<string> pulledImages, CancellationToken cancellationToken)
	{
		if (imagePullPolicy != DockerImagePullPolicy.Always && await ImageExistsLocallyAsync(imageReference, cancellationToken))
			return;

		(string repository, string tag) = SplitImageReference(imageReference);
		await PullImageWithRetriesAsync(repository, tag, cancellationToken);

		pulledImages.Add(imageReference);
	}

	private async Task PullImageWithRetriesAsync(string repository, string tag, CancellationToken cancellationToken)
	{
		for (int attempt = 1; ; attempt++)
		{
			try
			{
				await _dockerClient.Images.CreateImageAsync(
					new ImagesCreateParameters()
					{
						FromImage = repository,
						Tag = tag
					},
					null,
					new Progress<JSONMessage>(),
					cancellationToken);
				return;
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception exception) when (attempt < ImagePullRetryCount && IsTransientImagePullException(exception))
			{
				await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
			}
		}
	}

	private static bool IsTransientImagePullException(Exception exception)
	{
		if (exception is HttpRequestException)
			return true;

		if (exception is DockerApiException apiException)
		{
			string responseBody = apiException.ResponseBody ?? string.Empty;
			if (responseBody.Contains("EOF", StringComparison.OrdinalIgnoreCase)
				|| responseBody.Contains("TLS handshake timeout", StringComparison.OrdinalIgnoreCase)
				|| responseBody.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
				|| responseBody.Contains("i/o timeout", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private async Task<bool> ImageExistsLocallyAsync(string imageReference, CancellationToken cancellationToken)
	{
		try
		{
			ImageInspectResponse image = await _dockerClient.Images.InspectImageAsync(imageReference, cancellationToken);
			return !string.IsNullOrWhiteSpace(image?.ID);
		}
		catch (DockerImageNotFoundException)
		{
			return false;
		}
	}

	private static CreateContainerParameters BuildCreateContainerParameters(DockerDeploymentPlan plan, DockerRuntimeSpec runtimeSpec, DockerRuntimeServiceSpec service, DockerDeploymentServicePlan planService)
	{
		Dictionary<string, EmptyStruct> exposedPorts = new Dictionary<string, EmptyStruct>();
		Dictionary<string, IList<PortBinding>> portBindings = new Dictionary<string, IList<PortBinding>>();
		Dictionary<string, string> labels = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (DockerRuntimePortBinding binding in service.PortBindings)
		{
			string portKey = binding.ContainerPort + "/" + binding.Protocol;
			exposedPorts[portKey] = default;
			portBindings[portKey] = new List<PortBinding>()
			{
				new PortBinding() { HostIP = "0.0.0.0", HostPort = binding.HostPort.ToString() }
			};
		}

		if (planService?.Labels != null)
		{
			foreach (KeyValuePair<string, string> label in planService.Labels)
			{
				if (string.IsNullOrWhiteSpace(label.Key))
					continue;

				labels[label.Key] = label.Value ?? string.Empty;
			}
		}

		labels["manoir.managed-by"] = "platformops";
		labels["manoir.group"] = string.IsNullOrWhiteSpace(plan.DeploymentGroup) ? plan.PluginId ?? string.Empty : plan.DeploymentGroup;
		labels["manoir.plugin-id"] = plan.PluginId ?? string.Empty;
		labels["manoir.service-name"] = service.ServiceName ?? string.Empty;

		return new CreateContainerParameters()
		{
			Image = service.Image,
			Name = service.ContainerName,
			Hostname = service.ContainerName,
			Env = service.Environment.ToList(),
			ExposedPorts = exposedPorts.Count == 0 ? null : exposedPorts,
			NetworkingConfig = BuildNetworkingConfig(runtimeSpec, service),
			Labels = labels,
			HostConfig = new HostConfig()
			{
				Mounts = service.Mounts.Select(BuildMount).ToList(),
				PortBindings = portBindings.Count == 0 ? null : portBindings,
				RestartPolicy = BuildRestartPolicy(service.RestartPolicy)
			}
		};
	}

	private static Mount BuildMount(DockerRuntimeMount mount)
	{
		if (mount == null)
			return null;

		return new Mount()
		{
			Type = mount.Kind == DockerRuntimeMountKind.Bind ? "bind" : "volume",
			Source = mount.Source,
			Target = mount.Target,
			ReadOnly = mount.IsReadOnly
		};
	}

	private static NetworkingConfig BuildNetworkingConfig(DockerRuntimeSpec runtimeSpec, DockerRuntimeServiceSpec service)
	{
		if (runtimeSpec == null || string.IsNullOrWhiteSpace(runtimeSpec.NetworkName))
			return null;

		return new NetworkingConfig()
		{
			EndpointsConfig = new Dictionary<string, EndpointSettings>()
			{
				[runtimeSpec.NetworkName] = new EndpointSettings()
				{
					Aliases = service.NetworkAliases?.ToList()
				}
			}
		};
	}

	private static RestartPolicy BuildRestartPolicy(string restartPolicy)
	{
		if (string.IsNullOrWhiteSpace(restartPolicy))
			return null;

		RestartPolicyKind name = restartPolicy.Trim().ToLowerInvariant() switch
		{
			"always" => RestartPolicyKind.Always,
			"unless-stopped" => RestartPolicyKind.UnlessStopped,
			"on-failure" => RestartPolicyKind.OnFailure,
			_ => RestartPolicyKind.No
		};

		return new RestartPolicy() { Name = name };
	}

	private async Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken)
	{
		await _dockerClient.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters()
		{
			Force = true,
			RemoveVolumes = false
		}, cancellationToken);
	}

	private static HashSet<int> GetUsedHostPorts(IList<ContainerListResponse> containers, IReadOnlyCollection<string> excludedContainerNames)
	{
		HashSet<int> usedPorts = new HashSet<int>();

		foreach (ContainerListResponse container in containers)
		{
			if (container == null)
				continue;

			if (container.Names != null && container.Names.Any(name => excludedContainerNames.Contains(NormalizeContainerName(name))))
				continue;

			if (container.Ports == null)
				continue;

			foreach (Port port in container.Ports)
			{
				if (port.PublicPort > 0)
					usedPorts.Add((int)port.PublicPort);
			}
		}

		return usedPorts;
	}

	private static bool ContainerHasName(ContainerListResponse container, string containerName)
	{
		return container.Names != null && container.Names.Any(name => string.Equals(NormalizeContainerName(name), containerName, StringComparison.OrdinalIgnoreCase));
	}

	private static string NormalizeContainerName(string containerName)
	{
		return containerName?.Trim().TrimStart('/');
	}

	private static (string Repository, string Tag) SplitImageReference(string imageReference)
	{
		if (string.IsNullOrWhiteSpace(imageReference))
			throw new ArgumentException("A Docker image reference is required.", nameof(imageReference));

		if (imageReference.Contains('@', StringComparison.Ordinal))
			return (imageReference, null);

		int lastSlashIndex = imageReference.LastIndexOf('/');
		int lastColonIndex = imageReference.LastIndexOf(':');
		if (lastColonIndex > lastSlashIndex)
			return (imageReference.Substring(0, lastColonIndex), imageReference.Substring(lastColonIndex + 1));

		return (imageReference, "latest");
	}
}