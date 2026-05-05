using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities.Federation;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Federation.Configuration;
using MediaBrowser.Controller.Federation.Peers;
using MediaBrowser.Controller.Federation.Signing;
using MediaBrowser.Model.Federation.ActivityStreams;
using MediaBrowser.Model.Federation.Peers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Implementations.Federation.Peers;

/// <summary>
/// FederationPeerService.
/// </summary>
public class FederationPeerService : IFederationPeerService
{
    private readonly IDbContextFactory<JellyfinDbContext> _dbProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFederationSigningService _signingService;
    private readonly IConfigurationManager _configManager;
    private readonly IFederationBackfillService _backfillService;
    private readonly ILogger<FederationPeerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationPeerService"/> class.
    /// </summary>
    /// <param name="dbProvider">The database provider.</param>
    /// <param name="httpClientFactory">The http client factory.</param>
    /// <param name="signingService">The signing service.</param>
    /// <param name="configManager">The configuration manager.</param>
    /// <param name="backfillService">The outbox backfill service.</param>
    /// <param name="logger">The logger.</param>
    public FederationPeerService(
        IDbContextFactory<JellyfinDbContext> dbProvider,
        IHttpClientFactory httpClientFactory,
        IFederationSigningService signingService,
        IConfigurationManager configManager,
        IFederationBackfillService backfillService,
        ILogger<FederationPeerService> logger)
    {
        _dbProvider = dbProvider;
        _httpClientFactory = httpClientFactory;
        _signingService = signingService;
        _configManager = configManager;
        _backfillService = backfillService;
        _logger = logger;
    }

    /// <summary>
    /// Handles storing a follow request from an actor.
    /// </summary>
    /// <param name="actorUrl">The actor URL.</param>
    /// <returns>Nothing.</returns>
    public async Task HandleFollowRequestAsync(string actorUrl)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            // Signature validation at inbox time guarantees the actor row exists by now.
            var actor = await dbContext.FederationActors
                .FirstOrDefaultAsync(a => a.Url == actorUrl)
                .ConfigureAwait(false);
            if (actor is null)
            {
                _logger.LogWarning("HandleFollowRequest: actor {ActorUrl} not in DB (signature layer should have stored it)", actorUrl);
                return;
            }

            var exists = await dbContext.FederationFollowRequests
                .AnyAsync(request => request.ActorId == actor.Id && request.Type == FederationFollowRequestType.Follower)
                .ConfigureAwait(false);
            if (exists)
            {
                return;
            }

            await dbContext.FederationFollowRequests.AddAsync(new FederationFollowRequest(actor.Id, FederationFollowRequestType.Follower)).ConfigureAwait(false);
            await dbContext.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sends a follow request to a remote actor.
    /// </summary>
    /// <param name="actorUrl">The actor URL to follow.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<FollowRequestResult> SendFollowRequestAsync(string actorUrl)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            // Check if already following or pending
            var alreadyFollowing = await dbContext.FederationFollowings
                .Include(f => f.Actor)
                .AnyAsync(f => f.Actor.Url == actorUrl)
                .ConfigureAwait(false);
            if (alreadyFollowing)
            {
                return FollowRequestResult.AlreadyFollowing;
            }

            var pendingRequest = await dbContext.FederationFollowRequests
                .AnyAsync(r => r.Actor.Url == actorUrl && r.Type == FederationFollowRequestType.Following && !r.Responded)
                .ConfigureAwait(false);
            if (pendingRequest)
            {
                return FollowRequestResult.AlreadyFollowing;
            }

            // Fetch actor to get inbox URL. Signed GET for Mastodon-style servers that
            // require HTTP signatures on all requests.
            ActorResponse actor;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, actorUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/activity+json"));
                await _signingService.SignAsync(request).ConfigureAwait(false);

                var client = _httpClientFactory.CreateClient();
                using var response = await client.SendAsync(request).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var fetchedActor = await response.Content.ReadFromJsonAsync<ActorResponse>().ConfigureAwait(false);
                if (fetchedActor == null || string.IsNullOrEmpty(fetchedActor.Inbox) || string.IsNullOrEmpty(fetchedActor.Outbox) || string.IsNullOrEmpty(fetchedActor.PublicKey?.PublicKeyPem))
                {
                    return FollowRequestResult.ActorFetchFailed;
                }

                if (!string.Equals(fetchedActor.Id, actorUrl, StringComparison.Ordinal))
                {
                    _logger.LogWarning("Actor fetch for {ActorUrl} returned mismatched id {ActorId}; rejecting", actorUrl, fetchedActor.Id);
                    return FollowRequestResult.ActorFetchFailed;
                }

                actor = fetchedActor;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to fetch actor at {ActorUrl}", actorUrl);
                return FollowRequestResult.ActorFetchFailed;
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize actor response from {ActorUrl}", actorUrl);
                return FollowRequestResult.ActorFetchFailed;
            }

            // Create or get the actor entry
            var federationActor = await dbContext.FederationActors
                .FirstOrDefaultAsync(a => a.Url == actorUrl)
                .ConfigureAwait(false);
            if (federationActor == null)
            {
                federationActor = new FederationActor(actorUrl, actor.Inbox, actor.Outbox, actor.PublicKey.PublicKeyPem);
                await dbContext.FederationActors.AddAsync(federationActor).ConfigureAwait(false);
                await dbContext.SaveChangesAsync().ConfigureAwait(false);
            }

            // Create follow request record
            await dbContext.FederationFollowRequests
                .AddAsync(new FederationFollowRequest(federationActor.Id, FederationFollowRequestType.Following))
                .ConfigureAwait(false);

            // Queue the Follow activity for delivery
            var config = _configManager.GetFederationConfiguration();
            var ourActorUrl = config.ActorURL;
            var followActivity = new Follow
            {
                Id = $"{ourActorUrl}#follow-{actorUrl}",
                Actor = ourActorUrl,
                Object = new MediaBrowser.Model.Federation.ActivityStreams.Object { Id = actorUrl }
            };

            var activityJson = System.Text.Json.JsonSerializer.Serialize(followActivity, ActivityStreamsJsonOptions.Default);
            await dbContext.EnqueueDeliverAsync(federationActor.Id, federationActor.InboxUrl, activityJson).ConfigureAwait(false);

            await dbContext.SaveChangesAsync().ConfigureAwait(false);
            return FollowRequestResult.Success;
        }
    }

    /// <summary>
    /// Handles vetting a follow request.
    /// </summary>
    /// <param name="actorUrl">The actor URL.</param>
    /// <param name="accept">Whether to accept the follower.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<VetFollowRequestResult> VetFollowerAsync(string actorUrl, bool accept)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var request = await dbContext.FederationFollowRequests
                .Include(r => r.Actor)
                .FirstOrDefaultAsync(r => r.Actor.Url == actorUrl && r.Type == FederationFollowRequestType.Follower && !r.Responded)
                .ConfigureAwait(false);
            if (request is null)
            {
                return VetFollowRequestResult.NoPendingRequest;
            }

            request.Responded = true;

            if (!accept)
            {
                // Best-effort: notify the remote via Reject{Follow} through the delivery queue.
                await QueueRejectFollowAsync(dbContext, request.Actor).ConfigureAwait(false);
                await dbContext.SaveChangesAsync().ConfigureAwait(false);
                return VetFollowRequestResult.Success;
            }

            // Actor is already in the DB (signature layer fetched it at inbox time) — no need to
            // re-fetch. Queue Accept{Follow} using the loaded actor's InboxUrl.
            var config = _configManager.GetFederationConfiguration();
            var ourActorUrl = config.ActorURL;
            var acceptActivity = new Accept
            {
                Id = $"{ourActorUrl}#accept-follow-{actorUrl}",
                Actor = ourActorUrl,
                Object = new Follow
                {
                    Actor = actorUrl,
                    Object = new MediaBrowser.Model.Federation.ActivityStreams.Object { Id = ourActorUrl }
                }
            };

            var activityJson = System.Text.Json.JsonSerializer.Serialize(acceptActivity, ActivityStreamsJsonOptions.Default);
            await dbContext.EnqueueDeliverAsync(request.Actor.Id, request.Actor.InboxUrl, activityJson).ConfigureAwait(false);

            await dbContext.FederationFollowers
                .AddAsync(new FederationFollower(request.Actor.Id))
                .ConfigureAwait(false);
            await dbContext.SaveChangesAsync().ConfigureAwait(false);
            return VetFollowRequestResult.Success;
        }
    }

    private async Task QueueRejectFollowAsync(JellyfinDbContext dbContext, FederationActor actor)
    {
        var config = _configManager.GetFederationConfiguration();
        var ourActorUrl = config.ActorURL;
        var rejectActivity = new Reject
        {
            Id = $"{ourActorUrl}#reject-follow-{actor.Url}",
            Actor = ourActorUrl,
            Object = new Follow
            {
                Actor = actor.Url,
                Object = new MediaBrowser.Model.Federation.ActivityStreams.Object { Id = ourActorUrl }
            }
        };

        var activityJson = System.Text.Json.JsonSerializer.Serialize(rejectActivity, ActivityStreamsJsonOptions.Default);
        await dbContext.EnqueueDeliverAsync(actor.Id, actor.InboxUrl, activityJson).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles a remote actor accepting our follow request.
    /// </summary>
    /// <param name="actorUrl">The actor URL that accepted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Nothing.</returns>
    public async Task HandleFollowingAcceptedAsync(string actorUrl, CancellationToken cancellationToken = default)
    {
        FederationActor acceptedActor;
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var request = await dbContext.FederationFollowRequests
                .FirstOrDefaultAsync(r => r.Actor.Url == actorUrl && r.Type == FederationFollowRequestType.Following && !r.Responded, cancellationToken)
                .ConfigureAwait(false);
            if (request == null)
            {
                return;
            }

            request.Responded = true;

            // Actor should already exist from when we sent the follow request
            var federationActor = await dbContext.FederationActors
                .FirstOrDefaultAsync(a => a.Url == actorUrl, cancellationToken)
                .ConfigureAwait(false);
            if (federationActor == null)
            {
                _logger.LogError("Actor not found in DB for accepted follow: {ActorUrl}", actorUrl);
                return;
            }

            var following = new FederationFollowing(federationActor.Id);
            await dbContext.FederationFollowings.AddAsync(following, cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            acceptedActor = federationActor;
        }

        // Enqueue the initial backfill Fetch — the queue worker drives the pagination chain.
        await _backfillService.BackfillFromAsync(acceptedActor, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles a remote actor rejecting our follow request.
    /// </summary>
    /// <param name="actorUrl">The actor URL that rejected.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Nothing.</returns>
    public async Task HandleFollowingRejectedAsync(string actorUrl, CancellationToken cancellationToken = default)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var request = await dbContext.FederationFollowRequests
                .FirstOrDefaultAsync(r => r.Actor.Url == actorUrl && r.Type == FederationFollowRequestType.Following && !r.Responded, cancellationToken)
                .ConfigureAwait(false);
            if (request == null)
            {
                return;
            }

            request.Responded = true;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gets the list of followers.
    /// </summary>
    /// <returns>The list of followers.</returns>
    public async Task<IReadOnlyList<PeerActor>> GetFollowersAsync()
    {
        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            return await dbContext.FederationFollowers
                .AsNoTracking()
                .Include(f => f.Actor)
                .Select(f => new PeerActor(f.Actor.Url, f.Actor.InboxUrl, f.Actor.OutboxUrl, f.Actor.PublicKey))
                .ToListAsync()
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gets the list of actors this instance follows.
    /// </summary>
    /// <returns>The list of following.</returns>
    public async Task<IReadOnlyList<PeerActor>> GetFollowingAsync()
    {
        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            return await dbContext.FederationFollowings
                .AsNoTracking()
                .Include(f => f.Actor)
                .Select(f => new PeerActor(f.Actor.Url, f.Actor.InboxUrl, f.Actor.OutboxUrl, f.Actor.PublicKey))
                .ToListAsync()
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PendingFollowRequest>> GetFollowerRequestsAsync()
        => await GetPendingRequestsAsync(FederationFollowRequestType.Follower).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PendingFollowRequest>> GetFollowingRequestsAsync()
        => await GetPendingRequestsAsync(FederationFollowRequestType.Following).ConfigureAwait(false);

    private async Task<IReadOnlyList<PendingFollowRequest>> GetPendingRequestsAsync(FederationFollowRequestType type)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            return await dbContext.FederationFollowRequests
                .AsNoTracking()
                .Include(r => r.Actor)
                .Where(r => r.Type == type && !r.Responded)
                .OrderByDescending(r => r.DateCreated)
                .Select(r => new PendingFollowRequest(r.Actor.Url, r.DateCreated))
                .ToListAsync()
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Removes a follower by actor URL.
    /// </summary>
    /// <param name="actorUrl">The actor URL to remove.</param>
    /// <returns>True if the follower was found and removed.</returns>
    public async Task<bool> RemoveFollowerAsync(string actorUrl)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var follower = await dbContext.FederationFollowers
                .Include(f => f.Actor)
                .FirstOrDefaultAsync(f => f.Actor.Url == actorUrl)
                .ConfigureAwait(false);
            if (follower == null)
            {
                return false;
            }

            dbContext.FederationFollowers.Remove(follower);
            await dbContext.SaveChangesAsync().ConfigureAwait(false);
            return true;
        }
    }

    /// <summary>
    /// Removes a following by actor URL and queues an Undo{Follow} for delivery.
    /// </summary>
    /// <param name="actorUrl">The actor URL to unfollow.</param>
    /// <returns>True if the following was found and removed.</returns>
    public async Task<bool> RemoveFollowingAsync(string actorUrl)
    {
        var dbContext = await _dbProvider.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var following = await dbContext.FederationFollowings
                .Include(f => f.Actor)
                .FirstOrDefaultAsync(f => f.Actor.Url == actorUrl)
                .ConfigureAwait(false);
            if (following == null)
            {
                return false;
            }

            // Queue Undo{Follow} for delivery
            var config = _configManager.GetFederationConfiguration();
            var ourActorUrl = config.ActorURL;
            var undoActivity = new Undo
            {
                Id = $"{ourActorUrl}#undo-follow-{actorUrl}",
                Actor = ourActorUrl,
                Object = new Follow
                {
                    Actor = ourActorUrl,
                    Object = new MediaBrowser.Model.Federation.ActivityStreams.Object { Id = actorUrl }
                }
            };

            var activityJson = System.Text.Json.JsonSerializer.Serialize(undoActivity, ActivityStreamsJsonOptions.Default);
            await dbContext.EnqueueDeliverAsync(following.ActorId, following.Actor.InboxUrl, activityJson).ConfigureAwait(false);

            dbContext.FederationFollowings.Remove(following);
            await dbContext.SaveChangesAsync().ConfigureAwait(false);
            return true;
        }
    }
}
