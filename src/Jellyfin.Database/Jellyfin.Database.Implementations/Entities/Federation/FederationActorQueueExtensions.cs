using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Database.Implementations.Entities.Federation;

/// <summary>
/// Helpers for enqueuing federation actor-queue items. All helpers assume the caller will call
/// <c>SaveChangesAsync</c> on the <see cref="JellyfinDbContext"/> once the enclosing unit of work
/// is complete. The "ensure queue row exists" step does its own save so the FK is usable for the
/// item insert that follows.
/// </summary>
public static class FederationActorQueueExtensions
{
    /// <summary>
    /// Gets or creates the single <see cref="FederationActorQueue"/> row for the given actor.
    /// </summary>
    /// <param name="dbContext">The DbContext.</param>
    /// <param name="actorId">The remote actor ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The queue row.</returns>
    public static async Task<FederationActorQueue> EnsureActorQueueAsync(this JellyfinDbContext dbContext, int actorId, CancellationToken cancellationToken = default)
    {
        var queue = await dbContext.FederationActorQueues
            .FirstOrDefaultAsync(q => q.ActorId == actorId, cancellationToken)
            .ConfigureAwait(false);
        if (queue is not null)
        {
            return queue;
        }

        queue = new FederationActorQueue(actorId);
        await dbContext.FederationActorQueues.AddAsync(queue, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return queue;
    }

    /// <summary>
    /// Enqueues a Deliver (signed POST with body) item for the given actor.
    /// </summary>
    /// <param name="dbContext">The DbContext.</param>
    /// <param name="actorId">The remote actor ID.</param>
    /// <param name="targetInboxUrl">The actor's inbox URL.</param>
    /// <param name="activityJson">The serialized activity JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public static async Task EnqueueDeliverAsync(this JellyfinDbContext dbContext, int actorId, string targetInboxUrl, string activityJson, CancellationToken cancellationToken = default)
    {
        var queue = await dbContext.EnsureActorQueueAsync(actorId, cancellationToken).ConfigureAwait(false);
        await dbContext.FederationActorQueueItems
            .AddAsync(new FederationActorQueueItem(queue.Id, FederationActorQueueItemKind.Deliver, targetInboxUrl, activityJson), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Enqueues a Fetch (signed GET) item for the given actor.
    /// </summary>
    /// <param name="dbContext">The DbContext.</param>
    /// <param name="actorId">The remote actor ID.</param>
    /// <param name="targetUrl">The URL to GET.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public static async Task EnqueueFetchAsync(this JellyfinDbContext dbContext, int actorId, string targetUrl, CancellationToken cancellationToken = default)
    {
        var queue = await dbContext.EnsureActorQueueAsync(actorId, cancellationToken).ConfigureAwait(false);
        await dbContext.FederationActorQueueItems
            .AddAsync(new FederationActorQueueItem(queue.Id, FederationActorQueueItemKind.Fetch, targetUrl, null), cancellationToken)
            .ConfigureAwait(false);
    }
}
