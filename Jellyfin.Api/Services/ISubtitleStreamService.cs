using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Services;

/// <summary>
/// Serves a single subtitle stream — static passthrough for unencoded formats, or ffmpeg-transcoded
/// output for encoded formats. Extracted from <see cref="Controllers.SubtitleController"/> so that
/// other callers (e.g. federation endpoints) can reuse the same pipeline without duplicating the
/// request-building body.
/// </summary>
public interface ISubtitleStreamService
{
    /// <summary>
    /// Streams a subtitle. The caller is responsible for authorizing the request.
    /// </summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="mediaSourceId">The media source id.</param>
    /// <param name="index">The subtitle stream index.</param>
    /// <param name="format">The subtitle format (e.g. <c>vtt</c>, <c>srt</c>, or empty for passthrough).</param>
    /// <param name="startPositionTicks">The start position of the subtitle in ticks.</param>
    /// <param name="endPositionTicks">Optional end position in ticks.</param>
    /// <param name="copyTimestamps">Whether to copy the timestamps.</param>
    /// <param name="addVttTimeMap">Whether to add a VTT time map.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The action result to return to the caller.</returns>
    Task<ActionResult> GetSubtitleAsync(
        Guid itemId,
        string? mediaSourceId,
        int index,
        string format,
        long startPositionTicks,
        long? endPositionTicks,
        bool copyTimestamps,
        bool addVttTimeMap,
        CancellationToken cancellationToken);
}
