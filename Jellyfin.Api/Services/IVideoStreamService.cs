using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Streaming;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Services;

/// <summary>
/// Serves a progressive video stream — static passthrough, static file, or ffmpeg-transcoded output.
/// Extracted from <see cref="Controllers.VideosController"/> so that other callers (e.g. federation
/// endpoints) can reuse the same streaming pipeline without duplicating the request-building body.
/// </summary>
public interface IVideoStreamService
{
    /// <summary>
    /// Streams a video. The caller is responsible for authorizing the request.
    /// </summary>
    /// <param name="request">The streaming request built from query parameters.</param>
    /// <param name="httpContext">The ambient HTTP context, used for range processing and response writing.</param>
    /// <param name="isHeadRequest">Whether the caller's request is a HEAD (no body response).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The action result to return to the caller.</returns>
    Task<ActionResult> StreamAsync(
        VideoRequestDto request,
        HttpContext httpContext,
        bool isHeadRequest,
        CancellationToken cancellationToken);
}
