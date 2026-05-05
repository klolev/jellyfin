using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Common.Api;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Federation;
using MediaBrowser.Controller.Federation.Configuration;
using MediaBrowser.Controller.Federation.Signing;
using MediaBrowser.Model.Federation.ActivityStreams;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// Public federation ActivityPub endpoints.
/// </summary>
[Route("Federation")]
[Authorize(Policy = Policies.FederationEnabled)]
public class FederationController : BaseJellyfinApiController
{
    private readonly IFederationService _federationService;
    private readonly IFederationSigningService _signingService;
    private readonly IConfigurationManager _configManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationController"/> class.
    /// </summary>
    /// <param name="federationService">The federation service.</param>
    /// <param name="signingService">The signing service.</param>
    /// <param name="configManager">The configuration manager.</param>
    public FederationController(
        IFederationService federationService,
        IFederationSigningService signingService,
        IConfigurationManager configManager)
    {
        _federationService = federationService;
        _signingService = signingService;
        _configManager = configManager;
    }

    /// <summary>
    /// Gets the instance's ActivityPub actor document.
    /// </summary>
    /// <returns>The actor JSON-LD document.</returns>
    [HttpGet("Actor")]
    [Produces("application/activity+json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetActor()
    {
        // Feature gate on the controller guarantees Enabled && !IsNullOrEmpty(Hostname) here.
        var config = _configManager.GetFederationConfiguration();
        var publicKeyPem = await _signingService.EnsureKeypairAsync().ConfigureAwait(false);

        var actor = new Service
        {
            Id = config.ActorURL,
            Name = config.ActorName,
            PreferredUsername = config.ActorName,
            Inbox = config.InboxURL,
            Outbox = config.OutboxURL,
            PublicKey = new PublicKey
            {
                Id = $"{config.ActorURL}#main-key",
                Owner = config.ActorURL,
                PublicKeyPem = publicKeyPem
            }
        };

        return new JsonResult(actor) { ContentType = "application/activity+json" };
    }

    /// <summary>
    /// Receives ActivityPub inbox activities. The body is deserialized manually (rather than via
    /// <c>[FromBody]</c>) so that signature validation can read the raw request body first —
    /// model binding would consume the stream before we get a chance to verify the content digest.
    /// </summary>
    /// <returns>202 Accepted.</returns>
    [HttpPost("Inbox")]
    [Consumes("application/activity+json")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult> PostInbox()
    {
        Request.EnableBuffering();

        var signingActor = await _signingService.ValidateAsync(Request).ConfigureAwait(false);
        if (signingActor is null)
        {
            return Unauthorized();
        }

        Request.Body.Position = 0;
        var activity = await JsonSerializer.DeserializeAsync<Activity>(Request.Body, ActivityStreamsJsonOptions.Default).ConfigureAwait(false);
        if (activity is null)
        {
            return BadRequest();
        }

        await _federationService.HandleInboxActivityAsync(activity, signingActor).ConfigureAwait(false);
        return Accepted();
    }

    /// <summary>
    /// Gets the outbox ordered collection.
    /// Without a page param returns the top-level OrderedCollection with first/last references.
    /// With a page param returns an OrderedCollectionPage with items and next/prev navigation.
    /// </summary>
    /// <param name="page">The page number. When null, the summary is returned instead of a page.</param>
    /// <param name="limit">The page size.</param>
    /// <returns>The outbox collection or page.</returns>
    [HttpGet("Outbox")]
    [Produces("application/activity+json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetOutbox([FromQuery] uint? page = null, [FromQuery] uint limit = 20)
    {
        // Outbox is always a well-formed (possibly empty) OrderedCollection per AP §5.1. The
        // service itself filters items by the reader's permissions — anonymous / non-follower
        // readers get an empty collection, approved followers get the real data.
        var signingActor = await _signingService.ValidateAsync(Request).ConfigureAwait(false);
        var result = await _federationService.GetOutboxAsync(page, limit, signingActor).ConfigureAwait(false);
        return new JsonResult(result) { ContentType = "application/activity+json" };
    }
}
