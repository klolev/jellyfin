using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Helpers;
using Jellyfin.Api.Models.FederationDtos;
using Jellyfin.Api.Services;
using Jellyfin.Database.Implementations;
using MediaBrowser.Common.Api;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Federation.Configuration;
using MediaBrowser.Controller.Federation.Media;
using MediaBrowser.Controller.Federation.Signing;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// Federation media mirror endpoints. Exposes a token-issue endpoint that approved followers call
/// (signed), and a suite of token-gated GET endpoints that delegate to the stage-1 streaming
/// services so federation callers can play local items without a Jellyfin session.
/// </summary>
[Route("Federation/Media")]
[Authorize(Policy = Policies.FederationEnabled)]
public class FederationMediaController : BaseJellyfinApiController
{
    private static readonly TimeSpan DefaultTokenTtl = TimeSpan.FromHours(6);

    private readonly IFederationSigningService _signingService;
    private readonly IFederationStreamTokenService _tokenService;
    private readonly IFederationServiceUserProvider _serviceUserProvider;
    private readonly IVideoStreamService _videoStreamService;
    private readonly IHlsStreamService _hlsStreamService;
    private readonly ISubtitleStreamService _subtitleStreamService;
    private readonly DynamicHlsHelper _dynamicHlsHelper;
    private readonly ILibraryManager _libraryManager;
    private readonly IConfigurationManager _configManager;
    private readonly IDbContextFactory<JellyfinDbContext> _dbFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="FederationMediaController"/> class.
    /// </summary>
    /// <param name="signingService">Instance of the <see cref="IFederationSigningService"/> interface.</param>
    /// <param name="tokenService">Instance of the <see cref="IFederationStreamTokenService"/> interface.</param>
    /// <param name="serviceUserProvider">Instance of the <see cref="IFederationServiceUserProvider"/> interface.</param>
    /// <param name="videoStreamService">Instance of the <see cref="IVideoStreamService"/> interface.</param>
    /// <param name="hlsStreamService">Instance of the <see cref="IHlsStreamService"/> interface.</param>
    /// <param name="subtitleStreamService">Instance of the <see cref="ISubtitleStreamService"/> interface.</param>
    /// <param name="dynamicHlsHelper">Instance of <see cref="DynamicHlsHelper"/>.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="configManager">Instance of the <see cref="IConfigurationManager"/> interface.</param>
    /// <param name="dbFactory">Instance of the <see cref="IDbContextFactory{JellyfinDbContext}"/> interface.</param>
    public FederationMediaController(
        IFederationSigningService signingService,
        IFederationStreamTokenService tokenService,
        IFederationServiceUserProvider serviceUserProvider,
        IVideoStreamService videoStreamService,
        IHlsStreamService hlsStreamService,
        ISubtitleStreamService subtitleStreamService,
        DynamicHlsHelper dynamicHlsHelper,
        ILibraryManager libraryManager,
        IConfigurationManager configManager,
        IDbContextFactory<JellyfinDbContext> dbFactory)
    {
        _signingService = signingService;
        _tokenService = tokenService;
        _serviceUserProvider = serviceUserProvider;
        _videoStreamService = videoStreamService;
        _hlsStreamService = hlsStreamService;
        _subtitleStreamService = subtitleStreamService;
        _dynamicHlsHelper = dynamicHlsHelper;
        _libraryManager = libraryManager;
        _configManager = configManager;
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Issues a streaming token for <paramref name="itemId"/>. The request must carry a valid HTTP
    /// signature from an approved follower.
    /// </summary>
    /// <param name="itemId">The library item to authorize.</param>
    /// <returns>The token, expiry, and a convenience stream URL.</returns>
    [HttpPost("{itemId}/Token")]
    [Consumes("application/activity+json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FederationStreamTokenResponse>> IssueToken([FromRoute, Required] Guid itemId)
    {
        var signingActor = await _signingService.ValidateAsync(Request).ConfigureAwait(false);
        if (signingActor is null)
        {
            return Unauthorized();
        }

        var dbContext = await _dbFactory.CreateDbContextAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var follower = await dbContext.FederationFollowers
                .Include(f => f.Actor)
                .FirstOrDefaultAsync(f => f.Actor.Url == signingActor, HttpContext.RequestAborted)
                .ConfigureAwait(false);
            if (follower is null)
            {
                return Unauthorized();
            }

            if (_libraryManager.GetItemById(itemId) is null)
            {
                return NotFound();
            }

            var issued = await _tokenService.IssueAsync(follower.Id, itemId, DefaultTokenTtl, HttpContext.RequestAborted).ConfigureAwait(false);

            var config = _configManager.GetFederationConfiguration();
            var streamUrl = $"{config.BaseURL}/Federation/Media/{itemId:N}/Stream?token={issued.Token}";

            return Ok(new FederationStreamTokenResponse
            {
                Token = issued.Token,
                ExpiresAt = issued.ExpiresAt,
                StreamUrl = streamUrl
            });
        }
    }

    /// <summary>
    /// Progressive stream mirror — delegates to <see cref="IVideoStreamService"/>.
    /// </summary>
    /// <param name="itemId">The library item to stream.</param>
    /// <param name="token">The capability token issued by <c>POST /Federation/Media/{itemId}/Token</c>.</param>
    /// <param name="static">Whether to stream the source file directly without transcoding. Defaults to <c>true</c>.</param>
    /// <param name="container">Optional output container override (transcoding path only).</param>
    /// <returns>The streamed media.</returns>
    [HttpGet("{itemId}/Stream")]
    [HttpHead("{itemId}/Stream", Name = "HeadFederationStream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> GetStream(
        [FromRoute, Required] Guid itemId,
        [FromQuery, Required] string token,
        [FromQuery] bool? @static,
        [FromQuery] string? container)
    {
        if (await ValidateTokenAsync(token, itemId).ConfigureAwait(false) is null)
        {
            return Unauthorized();
        }

        await ApplyFederationUserAsync().ConfigureAwait(false);

        var streamingRequest = new VideoRequestDto
        {
            Id = itemId,
            Static = @static ?? true,
            Container = container
        };

        var isHeadRequest = string.Equals(Request.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
        return await _videoStreamService.StreamAsync(streamingRequest, HttpContext, isHeadRequest, HttpContext.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// HLS master playlist mirror — delegates to <see cref="DynamicHlsHelper"/>.
    /// </summary>
    /// <param name="itemId">The library item.</param>
    /// <param name="token">The capability token.</param>
    /// <param name="segmentContainer">The HLS segment container (e.g. <c>ts</c>, <c>mp4</c>).</param>
    /// <param name="segmentLength">Optional segment length override.</param>
    /// <returns>The master playlist.</returns>
    [HttpGet("{itemId}/master.m3u8")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> GetMasterPlaylist(
        [FromRoute, Required] Guid itemId,
        [FromQuery, Required] string token,
        [FromQuery] string? segmentContainer,
        [FromQuery] int? segmentLength)
    {
        if (await ValidateTokenAsync(token, itemId).ConfigureAwait(false) is null)
        {
            return Unauthorized();
        }

        await ApplyFederationUserAsync().ConfigureAwait(false);

        var streamingRequest = new VideoRequestDto
        {
            Id = itemId,
            SegmentContainer = segmentContainer,
            SegmentLength = segmentLength
        };

        return await _dynamicHlsHelper.GetMasterHlsPlaylist(TranscodingJobType.Hls, streamingRequest, enableAdaptiveBitrateStreaming: false).ConfigureAwait(false);
    }

    /// <summary>
    /// HLS variant playlist mirror — delegates to <see cref="IHlsStreamService"/>.
    /// </summary>
    /// <param name="itemId">The library item.</param>
    /// <param name="token">The capability token.</param>
    /// <param name="segmentContainer">The HLS segment container.</param>
    /// <param name="segmentLength">Optional segment length override.</param>
    /// <param name="playSessionId">The play session id.</param>
    /// <returns>The variant playlist.</returns>
    [HttpGet("{itemId}/main.m3u8")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> GetVariantPlaylist(
        [FromRoute, Required] Guid itemId,
        [FromQuery, Required] string token,
        [FromQuery] string? segmentContainer,
        [FromQuery] int? segmentLength,
        [FromQuery] string? playSessionId)
    {
        if (await ValidateTokenAsync(token, itemId).ConfigureAwait(false) is null)
        {
            return Unauthorized();
        }

        await ApplyFederationUserAsync().ConfigureAwait(false);

        var streamingRequest = new VideoRequestDto
        {
            Id = itemId,
            SegmentContainer = segmentContainer,
            SegmentLength = segmentLength,
            PlaySessionId = playSessionId
        };

        return await _hlsStreamService.GetVariantPlaylistAsync(streamingRequest, HttpContext, HttpContext.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// HLS segment mirror — delegates to <see cref="IHlsStreamService"/>.
    /// </summary>
    /// <param name="itemId">The library item.</param>
    /// <param name="playlistId">The playlist id (currently unused downstream; preserved for URL parity).</param>
    /// <param name="segmentId">The zero-based segment index.</param>
    /// <param name="container">The segment container extension.</param>
    /// <param name="token">The capability token.</param>
    /// <param name="runtimeTicks">The full runtime in ticks (required by the HLS pipeline).</param>
    /// <param name="actualSegmentLengthTicks">The actual segment length in ticks.</param>
    /// <param name="playSessionId">The play session id.</param>
    /// <returns>The segment bytes.</returns>
    [HttpGet("{itemId}/hls1/{playlistId}/{segmentId}.{container}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> GetSegment(
        [FromRoute, Required] Guid itemId,
        [FromRoute, Required] string playlistId,
        [FromRoute, Required] int segmentId,
        [FromRoute, Required] string container,
        [FromQuery, Required] string token,
        [FromQuery, Required] long runtimeTicks,
        [FromQuery, Required] long actualSegmentLengthTicks,
        [FromQuery] string? playSessionId)
    {
        _ = playlistId;

        if (await ValidateTokenAsync(token, itemId).ConfigureAwait(false) is null)
        {
            return Unauthorized();
        }

        await ApplyFederationUserAsync().ConfigureAwait(false);

        var streamingRequest = new VideoRequestDto
        {
            Id = itemId,
            Container = container,
            SegmentContainer = container,
            PlaySessionId = playSessionId,
            CurrentRuntimeTicks = runtimeTicks,
            ActualSegmentLengthTicks = actualSegmentLengthTicks
        };

        return await _hlsStreamService.GetSegmentAsync(streamingRequest, segmentId, HttpContext, HttpContext.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>
    /// Subtitle stream mirror — delegates to <see cref="ISubtitleStreamService"/>.
    /// </summary>
    /// <param name="itemId">The library item.</param>
    /// <param name="index">The subtitle stream index.</param>
    /// <param name="format">The subtitle format (e.g. <c>vtt</c>, <c>srt</c>).</param>
    /// <param name="token">The capability token.</param>
    /// <param name="mediaSourceId">The media source id.</param>
    /// <param name="startPositionTicks">The start position of the subtitle in ticks.</param>
    /// <param name="endPositionTicks">Optional end position in ticks.</param>
    /// <param name="copyTimestamps">Whether to copy timestamps.</param>
    /// <param name="addVttTimeMap">Whether to add a VTT time map.</param>
    /// <returns>The subtitle content.</returns>
    [HttpGet("{itemId}/Subtitles/{index}/Stream.{format}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> GetSubtitle(
        [FromRoute, Required] Guid itemId,
        [FromRoute, Required] int index,
        [FromRoute, Required] string format,
        [FromQuery, Required] string token,
        [FromQuery] string? mediaSourceId,
        [FromQuery] long startPositionTicks = 0,
        [FromQuery] long? endPositionTicks = null,
        [FromQuery] bool copyTimestamps = false,
        [FromQuery] bool addVttTimeMap = false)
    {
        if (await ValidateTokenAsync(token, itemId).ConfigureAwait(false) is null)
        {
            return Unauthorized();
        }

        return await _subtitleStreamService.GetSubtitleAsync(
            itemId,
            mediaSourceId,
            index,
            format,
            startPositionTicks,
            endPositionTicks,
            copyTimestamps,
            addVttTimeMap,
            HttpContext.RequestAborted).ConfigureAwait(false);
    }

    private Task<int?> ValidateTokenAsync(string token, Guid itemId)
        => _tokenService.ValidateAsync(token, itemId, HttpContext.RequestAborted);

    private async Task ApplyFederationUserAsync()
    {
        var userId = await _serviceUserProvider.GetOrCreateAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        var identity = new ClaimsIdentity(
            new[] { new Claim(InternalClaimTypes.UserId, userId.ToString("N", CultureInfo.InvariantCulture)) },
            authenticationType: "FederationToken");
        HttpContext.User = new ClaimsPrincipal(identity);
    }
}
