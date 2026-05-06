using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Net;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Services;

/// <inheritdoc/>
public class SubtitleStreamService : ISubtitleStreamService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly ISubtitleEncoder _subtitleEncoder;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleStreamService"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="mediaSourceManager">Instance of the <see cref="IMediaSourceManager"/> interface.</param>
    /// <param name="subtitleEncoder">Instance of the <see cref="ISubtitleEncoder"/> interface.</param>
    public SubtitleStreamService(
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        ISubtitleEncoder subtitleEncoder)
    {
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
        _subtitleEncoder = subtitleEncoder;
    }

    /// <inheritdoc/>
    public async Task<ActionResult> GetSubtitleAsync(
        Guid itemId,
        string? mediaSourceId,
        int index,
        string format,
        long startPositionTicks,
        long? endPositionTicks,
        bool copyTimestamps,
        bool addVttTimeMap,
        CancellationToken cancellationToken)
    {
        if (string.Equals(format, "js", StringComparison.OrdinalIgnoreCase))
        {
            format = "json";
        }

        if (string.IsNullOrEmpty(format))
        {
            var item = _libraryManager.GetItemById<Video>(itemId);

            var idString = itemId.ToString("N", CultureInfo.InvariantCulture);
            var mediaSource = _mediaSourceManager.GetStaticMediaSources(item, false)
                .First(i => string.Equals(i.Id, mediaSourceId ?? idString, StringComparison.Ordinal));

            var subtitleStream = mediaSource.MediaStreams
                .First(i => i.Type == MediaStreamType.Subtitle && i.Index == index);

            return new PhysicalFileResult(subtitleStream.Path, MimeTypes.GetMimeType(subtitleStream.Path));
        }

        if (string.Equals(format, "vtt", StringComparison.OrdinalIgnoreCase) && addVttTimeMap)
        {
            Stream stream = await EncodeSubtitles(itemId, mediaSourceId, index, format, startPositionTicks, endPositionTicks, copyTimestamps, cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var reader = new StreamReader(stream);

                var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

                text = text.Replace("WEBVTT", "WEBVTT\nX-TIMESTAMP-MAP=MPEGTS:900000,LOCAL:00:00:00.000", StringComparison.Ordinal);

                return new FileContentResult(Encoding.UTF8.GetBytes(text), MimeTypes.GetMimeType("file." + format));
            }
        }

        return new FileStreamResult(
            await EncodeSubtitles(
                itemId,
                mediaSourceId,
                index,
                format,
                startPositionTicks,
                endPositionTicks,
                copyTimestamps,
                cancellationToken).ConfigureAwait(false),
            MimeTypes.GetMimeType("file." + format));
    }

    private Task<Stream> EncodeSubtitles(
        Guid id,
        string? mediaSourceId,
        int index,
        string format,
        long startPositionTicks,
        long? endPositionTicks,
        bool copyTimestamps,
        CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById<BaseItem>(id);

        return _subtitleEncoder.GetSubtitles(
            item,
            mediaSourceId,
            index,
            format,
            startPositionTicks,
            endPositionTicks ?? 0,
            copyTimestamps,
            cancellationToken);
    }
}
