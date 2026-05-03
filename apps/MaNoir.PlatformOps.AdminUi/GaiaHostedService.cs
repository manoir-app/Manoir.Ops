using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Home.Common;
using Home.Common.Messages;
using NATS.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MaNoir.PlatformOps.AdminUi;

public sealed class GaiaHostedService : BackgroundService
{
	private readonly GaiaOptions _options;
	private readonly GaiaOperationsService _gaia;
	private readonly ILogger<GaiaHostedService> _logger;

	public GaiaHostedService(GaiaOptions options, GaiaOperationsService gaia, ILogger<GaiaHostedService> logger)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_gaia = gaia ?? throw new ArgumentNullException(nameof(gaia));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_logger.LogInformation("Gaia hosted service started. AutoEnsureSharedServicesOnStartup={AutoEnsureSharedServicesOnStartup}, EnsureIntervalSeconds={EnsureIntervalSeconds}.", _options.AutoEnsureSharedServicesOnStartup, _options.EnsureIntervalSeconds);
		Task meshPublicBaseDomainListenerTask = ShouldListenToMeshPublicBaseDomainChanges()
			? ListenToMeshPublicBaseDomainChangesAsync(stoppingToken)
			: Task.CompletedTask;

		if (_options.AutoEnsureSharedServicesOnStartup)
			await RunEnsureAsync(stoppingToken);
		else
			await RunInspectAsync(stoppingToken);

		TimeSpan interval = TimeSpan.FromSeconds(_options.EnsureIntervalSeconds > 0 ? _options.EnsureIntervalSeconds : 300);
		using PeriodicTimer timer = new PeriodicTimer(interval);

		while (await timer.WaitForNextTickAsync(stoppingToken))
			await RunEnsureAsync(stoppingToken);

		await meshPublicBaseDomainListenerTask;
	}

	private bool ShouldListenToMeshPublicBaseDomainChanges()
	{
		return GaiaEdgeCertificateRuntimeResolver.ShouldReactToMeshPublicBaseDomainChanges();
	}

	private async Task ListenToMeshPublicBaseDomainChangesAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				using IConnection connection = NatsInterprocess.GetConnection();
				using IAsyncSubscription subscription = connection.SubscribeAsync(MeshPublicBaseDomainChangedMessage.TopicName, (sender, args) =>
				{
					string payload = Encoding.UTF8.GetString(args.Message.Data, 0, args.Message.Data.Length);
					_ = HandleMeshPublicBaseDomainChangedAsync(payload, cancellationToken);
				});
				subscription.Start();

				while (!cancellationToken.IsCancellationRequested)
					await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
			{
				_logger.LogWarning(exception, "Gaia mesh public base domain listener failed. Retrying shortly.");
				await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
			}
		}
	}

	private async Task HandleMeshPublicBaseDomainChangedAsync(string payload, CancellationToken cancellationToken)
	{
		try
		{
			MeshPublicBaseDomainChangedMessage message = BaseMessage.ReadAs<MeshPublicBaseDomainChangedMessage>(payload);
			_logger.LogInformation(
				"Gaia received mesh public base domain change for {MeshId}: {PreviousDomain} -> {NewDomain}. Triggering convergence.",
				message?.MeshId,
				message?.PreviousPublicBaseDomain,
				message?.PublicBaseDomain);
			await _gaia.EnsureMinimumVitalAsync(cancellationToken);
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			_logger.LogError(exception, "Gaia could not process the mesh public base domain change notification.");
		}
	}

	private async Task RunInspectAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _gaia.InspectAsync(cancellationToken);
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			_logger.LogError(exception, "Gaia inspection cycle failed.");
		}
	}

	private async Task RunEnsureAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _gaia.EnsureMinimumVitalAsync(cancellationToken);
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			_logger.LogError(exception, "Gaia ensure cycle failed.");
		}
	}
}