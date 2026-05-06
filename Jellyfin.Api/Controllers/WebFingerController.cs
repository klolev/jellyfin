using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Federation.Webfinger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// WebFinger discovery endpoint. Gated behind the federation feature flag: when federation is
/// disabled, the endpoint returns 404 via the <see cref="Policies.FederationEnabled"/> policy so
/// the surface is indistinguishable from a server that never shipped federation.
/// </summary>
[Route(".well-known")]
[ApiController]
[Authorize(Policy = Policies.FederationEnabled)]
public class WebFingerController : ControllerBase
{
    private readonly IWebfingerService _webfingerService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebFingerController"/> class.
    /// </summary>
    /// <param name="webfingerService">The webfinger service.</param>
    public WebFingerController(IWebfingerService webfingerService)
    {
        _webfingerService = webfingerService;
    }

    /// <summary>
    /// Handles WebFinger resource queries.
    /// </summary>
    /// <param name="resource">The resource to query (e.g. acct:jellyfin@my-instance.com).</param>
    /// <returns>The WebFinger JRD response.</returns>
    [HttpGet("webfinger")]
    [Produces("application/jrd+json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetWebFinger([FromQuery, Required] string resource)
    {
        var response = await _webfingerService.QueryAsync(resource).ConfigureAwait(false);
        if (response == null)
        {
            return NotFound();
        }

        return new JsonResult(response) { ContentType = "application/jrd+json" };
    }
}
