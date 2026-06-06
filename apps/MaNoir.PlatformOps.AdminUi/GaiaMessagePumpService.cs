using System;
using System.Threading;
using System.Threading.Tasks;
using Home.Common;
using Microsoft.Extensions.Hosting;

namespace MaNoir.PlatformOps.AdminUi;

public sealed class GaiaMessagePumpService : BackgroundService
{
	private readonly GaiaAgentLifecycleService _lifecycle;
	private readonly GaiaMessageRouter _messageRouter;
	private readonly GaiaAgentRuntime _runtime;

	public GaiaMessagePumpService(GaiaAgentLifecycleService lifecycle, GaiaMessageRouter messageRouter, GaiaAgentRuntime runtime)
	{
		_lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
		_messageRouter = messageRouter ?? throw new ArgumentNullException(nameof(messageRouter));
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!_lifecycle.IsReadyForMessages)
		{
			await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
		}

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