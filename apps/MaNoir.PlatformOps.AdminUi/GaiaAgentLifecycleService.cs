using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using MaNoir.Core.Agents;
using MaNoir.Core.Contracts.Models.Agents;
using Microsoft.Extensions.Hosting;
using MaNoir.PlatformOps.Provider.Docker;

namespace MaNoir.PlatformOps.AdminUi;

public sealed class GaiaAgentLifecycleService : BackgroundService
{
	private readonly GaiaOperationsService _gaia;
	private readonly GaiaAgentRuntime _runtime;
	private AgentRegistryLogic _agentRegistryLogic;
	private bool _isAttachedToSharedNetwork;
	private bool _isRegistered;
	private bool _isRuntimeEnvironmentConfigured;
	private bool _hasReportedWaitingForMinimumVital;
	private bool _hasReportedWaitingForSharedNetwork;

	public GaiaAgentLifecycleService(GaiaOperationsService gaia, GaiaAgentRuntime runtime)
	{
		_gaia = gaia ?? throw new ArgumentNullException(nameof(gaia));
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
	}

	public bool IsReadyForMessages => _isRegistered && _gaia.IsMinimumVitalReady;

	public override async Task StartAsync(CancellationToken cancellationToken)
	{
		_runtime.ReportStarting();
		await base.StartAsync(cancellationToken);
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			if (!_gaia.IsMinimumVitalReady)
			{
				if (_isRegistered)
				{
					await TryHeartbeatAsync(AgentState.Degraded, "Waiting for minimum vital", stoppingToken);
				}
				else if (!_hasReportedWaitingForMinimumVital)
				{
					_runtime.ReportWaitingForMinimumVital();
					_hasReportedWaitingForMinimumVital = true;
				}

				await DelayAsync(TimeSpan.FromSeconds(1), stoppingToken);
				continue;
			}

			_hasReportedWaitingForMinimumVital = false;

			if (!await EnsureSharedNetworkConnectedAsync(stoppingToken))
			{
				await DelayAsync(TimeSpan.FromSeconds(1), stoppingToken);
				continue;
			}

			EnsureRuntimeEnvironmentConfigured();

			if (!_isRegistered)
			{
				await TryRegisterAsync(AgentState.Ready, "Minimum vital ready", stoppingToken);
				if (!_isRegistered)
					await DelayAsync(TimeSpan.FromSeconds(5), stoppingToken);
				continue;
			}

			await TryHeartbeatAsync(AgentState.Ready, "Running", stoppingToken);
			await DelayAsync(_runtime.HeartbeatInterval, stoppingToken);
		}
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		if (_isRegistered && _agentRegistryLogic != null)
		{
			try
			{
				await _agentRegistryLogic.HeartbeatAsync(_runtime.CreateHeartbeatRequest(AgentState.Stopping, "Stopping"), cancellationToken);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] [Gaia] DEBUG Stopping heartbeat update failed for {_runtime.AgentId}. {ex.Message}");
			}
		}

		_runtime.ReportStopping();
		await base.StopAsync(cancellationToken);
	}

	private async Task TryRegisterAsync(AgentState state, string statusMessage, CancellationToken cancellationToken)
	{
		try
		{
			RegisteredAgent agent = await GetAgentRegistryLogic().RegisterAsync(_runtime.CreateRegistrationRequest(state, statusMessage), cancellationToken);
			_isRegistered = true;
			_runtime.ReportRegistrationSucceeded(agent);
		}
		catch (Exception ex)
		{
			_isRegistered = false;
			_runtime.ReportRegistrationFailed(ex);
		}
	}

	private async Task TryHeartbeatAsync(AgentState state, string statusMessage, CancellationToken cancellationToken)
	{
		try
		{
			await GetAgentRegistryLogic().HeartbeatAsync(_runtime.CreateHeartbeatRequest(state, statusMessage), cancellationToken);
			_runtime.ReportHeartbeat();
		}
		catch (Exception ex)
		{
			_isRegistered = false;
			_runtime.ReportHeartbeatFailed(ex);
		}
	}

	private AgentRegistryLogic GetAgentRegistryLogic()
	{
		if (_agentRegistryLogic != null)
			return _agentRegistryLogic;

		EnsureRuntimeEnvironmentConfigured();
		_agentRegistryLogic = new AgentRegistryLogic();
		return _agentRegistryLogic;
	}

	private void EnsureRuntimeEnvironmentConfigured()
	{
		if (_isRuntimeEnvironmentConfigured)
			return;

		Dictionary<string, string> configuredVariables = new(StringComparer.Ordinal);
		foreach (KeyValuePair<string, string> pair in DockerAutomaticEnvironmentVariables.CreateVariablesByName(_runtime.AgentId))
		{
			string currentValue = Environment.GetEnvironmentVariable(pair.Key);
			if (string.Equals(currentValue, pair.Value, StringComparison.Ordinal))
				continue;

			Environment.SetEnvironmentVariable(pair.Key, pair.Value);
			configuredVariables[pair.Key] = pair.Value;
		}

		string mongoConnectionString = Environment.GetEnvironmentVariable("MONGODB_CONNECTIONSTRING");
		if (string.IsNullOrWhiteSpace(mongoConnectionString))
		{
			string mongoHost = Environment.GetEnvironmentVariable("MONGODB_SERVICE_HOST");
			string mongoPort = Environment.GetEnvironmentVariable("MONGODB_SERVICE_PORT");
			if (!string.IsNullOrWhiteSpace(mongoHost) && !string.IsNullOrWhiteSpace(mongoPort))
			{
				string resolvedConnectionString = $"mongodb://{mongoHost.Trim()}:{mongoPort.Trim()}";
				Environment.SetEnvironmentVariable("MONGODB_CONNECTIONSTRING", resolvedConnectionString);
				configuredVariables["MONGODB_CONNECTIONSTRING"] = resolvedConnectionString;
			}
		}

		_isRuntimeEnvironmentConfigured = true;
		_runtime.ReportEnvironmentConfigured(configuredVariables);
	}

	private async Task<bool> EnsureSharedNetworkConnectedAsync(CancellationToken cancellationToken)
	{
		if (_isAttachedToSharedNetwork)
			return true;

		string networkName = DockerRuntimeSpecFactory.SharedNetworkName;
		string containerId = ResolveCurrentContainerId();
		if (string.IsNullOrWhiteSpace(containerId))
		{
			if (!_hasReportedWaitingForSharedNetwork)
			{
				_runtime.ReportSharedNetworkUnavailable(networkName);
				_hasReportedWaitingForSharedNetwork = true;
			}

			return false;
		}

		try
		{
			using DockerClient dockerClient = DockerClientFactory.CreateClient();
			IList<NetworkResponse> networks = await dockerClient.Networks.ListNetworksAsync(new NetworksListParameters(), cancellationToken);
			NetworkResponse network = networks.FirstOrDefault(candidate => string.Equals(candidate?.Name, networkName, StringComparison.OrdinalIgnoreCase));
			if (network == null)
			{
				if (!_hasReportedWaitingForSharedNetwork)
				{
					_runtime.ReportSharedNetworkUnavailable(networkName);
					_hasReportedWaitingForSharedNetwork = true;
				}

				return false;
			}

			await dockerClient.Networks.ConnectNetworkAsync(network.ID, new NetworkConnectParameters()
			{
				Container = containerId
			}, cancellationToken);

			_isAttachedToSharedNetwork = true;
			_hasReportedWaitingForSharedNetwork = false;
			_runtime.ReportSharedNetworkConnected(networkName);
			return true;
		}
		catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
		{
			_isAttachedToSharedNetwork = true;
			_hasReportedWaitingForSharedNetwork = false;
			_runtime.ReportSharedNetworkConnected(networkName);
			return true;
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			_runtime.ReportSharedNetworkConnectionFailed(networkName, exception);
			return false;
		}
	}

	private static string ResolveCurrentContainerId()
	{
		string hostname = Environment.GetEnvironmentVariable("HOSTNAME");
		if (!string.IsNullOrWhiteSpace(hostname))
			return hostname.Trim();

		return string.IsNullOrWhiteSpace(Environment.MachineName) ? null : Environment.MachineName.Trim();
	}

	private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(delay, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}
}