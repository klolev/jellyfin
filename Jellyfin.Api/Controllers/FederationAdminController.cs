using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Jellyfin.Api.Models.FederationDtos;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Federation.Peers;
using MediaBrowser.Model.Federation.Peers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// Admin endpoints for managing federation peers. All routes require elevated privileges.
/// </summary>
[Route("Federation/Admin")]
[Authorize(Policy = Policies.RequiresElevation)]
[Authorize(Policy = Policies.FederationEnabled)]
public class FederationAdminController : BaseJellyfinApiController
{
    private readonly IFederationPeerService _peerService;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationAdminController"/> class.
    /// </summary>
    /// <param name="peerService">The peer service.</param>
    public FederationAdminController(IFederationPeerService peerService)
    {
        _peerService = peerService;
    }

    /// <summary>
    /// Gets the list of accepted followers.
    /// </summary>
    /// <returns>The list of follower actors.</returns>
    [HttpGet("Followers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PeerActor>>> GetFollowers()
    {
        var followers = await _peerService.GetFollowersAsync().ConfigureAwait(false);
        return Ok(followers);
    }

    /// <summary>
    /// Removes a follower.
    /// </summary>
    /// <param name="actorUrl">The actor URL to remove.</param>
    /// <returns>204 on success, 404 if the actor was not a follower.</returns>
    [HttpDelete("Followers")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemoveFollower([FromQuery, Required] string actorUrl)
    {
        var removed = await _peerService.RemoveFollowerAsync(actorUrl).ConfigureAwait(false);
        return removed ? NoContent() : NotFound();
    }

    /// <summary>
    /// Gets the list of pending inbound follow requests.
    /// </summary>
    /// <returns>The list of pending requests.</returns>
    [HttpGet("Followers/Requests")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PendingFollowRequest>>> GetFollowerRequests()
    {
        var requests = await _peerService.GetFollowerRequestsAsync().ConfigureAwait(false);
        return Ok(requests);
    }

    /// <summary>
    /// Accepts or rejects a pending inbound follow request.
    /// </summary>
    /// <param name="actorUrl">The actor URL whose request is being resolved.</param>
    /// <param name="body">Whether to accept the request.</param>
    /// <returns>204 on success; 404 if no pending request exists.</returns>
    [HttpPatch("Followers/Requests")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> VetFollowerRequest(
        [FromQuery, Required] string actorUrl,
        [FromBody] VetFollowerRequest body)
    {
        var result = await _peerService.VetFollowerAsync(actorUrl, body.Accept).ConfigureAwait(false);
        return result switch
        {
            VetFollowRequestResult.Success => NoContent(),
            VetFollowRequestResult.NoPendingRequest => NotFound(),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    /// <summary>
    /// Gets the list of actors this instance currently follows.
    /// </summary>
    /// <returns>The list of followed actors.</returns>
    [HttpGet("Following")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PeerActor>>> GetFollowing()
    {
        var following = await _peerService.GetFollowingAsync().ConfigureAwait(false);
        return Ok(following);
    }

    /// <summary>
    /// Unfollows a remote actor.
    /// </summary>
    /// <param name="actorUrl">The actor URL to unfollow.</param>
    /// <returns>204 on success, 404 if not currently followed.</returns>
    [HttpDelete("Following")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemoveFollowing([FromQuery, Required] string actorUrl)
    {
        var removed = await _peerService.RemoveFollowingAsync(actorUrl).ConfigureAwait(false);
        return removed ? NoContent() : NotFound();
    }

    /// <summary>
    /// Gets the list of pending outbound follow requests.
    /// </summary>
    /// <returns>The list of pending requests.</returns>
    [HttpGet("Following/Requests")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PendingFollowRequest>>> GetFollowingRequests()
    {
        var requests = await _peerService.GetFollowingRequestsAsync().ConfigureAwait(false);
        return Ok(requests);
    }

    /// <summary>
    /// Sends an outbound follow request to a remote actor.
    /// </summary>
    /// <param name="actorUrl">The actor URL to follow.</param>
    /// <returns>204 on success; 409 if already following or pending; 502 if the remote actor could not be fetched.</returns>
    [HttpPost("Following/Requests")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> SendFollowRequest([FromQuery, Required] string actorUrl)
    {
        var result = await _peerService.SendFollowRequestAsync(actorUrl).ConfigureAwait(false);
        return result switch
        {
            FollowRequestResult.Success => NoContent(),
            FollowRequestResult.AlreadyFollowing => Conflict(),
            FollowRequestResult.ActorFetchFailed => StatusCode(StatusCodes.Status502BadGateway),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
