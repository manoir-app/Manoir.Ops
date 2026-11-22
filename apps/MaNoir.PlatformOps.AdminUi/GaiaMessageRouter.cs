using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Home.Common.Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MaNoir.PlatformOps.AdminUi;

public sealed class GaiaMessageRouter
{
	private readonly GaiaOperationsService _gaia;
	private readonly IHostApplicationLifetime _hostApplicationLifetime;
	private readonly ILogger<GaiaMessageRouter> _logger;
	private readonly GaiaAgentRuntime _runtime;
	private readonly ConcurrentDictionary<string, GaiaPluginInstallationStatusResponse> _pluginInstallations = new();
	private readonly ConcurrentDictionary<string, DateTimeOffset> _pluginInstallationUpdates = new();

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
			case GaiaPluginInstallationMessage.TopicName:
				return AcceptPluginInstallation(messageBody);

			case GaiaPluginInstallationMessage.StatusTopicName:
				return GetPluginInstallationStatus(messageBody);

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

	private MessageResponse AcceptPluginInstallation(string messageBody)
	{
		GaiaPluginInstallationMessage message = JsonConvert.DeserializeObject<GaiaPluginInstallationMessage>(messageBody);
		if (message == null || string.IsNullOrWhiteSpace(message.RepositoryUrl))
			return new GaiaPluginInstallationResponse()
			{
				Response = "fail",
				Status = "rejected",
				Message = "A repository URL is required."
			};

		string operationId = string.IsNullOrWhiteSpace(message.OperationId)
			? Guid.NewGuid().ToString("N")
			: message.OperationId.Trim();
		string repositoryUrl = message.RepositoryUrl.Trim();
		SetInstallationStatus(operationId, repositoryUrl, "accepted", "accepted", "Plugin installation was accepted by Gaia.");
		_runtime.ReportPluginInstallationAccepted(operationId, repositoryUrl);
		_ = Task.Run(async () =>
		{
			try
			{
				await _gaia.InstallPluginAsync(repositoryUrl, CancellationToken.None, (status, step, progressMessage) =>
				{
					SetInstallationStatus(operationId, repositoryUrl, status, step, progressMessage);
				});
				SetInstallationStatus(operationId, repositoryUrl, "completed", "completed", "Plugin installation completed.");
			}
			catch (Exception exception)
			{
				SetInstallationStatus(operationId, repositoryUrl, "failed", "failed", exception.Message);
				_logger.LogError(exception, "Gaia could not install plugin repository {RepositoryUrl} for operation {OperationId}.", repositoryUrl, operationId);
			}
		});

		return new GaiaPluginInstallationResponse()
		{
			Response = "accepted",
			OperationId = operationId,
			RepositoryUrl = repositoryUrl,
			Status = "accepted",
			Message = "Plugin installation was accepted by Gaia."
		};
	}

	private MessageResponse GetPluginInstallationStatus(string messageBody)
	{
		GaiaPluginInstallationStatusRequest request = JsonConvert.DeserializeObject<GaiaPluginInstallationStatusRequest>(messageBody);
		if (request == null || string.IsNullOrWhiteSpace(request.OperationId))
			return MessageResponse.GenericFail;

		return _pluginInstallations.TryGetValue(request.OperationId.Trim(), out GaiaPluginInstallationStatusResponse status)
			? status
			: new GaiaPluginInstallationStatusResponse()
			{
				Response = "fail",
				OperationId = request.OperationId,
				Status = "not-found",
				Message = "The installation operation was not found."
			};
	}

	/// <summary>
	/// Returns the most recent plugin installations known to this Gaia instance, newest first.
	/// The state is held in memory: it is lost when Gaia restarts.
	/// </summary>
	public IReadOnlyList<GaiaPluginInstallationView> GetPluginInstallations(int maxCount = 10)
	{
		return _pluginInstallations
			.Select(entry => new GaiaPluginInstallationView()
			{
				OperationId = entry.Key,
				RepositoryUrl = entry.Value.RepositoryUrl,
				Status = entry.Value.Status,
				Step = entry.Value.Step,
				Message = entry.Value.Message,
				UpdatedAtUtc = _pluginInstallationUpdates.TryGetValue(entry.Key, out DateTimeOffset updatedAtUtc) ? updatedAtUtc : DateTimeOffset.MinValue
			})
			.OrderByDescending(view => view.UpdatedAtUtc)
			.Take(maxCount)
			.ToArray();
	}

	private void SetInstallationStatus(string operationId, string repositoryUrl, string status, string step, string message)
	{
		_pluginInstallationUpdates[operationId] = DateTimeOffset.UtcNow;
		_pluginInstallations[operationId] = new GaiaPluginInstallationStatusResponse()
		{
			Response = "ok",
			OperationId = operationId,
			RepositoryUrl = repositoryUrl,
			Status = status,
			Step = step,
			Message = message
		};
	}
}

public sealed class GaiaPluginInstallationView
{
	public string OperationId { get; set; }

	public string RepositoryUrl { get; set; }

	public string Status { get; set; }

	public string Step { get; set; }

	public string Message { get; set; }

	public DateTimeOffset UpdatedAtUtc { get; set; }
}
