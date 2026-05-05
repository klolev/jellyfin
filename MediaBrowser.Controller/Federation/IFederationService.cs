using System.Threading.Tasks;
using MediaBrowser.Model.Federation.ActivityStreams;

namespace MediaBrowser.Controller.Federation;

/// <summary>
/// IFederationService.
/// </summary>
public interface IFederationService
{
    /// <summary>
    /// Gets the outbox as either a top-level <see cref="OrderedCollection"/> (when
    /// <paramref name="page"/> is <c>null</c>) or an <see cref="OrderedCollectionPage"/> (when a
    /// page index is supplied). Filtered by reader permissions per ActivityPub §5.1: non-follower
    /// callers receive a well-formed but empty response.
    /// </summary>
    /// <param name="page">The page number (0-indexed), or <c>null</c> for the top-level summary.</param>
    /// <param name="limit">The limit of results per page.</param>
    /// <param name="signingActorUrl">The actor URL from the request's signature, or <c>null</c> for anonymous.</param>
    /// <returns>The outbox collection or collection page as an <see cref="Object"/>.</returns>
    public Task<Object> GetOutboxAsync(uint? page, uint limit, string? signingActorUrl);

    /// <summary>
    /// Handles inbox POST requests. Enforces the ActivityPub actor-on-behalf-of-self invariant:
    /// the <paramref name="signingActorUrl"/> (from the verified HTTP signature) must match the
    /// activity's declared <c>actor</c>. Mismatched activities are silently dropped so a peer
    /// can't publish on another peer's behalf.
    /// </summary>
    /// <param name="request">The request body.</param>
    /// <param name="signingActorUrl">The actor URL from the request's verified signature.</param>
    /// <returns>Nothing.</returns>
    public Task HandleInboxActivityAsync(Object request, string signingActorUrl);
}
