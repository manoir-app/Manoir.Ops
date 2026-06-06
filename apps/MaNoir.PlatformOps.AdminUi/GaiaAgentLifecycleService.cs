using System;
using System.Threading;
using System.Threading.Tasks;
using MaNoir.Core.Agents;
using MaNoir.Core.Contracts.Models.Agents;
using Microsoft.Extensions.Hosting;

namespace MaNoir.PlatformOps.AdminUi;

public sealed class GaiaAgentLifecycleService : BackgroundService
{
	private readonly AgentRegistryLogic _agentRegistryLogic;
	private readonly GaiaOperationsService _gaia;
	private readonly GaiaAgentRuntime _runtime;
	private bool _isRegistered;
	private bool _hasReportedWaitingForMinimumVital;

	public GaiaAgentLifecycleService(GaiaOperationsService gaia, GaiaAgentRuntime runtime)
	{
		_agentRegistryLogic = new AgentRegistryLogic();
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
		if (_isRegistered)
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
			RegisteredAgent agent = await _agentRegistryLogic.RegisterAsync(_runtime.CreateRegistrationRequest(state, statusMessage), cancellationToken);
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
			await _agentRegistryLogic.HeartbeatAsync(_runtime.CreateHeartbeatRequest(state, statusMessage), cancellationToken);
			_runtime.ReportHeartbeat();
		}
		catch (Exception ex)
		{
			_isRegistered = false;
			_runtime.ReportHeartbeatFailed(ex);
		}
	}
}