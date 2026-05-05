using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Services;

/// <summary>
/// Serves HLS variant playlists and segments — builds the <c>main.m3u8</c> body and the
/// <c>hls1/*</c> segment files, and emits the ffmpeg command line used for HLS transcoding jobs.
/// Extracted from <see cref="Controllers.DynamicHlsController"/> so that other callers (e.g.
/// federation endpoints) can reuse the same HLS pipeline without duplicating the request-building
/// body.
/// </summary>
public interface IHlsStreamService
{
    /// <summary>
    /// Builds the variant (<c>main.m3u8</c>) playlist for a streaming request. The caller is
    /// responsible for authorizing the request.
    /// </summary>
    /// <param name="streamingRequest">The streaming request built from query parameters.</param>
    /// <param name="httpContext">The ambient HTTP context, used for query-string echoing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The action result to return to the caller.</returns>
    Task<ActionResult> GetVariantPlaylistAsync(
        StreamingRequestDto streamingRequest,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// Serves an individual HLS segment, starting or reusing the backing ffmpeg transcode job
    /// as needed. The caller is responsible for authorizing the request.
    /// </summary>
    /// <param name="streamingRequest">The streaming request built from query parameters.</param>
    /// <param name="segmentId">The zero-based segment index, or <c>-1</c> for the fmp4 init segment.</param>
    /// <param name="httpContext">The ambient HTTP context, used for response completion callbacks.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The action result to return to the caller.</returns>
    Task<ActionResult> GetSegmentAsync(
        StreamingRequestDto streamingRequest,
        int segmentId,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    /// <summary>
    /// Builds the ffmpeg command line used to spawn an HLS transcoding job. Still invoked by the
    /// <c>live.m3u8</c> endpoint on <see cref="Controllers.DynamicHlsController"/>, which manages
    /// its own transcoding job bootstrap.
    /// </summary>
    /// <param name="outputPath">The playlist output path ffmpeg writes to.</param>
    /// <param name="state">The stream state.</param>
    /// <param name="isEventPlaylist">Whether the playlist is <c>event</c> (live) or <c>vod</c>.</param>
    /// <param name="startNumber">The first number in the HLS sequence.</param>
    /// <returns>The command line arguments for the HLS transcoding job.</returns>
    string GetCommandLineArguments(string outputPath, StreamState state, bool isEventPlaylist, int startNumber);
}
