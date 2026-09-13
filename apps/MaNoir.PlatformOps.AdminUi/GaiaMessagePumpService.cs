using System;
using System.Threading;
using System.Threading.Tasks;
using Home.Common;
using Microsoft.Extensions.Hosting;

namespace MaNoir.PlatformOps.AdminUi;

public sealed class GaiaMessagePumpService : BackgroundService
{
	private readonly GaiaMessageRouter _messageRouter;
	private readonly GaiaAgentRuntime _runtime;
	private readonly GaiaOperationsService _gaia;

	public GaiaMessagePumpService(GaiaMessageRouter messageRouter, GaiaAgentRuntime runtime, GaiaOperationsService gaia)
	{
		_messageRouter = messageRouter ?? throw new ArgumentNullException(nameof(messageRouter));
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		_gaia = gaia ?? throw new ArgumentNullException(nameof(gaia));
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!_gaia.IsMinimumVitalReady && !stoppingToken.IsCancellationRequested)
			await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

		if (stoppingToken.IsCancellationRequested)
			return;

		_runtime.ReportTopicsSubscribed();

		Task listenerTask = Task.Run(() => NatsInterprocessListener.Run(_runtime.MessageTopics, _messageRouter.HandleMessage), CancellationToken.None);
		try
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			NatsInterprocessListener.Stop();
			_runtime.ReportInterprocessStopped();
			await listenerTask;
		}
	}
}