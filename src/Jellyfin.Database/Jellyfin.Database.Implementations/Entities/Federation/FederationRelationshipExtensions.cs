using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Database.Implementations.Entities.Federation;

/// <summary>
/// Relationship-lookup helpers on <see cref="JellyfinDbContext"/>. Shared by federation service,
/// library ingester, and peer service so the "does this actor follow us / do we follow this
/// actor" queries have a single implementation.
/// </summary>
public static class FederationRelationshipExtensions
{
    /// <summary>
    /// Returns whether <paramref name="actorUrl"/> is an approved follower (inbound relationship).
    /// </summary>
    /// <param name="dbContext">The DbContext.</param>
    /// <param name="actorUrl">The actor URL to check. <c>null</c>/empty returns <c>false</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the actor is an approved follower.</returns>
    public static async Task<bool> IsFollowerAsync(this JellyfinDbContext dbContext, string? actorUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(actorUrl))
        {
            return false;
        }

        return await dbContext.FederationFollowers
            .Include(f => f.Actor)
            .AnyAsync(f => f.Actor.Url == actorUrl, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the <see cref="FederationActor"/> for an actor this instance follows (outbound
    /// relationship), or <c>null</c> if the actor is not followed or the URL is null/empty.
    /// </summary>
    /// <param name="dbContext">The DbContext.</param>
    /// <param name="actorUrl">The actor URL to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The followed actor entity, or null.</returns>
    public static async Task<FederationActor?> GetFollowedActorAsync(this JellyfinDbContext dbContext, string? actorUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(actorUrl))
        {
            return null;
        }

        return await dbContext.FederationFollowings
            .Include(f => f.Actor)
            .Where(f => f.Actor.Url == actorUrl)
            .Select(f => f.Actor)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
