using System;
using System.Threading;
using System.Threading.Tasks;
using Home.Common.Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MaNoir.PlatformOps.AdminUi;

public sealed class GaiaMessageRouter
{
	private readonly GaiaOperationsService _gaia;
	private readonly IHostApplicationLifetime _hostApplicationLifetime;
	private readonly ILogger<GaiaMessageRouter> _logger;
	private readonly GaiaAgentRuntime _runtime;

	public GaiaMessageRouter(GaiaOperationsService gaia, GaiaAgentRuntime runtime, IHostApplicationLifetime hostApplicationLifetime, ILogger<GaiaMessageRouter> logger)
	{
		_gaia = gaia ?? throw new ArgumentNullException(nameof(gaia));
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		_hostApplicationLifetime = hostApplicationLifetime ?? throw new ArgumentNullException(nameof(hostApplicationLifetime));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public MessageResponse HandleMessage(MessageOrigin origin, string topic, string messageBody)
	{
		if (string.IsNullOrWhiteSpace(topic))
			return MessageResponse.GenericFail;

		string normalizedTopic = topic.Trim().ToLowerInvariant();
		switch (normalizedTopic)
		{
			case "gaia.stop":
				_logger.LogWarning("Gaia received a stop request over NATS.");
				_hostApplicationLifetime.StopApplication();
				return MessageResponse.OK;

			case "gaia.refresh-reverse-proxy":
			case "gaia.refresh-certificate":
				ScheduleOperation(normalizedTopic, "ensure-minimum-vital", cancellationToken => _gaia.EnsureMinimumVitalAsync(cancellationToken));
				return MessageResponse.OK;

			case "gaia.deployments":
			case "system.extensions.create":
			case "system.extensions.restart":
			case "system.extensions.terminate":
				ScheduleOperation(normalizedTopic, "refresh-and-restart-all-plugins", cancellationToken => _gaia.RefreshAndRestartAllPluginsAsync(cancellationToken));
				return MessageResponse.OK;

			default:
				_logger.LogInformation("Gaia received NATS topic {Topic} from {Origin} and ignored it.", normalizedTopic, origin);
				return MessageResponse.OK;
		}
	}

	private void ScheduleOperation(string topic, string operationName, Func<CancellationToken, Task> operation)
	{
		_runtime.ReportMessageTriggeredOperation(topic, operationName);
		_ = Task.Run(async () =>
		{
			try
			{
				await operation(CancellationToken.None);
			}
			catch (Exception exception)
			{
				_logger.LogError(exception, "Gaia could not execute the operation {OperationName} triggered by topic {Topic}.", operationName, topic);
			}
		});
	}
}