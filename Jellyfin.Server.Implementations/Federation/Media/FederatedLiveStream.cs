using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Server.Implementations.Federation.Media;

/// <summary>
/// A minimal <see cref="ILiveStream"/> for federation remote sources. The stream URL is set on the
/// <see cref="MediaSource"/> — the player streams directly from the remote host. There's no local
/// pipe to read, so <see cref="GetStream"/> throws.
/// </summary>
internal sealed class FederatedLiveStream : ILiveStream
{
    public FederatedLiveStream(MediaSourceInfo mediaSource)
    {
        MediaSource = mediaSource;
        UniqueId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        ConsumerCount = 1;
        EnableStreamSharing = false;
    }

    public int ConsumerCount { get; set; }

    public string OriginalStreamId { get; set; } = string.Empty;

    public string? TunerHostId => null;

    public bool EnableStreamSharing { get; }

    public MediaSourceInfo MediaSource { get; set; }

    public string UniqueId { get; }

    public Task Close() => Task.CompletedTask;

    public Stream GetStream()
        => throw new NotSupportedException("Federation live streams are consumed via MediaSource.Path — no local pipe.");

    public Task Open(CancellationToken openCancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
    }
}
