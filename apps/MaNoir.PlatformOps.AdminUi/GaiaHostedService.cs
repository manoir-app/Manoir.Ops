using System;
using System.Threading;
using System.Threading.Tasks;
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
		await InitializePluginRepositoriesAsync(stoppingToken);

		if (_options.AutoEnsureSharedServicesOnStartup)
			await RunEnsureAsync(stoppingToken);
		else
			await RunInspectAsync(stoppingToken);

		TimeSpan interval = TimeSpan.FromSeconds(_options.EnsureIntervalSeconds > 0 ? _options.EnsureIntervalSeconds : 300);
		using PeriodicTimer timer = new PeriodicTimer(interval);

		while (await timer.WaitForNextTickAsync(stoppingToken))
			await RunEnsureAsync(stoppingToken);
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

	private async Task InitializePluginRepositoriesAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _gaia.InitializePluginRepositoriesAsync(cancellationToken);
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			_logger.LogError(exception, "Gaia plugin repository initialization failed.");
		}
	}
}