using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Federation;
using MediaBrowser.Controller.Federation.Configuration;
using MediaBrowser.Controller.Federation.Library;
using MediaBrowser.Controller.Federation.Peers;
using MediaBrowser.Model.Federation.ActivityStreams;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ActivityStreams = MediaBrowser.Model.Federation.ActivityStreams;

namespace Jellyfin.Server.Implementations.Federation;

/// <summary>
/// IFederationService.
/// </summary>
public class FederationService : IFederationService
{
    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;
    private readonly IFederationPeerService _peerService;
    private readonly IFederationLibraryIngester _libraryIngester;
    private readonly IConfigurationManager _configManager;
    private readonly ILogger<FederationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationService"/> class.
    /// </summary>
    /// <param name="dbProvider">The database provider.</param>
    /// <param name="peerService">The peer service.</param>
    /// <param name="libraryIngester">The library ingester.</param>
    /// <param name="configManager">The configuration manager.</param>
    /// <param name="logger">The logger.</param>
    public FederationService(
        IDbContextFactory<JellyfinDbContext> dbProvider,
        IFederationPeerService peerService,
        IFederationLibraryIngester libraryIngester,
        IConfigurationManager configManager,
        ILogger<FederationService> logger)
    {
        _dbProvider = dbProvider;
        _peerService = peerService;
        _libraryIngester = libraryIngester;
        _configManager = configManager;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ActivityStreams.Object> GetOutboxAsync(uint? page, uint limit, string? signingActorUrl)
    {
        limit = Math.Clamp(limit, 1, 100);

        // FederationEnabled policy + startup validator guarantee Hostname is populated.
        var config = _configManager.GetFederationConfiguration();
        var outboxUrl = config.OutboxURL;

        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            if (!await dbContext.IsFollowerAsync(signingActorUrl).ConfigureAwait(false))
            {
                return BuildEmptyResponse(outboxUrl, page, limit);
            }

            return page is null
                ? await BuildSummaryAsync(dbContext, outboxUrl, limit).ConfigureAwait(false)
                : await BuildPageAsync(dbContext, outboxUrl, page.Value, limit).ConfigureAwait(false);
        }
    }

    private static ActivityStreams.Object BuildEmptyResponse(string outboxUrl, uint? page, uint limit)
    {
        if (page is null)
        {
            return new OrderedCollection { Id = outboxUrl, TotalItems = 0 };
        }

        return new OrderedCollectionPage
        {
            Id = $"{outboxUrl}?page={page}&limit={limit}",
            PartOf = outboxUrl,
            TotalItems = 0,
            OrderedItems = Array.Empty<ActivityStreams.Object>()
        };
    }

    private static async Task<OrderedCollection> BuildSummaryAsync(JellyfinDbContext dbContext, string outboxUrl, uint limit)
    {
        var totalItems = await dbContext.FederationOutboxActivities
            .CountAsync()
            .ConfigureAwait(false);

        var collection = new OrderedCollection
        {
            Id = outboxUrl,
            TotalItems = totalItems,
            First = $"{outboxUrl}?page=0&limit={limit}"
        };

        if (totalItems > 0)
        {
            var lastPage = (uint)((totalItems - 1) / (int)limit);
            collection.Last = $"{outboxUrl}?page={lastPage}&limit={limit}";
        }

        return collection;
    }

    private static async Task<OrderedCollectionPage> BuildPageAsync(JellyfinDbContext dbContext, string outboxUrl, uint page, uint limit)
    {
        var totalItems = await dbContext.FederationOutboxActivities
            .CountAsync()
            .ConfigureAwait(false);

        var activities = await dbContext.FederationOutboxActivities
            .AsNoTracking()
            .OrderByDescending(a => a.DateCreated)
            .Skip((int)(page * limit))
            .Take((int)limit)
            .Select(a => a.ActivityJson)
            .ToListAsync()
            .ConfigureAwait(false);

        var items = activities
            .Select(json => System.Text.Json.JsonSerializer.Deserialize<ActivityStreams.Object>(json, ActivityStreamsJsonOptions.Default))
            .Where(obj => obj != null)
            .ToArray();

        var nextOffset = (page + 1) * limit;
        var hasNext = nextOffset < totalItems;

        return new OrderedCollectionPage
        {
            Id = $"{outboxUrl}?page={page}&limit={limit}",
            PartOf = outboxUrl,
            TotalItems = totalItems,
            OrderedItems = items!,
            Next = hasNext ? $"{outboxUrl}?page={page + 1}&limit={limit}" : null,
            Prev = page > 0 ? $"{outboxUrl}?page={page - 1}&limit={limit}" : null
        };
    }

    /// <inheritdoc/>
    public async Task HandleInboxActivityAsync(ActivityStreams.Object request, string signingActorUrl, CancellationToken cancellationToken = default)
    {
        if (request is not ActivityStreams.Activity activity)
        {
            return;
        }

        // Actor-on-behalf-of-self: every activity must declare the same actor that signed the
        // request. Rejects Alice-signed requests that claim Bob authored the activity.
        if (!string.Equals(activity.Actor, signingActorUrl, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Dropping {Type}: activity.actor={ActivityActor} does not match signing actor {SigningActor}",
                activity.Type,
                activity.Actor,
                signingActorUrl);
            return;
        }

        // Let handler exceptions propagate: the signer's delivery queue will retry with
        // exponential backoff, giving the operator a chance to fix the underlying issue while
        // activities queue on the peer side (not drop). We log here so the operator sees local
        // context without needing to cross-reference the peer's logs.
        try
        {
            switch (activity.Type)
            {
                case nameof(Follow):
                    await _peerService.HandleFollowRequestAsync(signingActorUrl, cancellationToken).ConfigureAwait(false);
                    break;

                case nameof(Accept):
                    await _peerService.HandleFollowingAcceptedAsync(signingActorUrl, cancellationToken).ConfigureAwait(false);
                    break;

                case nameof(Reject):
                    await _peerService.HandleFollowingRejectedAsync(signingActorUrl, cancellationToken).ConfigureAwait(false);
                    break;

                case nameof(Undo):
                    // Undo{Follow} means "I (who was following you) am unfollowing." The inner
                    // Follow's actor must be the signer — a mismatch is either a misconfigured
                    // peer or an active attempt to remove another follower's relationship; either
                    // way, log it loudly and drop.
                    if (activity.Object?.Type != nameof(Follow))
                    {
                        break;
                    }

                    if (!string.Equals(activity.Object.Actor, signingActorUrl, StringComparison.Ordinal))
                    {
                        _logger.LogWarning(
                            "Dropping Undo{{Follow}} from {Signer}: inner Follow.actor={InnerActor} does not match signer",
                            signingActorUrl,
                            activity.Object.Actor);
                        break;
                    }

                    await _peerService.RemoveFollowerAsync(signingActorUrl, cancellationToken).ConfigureAwait(false);
                    break;

                case nameof(Create):
                case nameof(Update):
                case nameof(Delete):
                    await _libraryIngester.IngestAsync(activity, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    _logger.LogDebug("Unhandled inbox activity type: {Type}", request.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to handle inbound {ActivityType} (id={ActivityId}) from {SigningActor}. "
                + "Returning 5xx so the sender's delivery queue retries with backoff. "
                + "Fix the underlying issue (see inner exception) before the retry window exhausts or the activity will be lost.",
                activity.Type,
                activity.Id,
                signingActorUrl);
            throw;
        }
    }
}
