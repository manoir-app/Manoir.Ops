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
	private readonly GaiaPluginRepositoryManager _pluginRepositoryManager;
	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
	private DockerFirstRunStatus _lastStatus;
	private DateTimeOffset? _lastInspectionUtc;
	private DateTimeOffset? _lastEnsureUtc;
	private DateTimeOffset? _lastPluginRepositorySyncUtc;
	private string _lastError;
	private IReadOnlyList<AdminUiDeploymentProjection> _lastAdminUiDeployments = Array.Empty<AdminUiDeploymentProjection>();
	private IReadOnlyList<AdminUiDeploymentDiff> _lastAdminUiDeploymentDiffs = Array.Empty<AdminUiDeploymentDiff>();
	private IReadOnlyList<string> _configuredPluginRepositoryUrls = Array.Empty<string>();
	private IReadOnlyList<GaiaManagedPluginRepositoryState> _managedPluginRepositories = Array.Empty<GaiaManagedPluginRepositoryState>();
	private IReadOnlyList<string> _lastPluginRepositorySyncMessages = Array.Empty<string>();
	private IReadOnlyList<string> _lastPluginRepositorySyncErrors = Array.Empty<string>();

	public GaiaOperationsService(GaiaOptions options, ILogger<GaiaOperationsService> logger)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_runtimeStateStore = new GaiaRuntimeStateStore(ResolveRuntimeStatePath());
		_pluginRepositoryManager = new GaiaPluginRepositoryManager();
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

	public async Task<IReadOnlyList<GaiaAdminUiRouteDiagnostic>> GetAdminUiRouteDiagnosticsAsync(CancellationToken cancellationToken = default)
	{
		if (_lastStatus == null)
			await InspectAsync(cancellationToken);

		string localProxyBaseUrl = ResolveLocalProxyBaseUrl(_lastStatus);
		string pluginRepositoriesRootPath = ResolvePluginRepositoriesRootPath();
		string[] pluginRepositoryRoots = EnumeratePluginRepositoryRoots(pluginRepositoriesRootPath).ToArray();
		List<GaiaAdminUiRouteDiagnostic> diagnostics = new List<GaiaAdminUiRouteDiagnostic>();

		foreach (string repositoryRootPath in pluginRepositoryRoots)
		{
			try
			{
				PluginDeploymentDescriptor descriptor = PluginRepositoryDeploymentLoader.Load(repositoryRootPath);
				GaiaAdminUiRouteDiagnostic diagnostic = CreateAdminUiRouteDiagnostic(descriptor, localProxyBaseUrl, repositoryRootPath);
				if (diagnostic != null)
					diagnostics.Add(diagnostic);
			}
			catch (Exception exception)
			{
				diagnostics.Add(new GaiaAdminUiRouteDiagnostic()
				{
					RepositoryRootPath = repositoryRootPath,
					Error = exception.Message
				});
			}
		}

		if (!diagnostics.Any(diagnostic => string.Equals(diagnostic.PluginId, PlatformCoreCatalogPluginLoader.PlatformPluginId, StringComparison.OrdinalIgnoreCase)))
		{
			if (PlatformCoreCatalogPluginLoader.TryLoad(pluginRepositoriesRootPath, out PluginDeploymentDescriptor platformDescriptor, out string platformError))
			{
				GaiaAdminUiRouteDiagnostic diagnostic = CreateAdminUiRouteDiagnostic(platformDescriptor, localProxyBaseUrl, platformDescriptor.RepositoryRootPath);
				if (diagnostic != null)
					diagnostics.Add(diagnostic);
			}
			else if (!string.IsNullOrWhiteSpace(platformError))
			{
				diagnostics.Add(new GaiaAdminUiRouteDiagnostic()
				{
					PluginId = PlatformCoreCatalogPluginLoader.PlatformPluginId,
					PluginDisplayName = "Platform Core",
					RepositoryRootPath = pluginRepositoriesRootPath,
					Error = platformError
				});
			}
		}

		return diagnostics
			.OrderBy(diagnostic => diagnostic.PluginId ?? diagnostic.RepositoryRootPath, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	public Task<GaiaPluginRepositoriesSnapshot> GetPluginRepositoriesAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(CreatePluginRepositoriesSnapshot());
	}

	public async Task<GaiaPluginRepositoriesSnapshot> UpdatePluginRepositoriesAsync(IReadOnlyList<string> repositoryUrls, CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			_configuredPluginRepositoryUrls = GaiaPluginRepositoryManager.NormalizeRepositoryUrls(repositoryUrls);
			GaiaPluginRepositorySyncResult syncResult = await SynchronizeConfiguredPluginRepositoriesAsync(force: true, cancellationToken);
			_logger.LogInformation(
				"Gaia plugin repository configuration updated. RepositoryCount={RepositoryCount}, SyncErrors={SyncErrors}.",
				ResolvePluginRepositoryConfiguration().RepositoryUrls.Count,
				syncResult.Errors.Count);
			return CreatePluginRepositoriesSnapshot();
		}
		catch (Exception exception)
		{
			_lastError = exception.Message;
			_logger.LogError(exception, "Gaia could not update the plugin repository configuration.");
			throw;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<GaiaPluginRepositoriesSnapshot> ResyncPluginRepositoriesAsync(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			GaiaPluginRepositorySyncResult syncResult = await SynchronizeConfiguredPluginRepositoriesAsync(force: true, cancellationToken);
			_logger.LogInformation(
				"Gaia plugin repository resync completed. RepositoryCount={RepositoryCount}, SyncErrors={SyncErrors}.",
				ResolvePluginRepositoryConfiguration().RepositoryUrls.Count,
				syncResult.Errors.Count);
			return CreatePluginRepositoriesSnapshot();
		}
		catch (Exception exception)
		{
			_lastError = exception.Message;
			_logger.LogError(exception, "Gaia could not resynchronize the configured plugin repositories.");
			throw;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task InitializePluginRepositoriesAsync(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (!_pluginRepositoryManager.IsPluginCatalogEmpty(ResolvePluginRepositoriesRootPath()))
			{
				_logger.LogInformation("Gaia startup repository sync skipped because the local plugin catalog is already populated.");
				return;
			}

			GaiaPluginRepositorySyncResult syncResult = await SynchronizeConfiguredPluginRepositoriesAsync(force: true, cancellationToken);
			_logger.LogInformation(
				"Gaia startup repository sync completed. RepositoryCount={RepositoryCount}, SyncErrors={SyncErrors}.",
				ResolvePluginRepositoryConfiguration().RepositoryUrls.Count,
				syncResult.Errors.Count);
		}
		catch (Exception exception)
		{
			_lastError = exception.Message;
			_logger.LogError(exception, "Gaia could not initialize the plugin repositories on startup.");
			throw;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<GaiaDashboardState> InspectAsync(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			_logger.LogInformation("Gaia inspection started.");
			using DockerFirstRunBootstrapper bootstrapper = new DockerFirstRunBootstrapper(_options.SharedServicesRootPath);
			DockerFirstRunStatus status = await bootstrapper.InspectAsync(cancellationToken);
			ApplyRequiredPluginAvailability(status);
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
			ApplyRequiredPluginAvailability(status);
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
			ApplyRequiredPluginAvailability(status);
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
			ApplyRequiredPluginAvailability(refreshedStatus);
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

	private void ApplyRequiredPluginAvailability(DockerFirstRunStatus status)
	{
		if (status == null)
			return;

		RequiredPluginAvailabilityEvaluation evaluation = LocalPluginCatalogInspector.EvaluateRequiredPlugins(ResolvePluginRepositoriesRootPath(), _options.RequiredPluginIds);
		status.MissingRequiredPluginIds = evaluation.MissingRequiredPluginIds ?? Array.Empty<string>();

		List<string> operationErrors = new List<string>(status.OperationErrors ?? Array.Empty<string>());
		if (evaluation.Errors?.Count > 0)
			operationErrors.AddRange(evaluation.Errors);

		foreach (string pluginId in evaluation.MissingRequiredPluginIds ?? Array.Empty<string>())
			operationErrors.Add("Required plugin '" + pluginId + "' is not available under '" + ResolvePluginRepositoriesRootPath() + "'.");

		status.OperationErrors = operationErrors
			.Distinct(StringComparer.Ordinal)
			.ToArray();
	}

	private async Task<GaiaPluginRepositorySyncResult> SynchronizeConfiguredPluginRepositoriesAsync(bool force, CancellationToken cancellationToken)
	{
		GaiaPluginRepositoryConfiguration configuration = ResolvePluginRepositoryConfiguration();
		if (!force && !_pluginRepositoryManager.IsPluginCatalogEmpty(ResolvePluginRepositoriesRootPath()))
		{
			return new GaiaPluginRepositorySyncResult()
			{
				ManagedRepositories = _managedPluginRepositories,
				Messages = ["Gaia plugin repository sync skipped because the local plugin catalog is not empty."],
				Errors = _lastPluginRepositorySyncErrors,
				SynchronizedAtUtc = _lastPluginRepositorySyncUtc
			};
		}

		GaiaPluginRepositorySyncResult result = await _pluginRepositoryManager.SyncAsync(
			ResolvePluginRepositoriesRootPath(),
			configuration.RepositoryUrls,
			_managedPluginRepositories,
			cancellationToken);

		ApplyPluginRepositorySyncResult(result);
		await RefreshRuntimeStatusAsync(cancellationToken);
		TryPersistRuntimeState();
		return result;
	}

	private void ApplyPluginRepositorySyncResult(GaiaPluginRepositorySyncResult result)
	{
		_lastPluginRepositorySyncUtc = result?.SynchronizedAtUtc ?? DateTimeOffset.UtcNow;
		_managedPluginRepositories = result?.ManagedRepositories?.ToArray() ?? Array.Empty<GaiaManagedPluginRepositoryState>();
		_lastPluginRepositorySyncMessages = result?.Messages?.ToArray() ?? Array.Empty<string>();
		_lastPluginRepositorySyncErrors = result?.Errors?.ToArray() ?? Array.Empty<string>();

		if (_lastPluginRepositorySyncErrors.Count > 0)
			_lastError = string.Join(" | ", _lastPluginRepositorySyncErrors);
	}

	private async Task RefreshRuntimeStatusAsync(CancellationToken cancellationToken)
	{
		using DockerFirstRunBootstrapper bootstrapper = new DockerFirstRunBootstrapper(_options.SharedServicesRootPath);
		DockerFirstRunStatus status = await bootstrapper.InspectAsync(cancellationToken);
		ApplyRequiredPluginAvailability(status);

		List<string> operationMessages = new List<string>(status.OperationMessages ?? Array.Empty<string>());
		operationMessages.AddRange(_lastPluginRepositorySyncMessages ?? Array.Empty<string>());
		status.OperationMessages = operationMessages
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		List<string> operationErrors = new List<string>(status.OperationErrors ?? Array.Empty<string>());
		operationErrors.AddRange(_lastPluginRepositorySyncErrors ?? Array.Empty<string>());
		status.OperationErrors = operationErrors
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		ApplyStatus(status, isEnsureOperation: false);
	}

	private GaiaPluginRepositoryConfiguration ResolvePluginRepositoryConfiguration()
	{
		return _pluginRepositoryManager.ResolveConfiguration(_configuredPluginRepositoryUrls);
	}

	private GaiaPluginRepositoriesSnapshot CreatePluginRepositoriesSnapshot()
	{
		GaiaPluginRepositoryConfiguration configuration = ResolvePluginRepositoryConfiguration();
		string pluginRepositoriesRootPath = ResolvePluginRepositoriesRootPath();
		string managedRepositoriesRootPath = GaiaPluginRepositoryManager.ResolveManagedRepositoriesRootPath(pluginRepositoriesRootPath);

		IReadOnlyList<GaiaPluginRepositoryEntry> repositories = configuration.RepositoryUrls
			.Select(repositoryUrl =>
			{
				GaiaManagedPluginRepositoryState managedRepository = (_managedPluginRepositories ?? Array.Empty<GaiaManagedPluginRepositoryState>())
					.FirstOrDefault(repository => string.Equals(repository?.RepositoryUrl, repositoryUrl, StringComparison.OrdinalIgnoreCase));
				string localDirectoryName = managedRepository?.LocalDirectoryName;
				string localPath = string.IsNullOrWhiteSpace(localDirectoryName)
					? null
					: Path.Combine(managedRepositoriesRootPath, localDirectoryName);

				return new GaiaPluginRepositoryEntry()
				{
					RepositoryUrl = repositoryUrl,
					LocalDirectoryName = localDirectoryName,
					LocalPath = localPath,
					Exists = !string.IsNullOrWhiteSpace(localPath) && Directory.Exists(localPath),
					IsManaged = !string.IsNullOrWhiteSpace(localDirectoryName)
				};
			})
			.OrderBy(repository => repository.RepositoryUrl, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		return new GaiaPluginRepositoriesSnapshot()
		{
			PluginRepositoriesRootPath = pluginRepositoriesRootPath,
			ManagedRepositoriesRootPath = managedRepositoriesRootPath,
			ConfigurationSource = configuration.Source,
			UsesDefaultRepository = configuration.UsesDefaultRepository,
			IsPluginCatalogEmpty = _pluginRepositoryManager.IsPluginCatalogEmpty(pluginRepositoriesRootPath),
			LastSynchronizedUtc = _lastPluginRepositorySyncUtc,
			ConfiguredRepositoryUrls = configuration.RepositoryUrls,
			Repositories = repositories,
			LastSyncMessages = _lastPluginRepositorySyncMessages,
			LastSyncErrors = _lastPluginRepositorySyncErrors
		};
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
			_lastPluginRepositorySyncUtc = state.LastPluginRepositorySyncUtc;
			_lastAdminUiDeployments = state.AdminUiDeployments ?? Array.Empty<AdminUiDeploymentProjection>();
			_lastAdminUiDeploymentDiffs = state.AdminUiDeploymentDiffs ?? Array.Empty<AdminUiDeploymentDiff>();
			_configuredPluginRepositoryUrls = GaiaPluginRepositoryManager.NormalizeRepositoryUrls(state.ConfiguredPluginRepositoryUrls);
			_managedPluginRepositories = state.ManagedPluginRepositories ?? Array.Empty<GaiaManagedPluginRepositoryState>();
			_lastPluginRepositorySyncMessages = state.LastPluginRepositorySyncMessages ?? Array.Empty<string>();
			_lastPluginRepositorySyncErrors = state.LastPluginRepositorySyncErrors ?? Array.Empty<string>();
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
				LastPluginRepositorySyncUtc = _lastPluginRepositorySyncUtc,
				AdminUiDeployments = _lastAdminUiDeployments?.ToArray() ?? Array.Empty<AdminUiDeploymentProjection>(),
				AdminUiDeploymentDiffs = _lastAdminUiDeploymentDiffs?.ToArray() ?? Array.Empty<AdminUiDeploymentDiff>(),
				ConfiguredPluginRepositoryUrls = _configuredPluginRepositoryUrls?.ToArray() ?? Array.Empty<string>(),
				ManagedPluginRepositories = _managedPluginRepositories?.ToArray() ?? Array.Empty<GaiaManagedPluginRepositoryState>(),
				LastPluginRepositorySyncMessages = _lastPluginRepositorySyncMessages?.ToArray() ?? Array.Empty<string>(),
				LastPluginRepositorySyncErrors = _lastPluginRepositorySyncErrors?.ToArray() ?? Array.Empty<string>()
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

	private static GaiaAdminUiRouteDiagnostic CreateAdminUiRouteDiagnostic(PluginDeploymentDescriptor descriptor, string localProxyBaseUrl, string repositoryRootPath)
	{
		DockerAdminUiRoutePlan routePlan = DockerDeploymentPlanFactory.CreateAdminUiRoutePlan(descriptor);
		if (routePlan == null)
			return null;

		return new GaiaAdminUiRouteDiagnostic()
		{
			PluginId = descriptor.PluginId,
			PluginDisplayName = descriptor.DisplayName,
			RepositoryRootPath = repositoryRootPath,
			PublicBasePath = routePlan.PublicBasePath,
			ComposeServiceName = routePlan.ComposeServiceName,
			ServicePort = routePlan.ServicePort,
			TraefikResourceName = routePlan.TraefikResourceName,
			RouterRule = routePlan.RouterRule,
			LocalUrl = CombineLocalUrl(localProxyBaseUrl, routePlan.PublicBasePath),
			Labels = routePlan.Labels
		};
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

	private static string ResolveLocalProxyBaseUrl(DockerFirstRunStatus status)
	{
		DockerSharedServiceStatus traefikService = status?.SharedServices?
			.FirstOrDefault(service => string.Equals(service?.ServiceName, "traefik", StringComparison.OrdinalIgnoreCase) && service.IsRunning);

		int? hostPort = TryResolveTcpHostPort(traefikService?.PublishedPorts);
		if (!hostPort.HasValue)
			return null;

		return hostPort.Value == 80
			? "http://127.0.0.1"
			: "http://127.0.0.1:" + hostPort.Value;
	}

	private static int? TryResolveTcpHostPort(IReadOnlyList<string> publishedPorts)
	{
		foreach (string publishedPort in publishedPorts ?? Array.Empty<string>())
		{
			if (string.IsNullOrWhiteSpace(publishedPort))
				continue;

			string[] protocolParts = publishedPort.Split('/');
			if (protocolParts.Length != 2 || !string.Equals(protocolParts[1], "tcp", StringComparison.OrdinalIgnoreCase))
				continue;

			string[] bindingParts = protocolParts[0].Split(':');
			if (bindingParts.Length != 2)
				continue;

			if (int.TryParse(bindingParts[0], out int hostPort) && hostPort > 0)
				return hostPort;
		}

		return null;
	}

	private static string CombineLocalUrl(string baseUrl, string publicBasePath)
	{
		if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(publicBasePath))
			return null;

		return baseUrl.TrimEnd('/') + publicBasePath;
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

public sealed class GaiaAdminUiRouteDiagnostic
{
	public string PluginId { get; set; }

	public string PluginDisplayName { get; set; }

	public string RepositoryRootPath { get; set; }

	public string PublicBasePath { get; set; }

	public string ComposeServiceName { get; set; }

	public int ServicePort { get; set; }

	public string TraefikResourceName { get; set; }

	public string RouterRule { get; set; }

	public string LocalUrl { get; set; }

	public IReadOnlyDictionary<string, string> Labels { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

	public string Error { get; set; }
}