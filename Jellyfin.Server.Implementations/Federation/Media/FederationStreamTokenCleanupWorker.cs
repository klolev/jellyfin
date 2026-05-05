using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Federation.Configuration;
using MediaBrowser.Controller.Federation.Media;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Implementations.Federation.Media;

/// <summary>
/// Periodically sweeps expired <see cref="Jellyfin.Database.Implementations.Entities.Federation.FederationStreamToken"/>
/// rows. Expired tokens already fail validation, but a long-running instance would otherwise
/// accumulate them indefinitely.
/// </summary>
public sealed class FederationStreamTokenCleanupWorker : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    private readonly IFederationStreamTokenService _tokenService;
    private readonly IConfigurationManager _configManager;
    private readonly ILogger<FederationStreamTokenCleanupWorker> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationStreamTokenCleanupWorker"/> class.
    /// </summary>
    /// <param name="tokenService">Instance of the <see cref="IFederationStreamTokenService"/> interface.</param>
    /// <param name="configManager">Instance of the <see cref="IConfigurationManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{FederationStreamTokenCleanupWorker}"/> interface.</param>
    public FederationStreamTokenCleanupWorker(
        IFederationStreamTokenService tokenService,
        IConfigurationManager configManager,
        ILogger<FederationStreamTokenCleanupWorker> logger)
    {
        _tokenService = tokenService;
        _configManager = configManager;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configManager.GetFederationConfiguration().Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var removed = await _tokenService.DeleteExpiredAsync(stoppingToken).ConfigureAwait(false);
                if (removed > 0)
                {
                    _logger.LogDebug("Swept {Count} expired federation stream tokens", removed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error sweeping expired federation stream tokens");
            }

            await Task.Delay(SweepInterval, stoppingToken).ConfigureAwait(false);
        }
    }
}
