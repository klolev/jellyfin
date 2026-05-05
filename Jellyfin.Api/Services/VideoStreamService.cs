using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Helpers;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Services;

/// <inheritdoc/>
public class VideoStreamService : IVideoStreamService
{
    private const TranscodingJobType JobType = TranscodingJobType.Progressive;

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly IServerConfigurationManager _serverConfigurationManager;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly ITranscodeManager _transcodeManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EncodingHelper _encodingHelper;

    /// <summary>
    /// Initializes a new instance of the <see cref="VideoStreamService"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="mediaSourceManager">Instance of the <see cref="IMediaSourceManager"/> interface.</param>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="mediaEncoder">Instance of the <see cref="IMediaEncoder"/> interface.</param>
    /// <param name="transcodeManager">Instance of the <see cref="ITranscodeManager"/> interface.</param>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
    /// <param name="encodingHelper">Instance of <see cref="EncodingHelper"/>.</param>
    public VideoStreamService(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IMediaSourceManager mediaSourceManager,
        IServerConfigurationManager serverConfigurationManager,
        IMediaEncoder mediaEncoder,
        ITranscodeManager transcodeManager,
        IHttpClientFactory httpClientFactory,
        EncodingHelper encodingHelper)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _mediaSourceManager = mediaSourceManager;
        _serverConfigurationManager = serverConfigurationManager;
        _mediaEncoder = mediaEncoder;
        _transcodeManager = transcodeManager;
        _httpClientFactory = httpClientFactory;
        _encodingHelper = encodingHelper;
    }

    /// <inheritdoc/>
    public async Task<ActionResult> StreamAsync(
        VideoRequestDto request,
        HttpContext httpContext,
        bool isHeadRequest,
        CancellationToken cancellationToken)
    {
        // CTS lifecycle: ownership transfers to GetTranscodedFile on the transcode path;
        // all other paths (including exceptions) dispose it here.
        var cancellationTokenSource = new CancellationTokenSource();
        StreamState state;
        try
        {
            state = await StreamingHelpers.GetStreamingState(
                    request,
                    httpContext,
                    _mediaSourceManager,
                    _userManager,
                    _libraryManager,
                    _serverConfigurationManager,
                    _mediaEncoder,
                    _encodingHelper,
                    _transcodeManager,
                    JobType,
                    cancellationTokenSource.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            cancellationTokenSource.Dispose();
            throw;
        }

        if (request.Static && state.DirectStreamProvider is not null)
        {
            cancellationTokenSource.Dispose();
            var liveStreamInfo = _mediaSourceManager.GetLiveStreamInfo(request.LiveStreamId);
            if (liveStreamInfo is null)
            {
                return new NotFoundResult();
            }

            var liveStream = new ProgressiveFileStream(liveStreamInfo.GetStream());
            // TODO (moved from MediaBrowser.Api): Don't hardcode contentType
            return new FileStreamResult(liveStream, MimeTypes.GetMimeType("file.ts"));
        }

        // Static remote stream
        if (request.Static && state.InputProtocol == MediaProtocol.Http)
        {
            cancellationTokenSource.Dispose();
            var httpClient = _httpClientFactory.CreateClient(NamedClient.Default);
            return await FileStreamResponseHelpers.GetStaticRemoteStreamResult(state, httpClient, httpContext, cancellationToken).ConfigureAwait(false);
        }

        if (request.Static && state.InputProtocol != MediaProtocol.File)
        {
            cancellationTokenSource.Dispose();
            return new BadRequestObjectResult($"Input protocol {state.InputProtocol} cannot be streamed statically");
        }

        // Static stream
        if (request.Static && !(state.MediaSource.VideoType == VideoType.BluRay || state.MediaSource.VideoType == VideoType.Dvd))
        {
            cancellationTokenSource.Dispose();
            var contentType = state.GetMimeType("." + state.OutputContainer, false) ?? state.GetMimeType(state.MediaPath);

            if (state.MediaSource.IsInfiniteStream)
            {
                var liveStream = new ProgressiveFileStream(state.MediaPath, null, _transcodeManager);
                return new FileStreamResult(liveStream, contentType);
            }

            return FileStreamResponseHelpers.GetStaticFileResult(
                state.MediaPath,
                contentType);
        }

        // Need to start ffmpeg (because media can't be returned directly)
        var encodingOptions = _serverConfigurationManager.GetEncodingOptions();
        var ffmpegCommandLineArguments = _encodingHelper.GetProgressiveVideoFullCommandLine(state, encodingOptions, EncoderPreset.superfast);
        return await FileStreamResponseHelpers.GetTranscodedFile(
            state,
            isHeadRequest,
            httpContext,
            _transcodeManager,
            ffmpegCommandLineArguments,
            JobType,
            cancellationTokenSource).ConfigureAwait(false);
    }
}
