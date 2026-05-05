using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Federation.Configuration;
using MediaBrowser.Controller.Federation.Peers;
using MediaBrowser.Controller.Federation.Signing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Implementations.Federation;

/// <summary>
/// Background worker that drains <see cref="FederationActorQueue"/> rows. One queue per remote
/// actor; items processed sequentially by <c>DateCreated</c>. A failure on any item pauses the
/// whole queue with exponential backoff (capped at 24h) — other actors' queues are unaffected
/// because each has its own <c>NextAttemptAt</c>.
/// </summary>
public sealed class FederationActorQueueWorker : BackgroundService
{
    private const int PollIntervalSeconds = 30;
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(24);

    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFederationSigningService _signingService;
    private readonly IConfigurationManager _configManager;
    private readonly IFederationBackfillService _backfillService;
    private readonly ILogger<FederationActorQueueWorker> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationActorQueueWorker"/> class.
    /// </summary>
    /// <param name="dbProvider">The database provider.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="signingService">The signing service.</param>
    /// <param name="configManager">The configuration manager.</param>
    /// <param name="backfillService">The backfill response handler.</param>
    /// <param name="logger">The logger.</param>
    public FederationActorQueueWorker(
        IDbContextFactory<JellyfinDbContext> dbProvider,
        IHttpClientFactory httpClientFactory,
        IFederationSigningService signingService,
        IConfigurationManager configManager,
        IFederationBackfillService backfillService,
        ILogger<FederationActorQueueWorker> logger)
    {
        _dbProvider = dbProvider;
        _httpClientFactory = httpClientFactory;
        _signingService = signingService;
        _configManager = configManager;
        _backfillService = backfillService;
        _logger = logger;
    }

    /// <inheritdoc/>
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
                await ProcessReadyQueuesAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing federation actor queues");
            }

            await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessReadyQueuesAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        int[] readyQueueIds;

        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            readyQueueIds = await dbContext.FederationActorQueues
                .Where(q => q.NextAttemptAt <= now)
                .Select(q => q.Id)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var queueId in readyQueueIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await ProcessQueueAsync(queueId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessQueueAsync(int queueId, CancellationToken cancellationToken)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var queue = await dbContext.FederationActorQueues
                .Include(q => q.Actor)
                .FirstOrDefaultAsync(q => q.Id == queueId, cancellationToken)
                .ConfigureAwait(false);
            if (queue is null)
            {
                return;
            }

            var client = _httpClientFactory.CreateClient();

            while (!cancellationToken.IsCancellationRequested)
            {
                var item = await dbContext.FederationActorQueueItems
                    .Where(i => i.QueueId == queueId)
                    .OrderBy(i => i.DateCreated)
                    .ThenBy(i => i.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (item is null)
                {
                    // Nothing more to do for this actor; reset backoff.
                    if (queue.AttemptCount != 0)
                    {
                        queue.AttemptCount = 0;
                        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }

                    return;
                }

                var success = await ProcessItemAsync(client, queue.Actor, item, cancellationToken).ConfigureAwait(false);
                if (success)
                {
                    dbContext.FederationActorQueueItems.Remove(item);
                    queue.AttemptCount = 0;
                    queue.NextAttemptAt = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // Failure: bump backoff for the whole queue, stop draining.
                queue.AttemptCount += 1;
                queue.NextAttemptAt = DateTime.UtcNow.Add(ComputeBackoff(queue.AttemptCount));
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogDebug(
                    "Queue for {Actor} paused: attempt #{Attempt}, next at {Next:O}",
                    queue.Actor.Url,
                    queue.AttemptCount,
                    queue.NextAttemptAt);
                return;
            }
        }
    }

    private async Task<bool> ProcessItemAsync(HttpClient client, FederationActor actor, FederationActorQueueItem item, CancellationToken cancellationToken)
    {
        return item.Kind switch
        {
            FederationActorQueueItemKind.Deliver => await DeliverAsync(client, item, cancellationToken).ConfigureAwait(false),
            FederationActorQueueItemKind.Fetch => await FetchAsync(client, actor, item, cancellationToken).ConfigureAwait(false),
            _ => true // unknown kind — drop (shouldn't happen; drained to avoid wedging the queue)
        };
    }

    private async Task<bool> DeliverAsync(HttpClient client, FederationActorQueueItem item, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, item.TargetUrl);
        request.Content = new StringContent(item.Body ?? string.Empty, Encoding.UTF8, "application/activity+json");
        try
        {
            await _signingService.SignAsync(request).ConfigureAwait(false);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            _logger.LogWarning("Deliver to {Url} returned {Status}", item.TargetUrl, (int)response.StatusCode);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Deliver to {Url} failed", item.TargetUrl);
            return false;
        }
    }

    private async Task<bool> FetchAsync(HttpClient client, FederationActor actor, FederationActorQueueItem item, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, item.TargetUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/activity+json"));

        try
        {
            await _signingService.SignAsync(request).ConfigureAwait(false);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Fetch of {Url} returned {Status}", item.TargetUrl, (int)response.StatusCode);
                return false;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            await _backfillService.HandleOutboxResponseAsync(actor, body, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Fetch of {Url} failed", item.TargetUrl);
            return false;
        }
    }

    private static TimeSpan ComputeBackoff(int attemptCount)
    {
        // Exponential: 1m, 2m, 4m, 8m, ... capped at 24h. Never gives up entirely.
        var baseMs = BaseBackoff.TotalMilliseconds;
        var capped = Math.Min(baseMs * Math.Pow(2, Math.Max(0, attemptCount - 1)), MaxBackoff.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(capped);
    }
}
