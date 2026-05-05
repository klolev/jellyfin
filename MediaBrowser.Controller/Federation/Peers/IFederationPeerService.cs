using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Federation.Peers;

namespace MediaBrowser.Controller.Federation.Peers;

/// <summary>
/// IFederationPeerService.
/// </summary>
public interface IFederationPeerService
{
    /// <summary>
    /// Handles storing a follow request from an actor.
    /// </summary>
    /// <param name="actorUrl">The actor URL.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Nothing.</returns>
    public Task HandleFollowRequestAsync(string actorUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles a remote actor accepting our follow request.
    /// </summary>
    /// <param name="actorUrl">The actor URL that accepted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Nothing.</returns>
    public Task HandleFollowingAcceptedAsync(string actorUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles a remote actor rejecting our follow request.
    /// </summary>
    /// <param name="actorUrl">The actor URL that rejected.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Nothing.</returns>
    public Task HandleFollowingRejectedAsync(string actorUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles vetting a follow request.
    /// </summary>
    /// <param name="actorUrl">The actor URL.</param>
    /// <param name="accept">Whether to accept the follower.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    public Task<VetFollowRequestResult> VetFollowerAsync(string actorUrl, bool accept, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets pending inbound follow requests.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list of pending inbound requests.</returns>
    public Task<IReadOnlyList<PendingFollowRequest>> GetFollowerRequestsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets pending outbound follow requests sent by this instance.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list of pending outbound requests.</returns>
    public Task<IReadOnlyList<PendingFollowRequest>> GetFollowingRequestsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a follow request to a remote actor.
    /// </summary>
    /// <param name="actorUrl">The actor URL to follow.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    public Task<FollowRequestResult> SendFollowRequestAsync(string actorUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of followers.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list of follower actors.</returns>
    public Task<IReadOnlyList<PeerActor>> GetFollowersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of following.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The list of followed actors.</returns>
    public Task<IReadOnlyList<PeerActor>> GetFollowingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a follower by actor URL.
    /// </summary>
    /// <param name="actorUrl">The actor URL to remove.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the follower was found and removed.</returns>
    public Task<bool> RemoveFollowerAsync(string actorUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a following by actor URL.
    /// </summary>
    /// <param name="actorUrl">The actor URL to unfollow.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the following was found and removed.</returns>
    public Task<bool> RemoveFollowingAsync(string actorUrl, CancellationToken cancellationToken = default);
}
