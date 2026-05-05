using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Provider.Docker;

public sealed class DockerFirstRunBootstrapper : IDisposable
{
	private readonly DockerClient _dockerClient;
	private readonly string _sharedServicesRootPath;
	private readonly Func<DockerDeploymentPlan, CancellationToken, Task<DockerDeploymentExecutionResult>> _applyDeploymentAsync;

	public DockerFirstRunBootstrapper(string sharedServicesRootPath = null)
		: this(new DockerClientConfiguration().CreateClient(), sharedServicesRootPath, null)
	{
	}

	internal DockerFirstRunBootstrapper(DockerClient dockerClient, string sharedServicesRootPath, Func<DockerDeploymentPlan, CancellationToken, Task<DockerDeploymentExecutionResult>> applyDeploymentAsync)
	{
		_dockerClient = dockerClient ?? throw new ArgumentNullException(nameof(dockerClient));
		_sharedServicesRootPath = DockerSharedServicesCatalog.ResolveSharedServicesRootPath(sharedServicesRootPath);
		_applyDeploymentAsync = applyDeploymentAsync ?? ((plan, cancellationToken) => new DockerDeploymentExecutor().ApplyAsync(plan, cancellationToken));
	}

	public async Task<DockerFirstRunStatus> InspectAsync(CancellationToken cancellationToken = default)
	{
		DockerFirstRunStatus status = new DockerFirstRunStatus()
		{
			SharedServices = DockerSharedServicesCatalog.Evaluate(Array.Empty<ContainerListResponse>(), _sharedServicesRootPath),
			CoreServices = DockerCoreServiceCatalog.Evaluate(Array.Empty<ContainerListResponse>(), _sharedServicesRootPath),
			EnvironmentErrors = GetEnvironmentErrors()
		};

		try
		{
			await _dockerClient.System.PingAsync();
			VersionResponse version = await _dockerClient.System.GetVersionAsync();
			IList<ContainerListResponse> containers = await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters() { All = true }, cancellationToken);

			status.IsDockerAvailable = true;
			status.DockerServerVersion = version?.Version;
			status.SharedServices = DockerSharedServicesCatalog.Evaluate(containers.ToArray(), _sharedServicesRootPath);
			status.CoreServices = DockerCoreServiceCatalog.Evaluate(containers.ToArray(), _sharedServicesRootPath);
			return status;
		}
		catch (Exception exception)
		{
			status.IsDockerAvailable = false;
			status.DockerError = exception.Message;
			return status;
		}
	}

	public async Task<DockerFirstRunStatus> EnsureSharedServicesAsync(CancellationToken cancellationToken = default)
	{
		return await EnsureMinimumVitalAsync(cancellationToken);
	}

	public async Task<DockerFirstRunStatus> EnsureMinimumVitalAsync(CancellationToken cancellationToken = default)
	{
		DockerFirstRunStatus status = await InspectAsync(cancellationToken);
		if (!status.IsDockerAvailable || !status.HasRequiredEnvironment)
			return status;

		List<string> operationMessages = new List<string>();
		List<string> operationErrors = new List<string>();
		List<string> deployedSharedServices = new List<string>();
		List<string> deployedCoreServices = new List<string>();
		await RefreshMutableImagesAsync(status.SharedServices, "shared service", shouldPullAlways: true, operationMessages, operationErrors, cancellationToken);
		await RefreshMutableImagesAsync(status.CoreServices, "core service", shouldPullAlways: true, operationMessages, operationErrors, cancellationToken);

		string[] servicesToDeploy = status.SharedServices
			.Where(service => !service.IsRunning || !service.MatchesExpectedImage)
			.Select(service => service.ServiceName)
			.ToArray();

		string[] coreServicesToDeploy = status.CoreServices
			.Where(service => !service.IsRunning || !service.MatchesExpectedImage)
			.Select(service => service.ServiceName)
			.ToArray();

		if (servicesToDeploy.Length == 0 && coreServicesToDeploy.Length == 0)
		{
			status.OperationMessages = operationMessages;
			status.OperationErrors = operationErrors;
			return status;
		}

		foreach (string serviceName in servicesToDeploy)
		{
			operationMessages.Add("Ensuring shared service '" + serviceName + "'.");
			try
			{
				DockerDeploymentPlan sharedServicesPlan = DockerSharedServicesCatalog.CreateDeploymentPlan(_sharedServicesRootPath, [serviceName]);
				await _applyDeploymentAsync(sharedServicesPlan, cancellationToken);
				deployedSharedServices.Add(serviceName);
				operationMessages.Add("Shared service '" + serviceName + "' ensured.");
			}
			catch (Exception exception)
			{
				operationErrors.Add("Shared service '" + serviceName + "' could not be ensured: " + exception.Message);
			}
		}

		foreach (string serviceName in coreServicesToDeploy)
		{
			operationMessages.Add("Ensuring core service '" + serviceName + "'.");
			try
			{
				DockerDeploymentPlan corePlan = DockerCoreServiceCatalog.CreateDeploymentPlan(_sharedServicesRootPath);
				await _applyDeploymentAsync(corePlan, cancellationToken);
				deployedCoreServices.Add(serviceName);
				operationMessages.Add("Core service '" + serviceName + "' ensured.");
			}
			catch (Exception exception)
			{
				operationErrors.Add("Core service '" + serviceName + "' could not be ensured: " + exception.Message);
			}
		}

		DockerFirstRunStatus refreshedStatus = await InspectAsync(cancellationToken);
		refreshedStatus.DeployedSharedServices = deployedSharedServices;
		refreshedStatus.DeployedCoreServices = deployedCoreServices;
		refreshedStatus.OperationErrors = operationErrors;
		refreshedStatus.OperationMessages = operationMessages;
		return refreshedStatus;
	}

	public async Task<DockerFirstRunStatus> ResetSharedServicesAsync(bool wipeData = false, CancellationToken cancellationToken = default)
	{
		DockerFirstRunStatus inspectStatus = await InspectAsync(cancellationToken);
		if (!inspectStatus.IsDockerAvailable)
			return inspectStatus;

		List<string> operationMessages = new List<string>();
		List<string> operationErrors = new List<string>();
		List<string> removedContainers = new List<string>();
		List<string> removedVolumes = new List<string>();

		IList<ContainerListResponse> containers = await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters() { All = true }, cancellationToken);

		foreach (DockerSharedServiceStatus service in inspectStatus.SharedServices)
		{
			if (service == null)
				continue;

			ContainerListResponse container = containers.FirstOrDefault(candidate => ContainerHasName(candidate, service.ContainerName));
			if (container != null)
			{
				operationMessages.Add("Removing container '" + service.ContainerName + "'.");
				try
				{
					await _dockerClient.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters() { Force = true, RemoveVolumes = false }, cancellationToken);
					removedContainers.Add(service.ContainerName);
					operationMessages.Add("Container '" + service.ContainerName + "' removed.");
				}
				catch (Exception exception)
				{
					operationErrors.Add("Container '" + service.ContainerName + "' could not be removed: " + exception.Message);
				}
			}
			else
			{
				operationMessages.Add("Container '" + service.ContainerName + "' was not present.");
			}
		}

		if (wipeData)
		{
			foreach (string volumeName in DockerSharedServicesCatalog.GetDataVolumeNames())
			{
				operationMessages.Add("Removing data volume '" + volumeName + "'.");
				try
				{
					await _dockerClient.Volumes.RemoveAsync(volumeName, false, cancellationToken);
					removedVolumes.Add(volumeName);
					operationMessages.Add("Data volume '" + volumeName + "' removed.");
				}
				catch (DockerApiException apiException) when (apiException.StatusCode == HttpStatusCode.NotFound)
				{
					operationMessages.Add("Data volume '" + volumeName + "' was not present.");
				}
				catch (Exception exception)
				{
					operationErrors.Add("Data volume '" + volumeName + "' could not be removed: " + exception.Message);
				}
			}
		}

		DockerFirstRunStatus redeployStatus = await EnsureMinimumVitalAsync(cancellationToken);

		List<string> allMessages = new List<string>(operationMessages);
		allMessages.AddRange(redeployStatus.OperationMessages ?? Array.Empty<string>());
		List<string> allErrors = new List<string>(operationErrors);
		allErrors.AddRange(redeployStatus.OperationErrors ?? Array.Empty<string>());

		redeployStatus.RemovedSharedServices = removedContainers;
		redeployStatus.RemovedDataVolumes = removedVolumes;
		redeployStatus.OperationMessages = allMessages;
		redeployStatus.OperationErrors = allErrors;
		return redeployStatus;
	}

	public void Dispose()
	{
		_dockerClient?.Dispose();
	}

	private static IReadOnlyList<string> GetEnvironmentErrors()
	{
		return PlatformOpsSecretsRuntimeGuard.TryEnsureConfigured(out _, out IReadOnlyList<string> errors)
			? Array.Empty<string>()
			: errors;
	}

	private async Task RefreshMutableImagesAsync(
		IReadOnlyList<DockerSharedServiceStatus> services,
		string serviceKind,
		bool shouldPullAlways,
		List<string> operationMessages,
		List<string> operationErrors,
		CancellationToken cancellationToken)
	{
		if (services == null)
			return;

		if (!shouldPullAlways)
			return;

		foreach (DockerSharedServiceStatus service in services)
		{
			if (service == null || !service.IsPresent || !service.IsRunning || !service.MatchesExpectedImage)
				continue;

			try
			{
				bool imageUpdated = await PullAndDetectImageUpdateAsync(service.ExpectedImage, service.CurrentImageId, cancellationToken);
				if (!imageUpdated)
					continue;

				service.MatchesExpectedImage = false;
				operationMessages.Add("A newer image was pulled for " + serviceKind + " '" + service.ServiceName + "'.");
			}
			catch (Exception exception)
			{
				operationErrors.Add("The image refresh check could not complete for " + serviceKind + " '" + service.ServiceName + "': " + exception.Message);
			}
		}
	}

	private async Task<bool> PullAndDetectImageUpdateAsync(string imageReference, string currentImageId, CancellationToken cancellationToken)
	{
		(string repository, string tag) = SplitImageReference(imageReference);
		await _dockerClient.Images.CreateImageAsync(
			new ImagesCreateParameters()
			{
				FromImage = repository,
				Tag = tag
			},
			null,
			new Progress<JSONMessage>(),
			cancellationToken);

		ImageInspectResponse image = await _dockerClient.Images.InspectImageAsync(imageReference, cancellationToken);
		return !string.IsNullOrWhiteSpace(image?.ID)
			&& !string.Equals(image.ID, currentImageId, StringComparison.OrdinalIgnoreCase);
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

	private static bool ContainerHasName(ContainerListResponse container, string containerName)
	{
		return container?.Names != null
			&& container.Names.Any(name => string.Equals(name?.TrimStart('/'), containerName, StringComparison.OrdinalIgnoreCase));
	}
}