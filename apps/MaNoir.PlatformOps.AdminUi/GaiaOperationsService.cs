using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaNoir.Core.Contracts.Models.Mesh;
using Microsoft.Extensions.Logging;
using MaNoir.PlatformOps.Core;
using MaNoir.PlatformOps.Provider.Docker;

namespace MaNoir.PlatformOps.AdminUi;

public sealed class GaiaOperationsService
{
	private readonly GaiaOptions _options;
	private readonly ILogger<GaiaOperationsService> _logger;
	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
	private readonly HttpClient _httpClient = new HttpClient();
	private DockerFirstRunStatus _lastStatus;
	private DateTimeOffset? _lastInspectionUtc;
	private DateTimeOffset? _lastEnsureUtc;
	private string _lastError;

	public GaiaOperationsService(GaiaOptions options, ILogger<GaiaOperationsService> logger)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task<GaiaDashboardState> GetStateAsync(CancellationToken cancellationToken = default)
	{
		if (_lastStatus == null)
			return await InspectAsync(cancellationToken);

		return CreateSnapshot();
	}

	public async Task<GaiaDashboardState> InspectAsync(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			_logger.LogInformation("Gaia inspection started.");
			using DockerFirstRunBootstrapper bootstrapper = new DockerFirstRunBootstrapper(_options.SharedServicesRootPath);
			DockerFirstRunStatus status = await bootstrapper.InspectAsync(cancellationToken);
			ApplyStatus(status, isEnsureOperation: false);
			_logger.LogInformation(
				"Gaia inspection completed. DockerAvailable={DockerAvailable}, NeedsMinimumVitalDeployment={NeedsMinimumVitalDeployment}, OperationErrors={OperationErrors}.",
				status.IsDockerAvailable,
				status.NeedsMinimumVitalDeployment,
				status.OperationErrors.Count);
			return CreateSnapshot();
		}
		catch (Exception exception)
		{
			_lastError = exception.Message;
			_logger.LogError(exception, "Gaia could not inspect the local Docker runtime.");
			throw;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<GaiaDashboardState> EnsureSharedServicesAsync(CancellationToken cancellationToken = default)
	{
		return await EnsureMinimumVitalAsync(cancellationToken);
	}

	public async Task<GaiaDashboardState> EnsureMinimumVitalAsync(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			_logger.LogInformation("Gaia ensure cycle started.");
			using DockerFirstRunBootstrapper bootstrapper = new DockerFirstRunBootstrapper(_options.SharedServicesRootPath);
			DockerFirstRunStatus status = await bootstrapper.EnsureMinimumVitalAsync(cancellationToken);
			List<string> operationMessages = new List<string>(status.OperationMessages ?? Array.Empty<string>());
			List<string> operationErrors = new List<string>(status.OperationErrors ?? Array.Empty<string>());
			await EnsureEdgeCertificateAsync(operationMessages, operationErrors, cancellationToken);
			status.OperationMessages = operationMessages;
			status.OperationErrors = operationErrors;
			ApplyStatus(status, isEnsureOperation: true);

			if (status.OperationMessages.Count > 0)
			{
				foreach (string operationMessage in status.OperationMessages)
					_logger.LogInformation("{OperationMessage}", operationMessage);
			}

			if (status.OperationErrors.Count > 0)
			{
				foreach (string operationError in status.OperationErrors)
					_logger.LogWarning("{OperationError}", operationError);
			}

			_logger.LogInformation(
				"Gaia ensure cycle completed. DeployedSharedServices={DeployedSharedServices}, DeployedCoreServices={DeployedCoreServices}, HasMinimumVital={HasMinimumVital}, OperationErrors={OperationErrors}.",
				status.DeployedSharedServices.Count,
				status.DeployedCoreServices.Count,
				status.HasMinimumVital,
				status.OperationErrors.Count);
			return CreateSnapshot();
		}
		catch (Exception exception)
		{
			_lastError = exception.Message;
			_logger.LogError(exception, "Gaia could not ensure the minimum vital services.");
			throw;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<GaiaDashboardState> RefreshAndRestartAllPluginsAsync(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			_logger.LogInformation("Gaia plugin refresh cycle started.");

			using DockerFirstRunBootstrapper bootstrapper = new DockerFirstRunBootstrapper(_options.SharedServicesRootPath);
			DockerFirstRunStatus status = await bootstrapper.EnsureMinimumVitalAsync(cancellationToken);

			List<string> operationMessages = new List<string>(status.OperationMessages ?? Array.Empty<string>());
			List<string> operationErrors = new List<string>(status.OperationErrors ?? Array.Empty<string>());
			List<string> deployedPlugins = new List<string>();

			string pluginRepositoriesRootPath = ResolvePluginRepositoriesRootPath();
			string[] pluginRepositoryRoots = EnumeratePluginRepositoryRoots(pluginRepositoriesRootPath).ToArray();

			if (pluginRepositoryRoots.Length == 0)
			{
				operationMessages.Add("No plugin repository was found under '" + pluginRepositoriesRootPath + "'.");
			}
			else
			{
				using DockerDeploymentExecutor deploymentExecutor = new DockerDeploymentExecutor();

				foreach (string repositoryRootPath in pluginRepositoryRoots)
				{
					try
					{
						PluginDeploymentDescriptor descriptor = PluginRepositoryDeploymentLoader.Load(repositoryRootPath);
						DockerDeploymentPlan plan = await DockerDeploymentPlanFactory.CreateAsync(descriptor, cancellationToken);
						await deploymentExecutor.ApplyAsync(plan, cancellationToken);
						deployedPlugins.Add(descriptor.PluginId);
						operationMessages.Add("Plugin '" + descriptor.PluginId + "' refreshed and restarted.");
					}
					catch (Exception exception)
					{
						operationErrors.Add("Plugin repository '" + repositoryRootPath + "' could not be refreshed: " + exception.Message);
					}
				}
			}

			DockerFirstRunStatus refreshedStatus = await bootstrapper.InspectAsync(cancellationToken);
			refreshedStatus.DeployedSharedServices = status.DeployedSharedServices;
			refreshedStatus.DeployedCoreServices = status.DeployedCoreServices;
			refreshedStatus.DeployedPlugins = deployedPlugins;
			refreshedStatus.OperationMessages = operationMessages;
			refreshedStatus.OperationErrors = operationErrors;

			ApplyStatus(refreshedStatus, isEnsureOperation: true);

			foreach (string operationMessage in refreshedStatus.OperationMessages)
				_logger.LogInformation("{OperationMessage}", operationMessage);

			foreach (string operationError in refreshedStatus.OperationErrors)
				_logger.LogWarning("{OperationError}", operationError);

			_logger.LogInformation(
				"Gaia plugin refresh cycle completed. DeployedPlugins={DeployedPlugins}, DeployedCoreServices={DeployedCoreServices}, OperationErrors={OperationErrors}.",
				refreshedStatus.DeployedPlugins.Count,
				refreshedStatus.DeployedCoreServices.Count,
				refreshedStatus.OperationErrors.Count);

			return CreateSnapshot();
		}
		catch (Exception exception)
		{
			_lastError = exception.Message;
			_logger.LogError(exception, "Gaia could not refresh and restart all plugins.");
			throw;
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task EnsureEdgeCertificateAsync(List<string> operationMessages, List<string> operationErrors, CancellationToken cancellationToken)
	{
		GaiaEdgeCertificateRuntimeConfiguration edgeCertificate = await GaiaEdgeCertificateRuntimeResolver.ResolveAsync(cancellationToken);
		if (!edgeCertificate.IsEnabled)
			return;

		if (string.IsNullOrWhiteSpace(edgeCertificate.AccountEmail))
		{
			operationErrors.Add("The edge certificate account email is required when edge certificate convergence is enabled. Configure " + GaiaEdgeCertificateRuntimeResolver.AccountEmailEnvironmentVariableName + ".");
			return;
		}

		try
		{
			using DockerLocalCoreMeshSettingsClient meshSettingsClient = new DockerLocalCoreMeshSettingsClient(_httpClient);
			AutomationMeshLocalSettings meshSettings = await meshSettingsClient.GetLocalSettingsAsync(cancellationToken: cancellationToken);
			if (meshSettings == null)
			{
				operationMessages.Add("No local Core mesh settings are available yet for edge certificate convergence.");
				return;
			}

			if (string.IsNullOrWhiteSpace(meshSettings.PublicBaseDomain))
			{
				operationMessages.Add("No mesh public base domain is configured. Edge certificate convergence was skipped.");
				return;
			}

			using DockerTraefikCertificateOrchestrator certificateOrchestrator = new DockerTraefikCertificateOrchestrator(_options.SharedServicesRootPath);
			DockerTraefikCertificateDeploymentResult result = await certificateOrchestrator.EnsureCurrentAsync(
				new DockerTraefikCertificateConfiguration()
				{
					DomainName = meshSettings.PublicBaseDomain,
					UseWildcard = edgeCertificate.UseWildcard,
					AccountEmail = edgeCertificate.AccountEmail,
					AcmeServerUri = edgeCertificate.AcmeServerUri,
					OrganizationUnit = edgeCertificate.OrganizationUnit,
					OvhDns = new OvhDnsChallengeSecretConfiguration()
					{
						ZoneName = string.IsNullOrWhiteSpace(edgeCertificate.DnsZoneNameOverride) ? meshSettings.PublicBaseDomain : edgeCertificate.DnsZoneNameOverride
					}
				},
				cancellationToken);

			if (result.Certificate?.WasRenewed == true)
				operationMessages.Add("The edge certificate was renewed for '" + meshSettings.PublicBaseDomain + "'.");
			else
				operationMessages.Add("The edge certificate is already current for '" + meshSettings.PublicBaseDomain + "'.");

			if (result.ReloadAttempted && !result.ReloadPerformed)
				operationErrors.Add("The edge certificate changed but the Traefik container '" + result.ReloadedContainerName + "' could not be reloaded automatically.");
		}
		catch (Exception exception)
		{
			operationErrors.Add("The edge certificate convergence failed: " + exception.Message);
		}
	}

	private void ApplyStatus(DockerFirstRunStatus status, bool isEnsureOperation)
	{
		_lastStatus = status;
		_lastInspectionUtc = DateTimeOffset.UtcNow;
		if (isEnsureOperation)
			_lastEnsureUtc = _lastInspectionUtc;

		if (status?.OperationErrors?.Count > 0)
			_lastError = string.Join(" | ", status.OperationErrors);
		else
			_lastError = status?.DockerError;
	}

	private GaiaDashboardState CreateSnapshot()
	{
		return new GaiaDashboardState()
		{
			Options = _options,
			LastStatus = _lastStatus,
			LastInspectionUtc = _lastInspectionUtc,
			LastEnsureUtc = _lastEnsureUtc,
			LastError = _lastError
		};
	}

	private string ResolvePluginRepositoriesRootPath()
	{
		if (!string.IsNullOrWhiteSpace(_options.PluginRepositoriesRootPath))
			return _options.PluginRepositoriesRootPath;

		if (!string.IsNullOrWhiteSpace(_options.SharedServicesRootPath))
		{
			string homeAutomationRootPath = Path.GetDirectoryName(_options.SharedServicesRootPath.TrimEnd('/'));
			if (!string.IsNullOrWhiteSpace(homeAutomationRootPath))
				return Path.Combine(homeAutomationRootPath, "plugins");
		}

		return Path.Combine(DockerSharedServicesCatalog.HomeAutomationRootContainerPath, "plugins");
	}

	private static IEnumerable<string> EnumeratePluginRepositoryRoots(string pluginRepositoriesRootPath)
	{
		if (string.IsNullOrWhiteSpace(pluginRepositoriesRootPath) || !Directory.Exists(pluginRepositoriesRootPath))
			return Array.Empty<string>();

		return Directory
			.EnumerateFiles(pluginRepositoriesRootPath, PluginRepositoryDeploymentLoader.DefaultManifestFileName, SearchOption.AllDirectories)
			.Select(Path.GetDirectoryName)
			.Where(path => !string.IsNullOrWhiteSpace(path))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}
}

public sealed class GaiaDashboardState
{
	public GaiaOptions Options { get; set; }

	public DockerFirstRunStatus LastStatus { get; set; }

	public DateTimeOffset? LastInspectionUtc { get; set; }

	public DateTimeOffset? LastEnsureUtc { get; set; }

	public string LastError { get; set; }
}