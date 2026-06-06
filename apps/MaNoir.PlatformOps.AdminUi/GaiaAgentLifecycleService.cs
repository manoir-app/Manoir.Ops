using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
	private bool _isRegistered;
	private bool _isRuntimeEnvironmentConfigured;
	private bool _hasReportedWaitingForMinimumVital;

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
		using PeriodicTimer timer = new(_runtime.HeartbeatInterval);

		while (await timer.WaitForNextTickAsync(stoppingToken))
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

				continue;
			}

			_hasReportedWaitingForMinimumVital = false;

			EnsureRuntimeEnvironmentConfigured();

			if (!_isRegistered)
			{
				await TryRegisterAsync(AgentState.Ready, "Minimum vital ready", stoppingToken);
				continue;
			}

			await TryHeartbeatAsync(AgentState.Ready, "Running", stoppingToken);
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
}