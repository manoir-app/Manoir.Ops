using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MaNoir.PlatformOps.Core;
using MaNoir.PlatformOps.Provider.Docker;

namespace MaNoir.PlatformOps.AdminUi;

public sealed class GaiaOperationsService
{
	private readonly GaiaOptions _options;
	private readonly ILogger<GaiaOperationsService> _logger;
	private readonly GaiaRuntimeStateStore _runtimeStateStore;
	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
	private DockerFirstRunStatus _lastStatus;
	private DateTimeOffset? _lastInspectionUtc;
	private DateTimeOffset? _lastEnsureUtc;
	private string _lastError;
	private IReadOnlyList<AdminUiDeploymentProjection> _lastAdminUiDeployments = Array.Empty<AdminUiDeploymentProjection>();
	private IReadOnlyList<AdminUiDeploymentDiff> _lastAdminUiDeploymentDiffs = Array.Empty<AdminUiDeploymentDiff>();

	public GaiaOperationsService(GaiaOptions options, ILogger<GaiaOperationsService> logger)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_runtimeStateStore = new GaiaRuntimeStateStore(ResolveRuntimeStatePath());
		TryLoadPersistedRuntimeState();
	}

	public async Task<GaiaDashboardState> GetStateAsync(CancellationToken cancellationToken = default)
	{
		if (_lastStatus == null)
			return await InspectAsync(cancellationToken);

		return CreateSnapshot();
	}

	public async Task<IReadOnlyList<AdminUiDeploymentProjection>> GetAdminUiDeploymentsAsync(CancellationToken cancellationToken = default)
	{
		if (_lastStatus == null)
			await InspectAsync(cancellationToken);

		return _lastAdminUiDeployments;
	}

	public async Task<IReadOnlyList<AdminUiDeploymentDiff>> GetAdminUiDeploymentDiffsAsync(CancellationToken cancellationToken = default)
	{
		if (_lastStatus == null)
			await InspectAsync(cancellationToken);

		return _lastAdminUiDeploymentDiffs;
	}

	public bool IsMinimumVitalReady => _lastStatus?.HasMinimumVital == true;

	public Task InitializePluginRepositoriesAsync(CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
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

	public async Task<GaiaDashboardState> ResetSharedServicesAsync(bool wipeData = false, CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			_logger.LogWarning("Gaia shared services reset started. WipeData={WipeData}.", wipeData);
			using DockerFirstRunBootstrapper bootstrapper = new DockerFirstRunBootstrapper(_options.SharedServicesRootPath);
			DockerFirstRunStatus status = await bootstrapper.ResetSharedServicesAsync(wipeData, cancellationToken);
			ApplyStatus(status, isEnsureOperation: true);

			foreach (string operationMessage in status.OperationMessages)
				_logger.LogInformation("{OperationMessage}", operationMessage);

			foreach (string operationError in status.OperationErrors)
				_logger.LogWarning("{OperationError}", operationError);

			_logger.LogWarning(
				"Gaia shared services reset completed. RemovedSharedServices={RemovedSharedServices}, RemovedDataVolumes={RemovedDataVolumes}, HasMinimumVital={HasMinimumVital}, OperationErrors={OperationErrors}.",
				status.RemovedSharedServices.Count,
				status.RemovedDataVolumes.Count,
				status.HasMinimumVital,
				status.OperationErrors.Count);
			return CreateSnapshot();
		}
		catch (Exception exception)
		{
			_lastError = exception.Message;
			_logger.LogError(exception, "Gaia could not reset the shared services.");
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
			List<AdminUiDeploymentProjection> currentAdminUiDeployments = new List<AdminUiDeploymentProjection>();

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
						PluginManifest manifest = PluginManifestParser.ParseFile(descriptor.ManifestPath);
						DockerDeploymentPlan plan = await DockerDeploymentPlanFactory.CreateAsync(descriptor, cancellationToken);
						await deploymentExecutor.ApplyAsync(plan, cancellationToken);
						currentAdminUiDeployments.Add(AdminUiDeploymentProjectionFactory.Create(manifest, descriptor));
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
			_lastAdminUiDeploymentDiffs = BuildAdminUiDeploymentDiffs(_lastAdminUiDeployments, currentAdminUiDeployments);
			_lastAdminUiDeployments = currentAdminUiDeployments;

			ApplyStatus(refreshedStatus, isEnsureOperation: true);
			TryPersistRuntimeState();

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
			AdminUiDeployments = _lastAdminUiDeployments,
			AdminUiDeploymentDiffs = _lastAdminUiDeploymentDiffs,
			LastInspectionUtc = _lastInspectionUtc,
			LastEnsureUtc = _lastEnsureUtc,
			LastError = _lastError
		};
	}

	private void TryLoadPersistedRuntimeState()
	{
		try
		{
			GaiaPersistedRuntimeState state = _runtimeStateStore.Load();
			if (state == null)
				return;

			_lastInspectionUtc = state.LastInspectionUtc;
			_lastEnsureUtc = state.LastEnsureUtc;
			_lastAdminUiDeployments = state.AdminUiDeployments ?? Array.Empty<AdminUiDeploymentProjection>();
			_lastAdminUiDeploymentDiffs = state.AdminUiDeploymentDiffs ?? Array.Empty<AdminUiDeploymentDiff>();
			_logger.LogInformation("Gaia runtime state restored from {StateFilePath}.", _runtimeStateStore.StateFilePath);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Gaia could not restore the persisted runtime state from {StateFilePath}.", _runtimeStateStore.StateFilePath);
		}
	}

	private void TryPersistRuntimeState()
	{
		try
		{
			_runtimeStateStore.Save(new GaiaPersistedRuntimeState()
			{
				LastInspectionUtc = _lastInspectionUtc,
				LastEnsureUtc = _lastEnsureUtc,
				AdminUiDeployments = _lastAdminUiDeployments?.ToArray() ?? Array.Empty<AdminUiDeploymentProjection>(),
				AdminUiDeploymentDiffs = _lastAdminUiDeploymentDiffs?.ToArray() ?? Array.Empty<AdminUiDeploymentDiff>()
			});
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Gaia could not persist the runtime state to {StateFilePath}.", _runtimeStateStore.StateFilePath);
		}
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

	private string ResolveRuntimeStatePath()
	{
		if (!string.IsNullOrWhiteSpace(_options.RuntimeStatePath))
			return _options.RuntimeStatePath;

		if (!string.IsNullOrWhiteSpace(_options.SharedServicesRootPath))
			return Path.Combine(_options.SharedServicesRootPath, "gaia", "runtime-state.json");

		return Path.Combine(AppContext.BaseDirectory, "data", "gaia-runtime-state.json");
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

	private static IReadOnlyList<AdminUiDeploymentDiff> BuildAdminUiDeploymentDiffs(IReadOnlyList<AdminUiDeploymentProjection> previousDeployments, IReadOnlyList<AdminUiDeploymentProjection> currentDeployments)
	{
		Dictionary<string, AdminUiDeploymentProjection> previousByPluginId = (previousDeployments ?? Array.Empty<AdminUiDeploymentProjection>())
			.Where(deployment => !string.IsNullOrWhiteSpace(deployment?.PluginId))
			.ToDictionary(deployment => deployment.PluginId, StringComparer.Ordinal);

		return (currentDeployments ?? Array.Empty<AdminUiDeploymentProjection>())
			.Select(current => AdminUiDeploymentDiffFactory.Create(previousByPluginId.TryGetValue(current.PluginId, out AdminUiDeploymentProjection previous) ? previous : null, current))
			.OrderBy(diff => diff.PluginId, StringComparer.Ordinal)
			.ToArray();
	}
}

public sealed class GaiaDashboardState
{
	public GaiaOptions Options { get; set; }

	public DockerFirstRunStatus LastStatus { get; set; }

	public IReadOnlyList<AdminUiDeploymentProjection> AdminUiDeployments { get; set; } = Array.Empty<AdminUiDeploymentProjection>();

	public IReadOnlyList<AdminUiDeploymentDiff> AdminUiDeploymentDiffs { get; set; } = Array.Empty<AdminUiDeploymentDiff>();

	public DateTimeOffset? LastInspectionUtc { get; set; }

	public DateTimeOffset? LastEnsureUtc { get; set; }

	public string LastError { get; set; }
}