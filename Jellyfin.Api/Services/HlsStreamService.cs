using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Extensions;
using Jellyfin.Api.Helpers;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using Jellyfin.MediaEncoding.Hls.Playlist;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.MediaEncoding.Encoder;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.Services;

/// <inheritdoc/>
public class HlsStreamService : IHlsStreamService
{
    private const EncoderPreset DefaultVodEncoderPreset = EncoderPreset.veryfast;
    private const EncoderPreset DefaultEventEncoderPreset = EncoderPreset.superfast;
    private const TranscodingJobType JobType = TranscodingJobType.Hls;

    private readonly Version _minFFmpegFlacInMp4 = new Version(6, 0);
    private readonly Version _minFFmpegX265BframeInFmp4 = new Version(7, 0, 1);
    private readonly Version _minFFmpegHlsSegmentOptions = new Version(5, 0);

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly IServerConfigurationManager _serverConfigurationManager;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IFileSystem _fileSystem;
    private readonly ITranscodeManager _transcodeManager;
    private readonly ILogger<HlsStreamService> _logger;
    private readonly EncodingHelper _encodingHelper;
    private readonly IDynamicHlsPlaylistGenerator _dynamicHlsPlaylistGenerator;
    private readonly EncodingOptions _encodingOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="HlsStreamService"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="mediaSourceManager">Instance of the <see cref="IMediaSourceManager"/> interface.</param>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="mediaEncoder">Instance of the <see cref="IMediaEncoder"/> interface.</param>
    /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
    /// <param name="transcodeManager">Instance of the <see cref="ITranscodeManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{HlsStreamService}"/> interface.</param>
    /// <param name="encodingHelper">Instance of <see cref="EncodingHelper"/>.</param>
    /// <param name="dynamicHlsPlaylistGenerator">Instance of the <see cref="IDynamicHlsPlaylistGenerator"/> interface.</param>
    public HlsStreamService(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IMediaSourceManager mediaSourceManager,
        IServerConfigurationManager serverConfigurationManager,
        IMediaEncoder mediaEncoder,
        IFileSystem fileSystem,
        ITranscodeManager transcodeManager,
        ILogger<HlsStreamService> logger,
        EncodingHelper encodingHelper,
        IDynamicHlsPlaylistGenerator dynamicHlsPlaylistGenerator)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _mediaSourceManager = mediaSourceManager;
        _serverConfigurationManager = serverConfigurationManager;
        _mediaEncoder = mediaEncoder;
        _fileSystem = fileSystem;
        _transcodeManager = transcodeManager;
        _logger = logger;
        _encodingHelper = encodingHelper;
        _dynamicHlsPlaylistGenerator = dynamicHlsPlaylistGenerator;

        _encodingOptions = serverConfigurationManager.GetEncodingOptions();
    }

    /// <inheritdoc/>
    public async Task<ActionResult> GetVariantPlaylistAsync(
        StreamingRequestDto streamingRequest,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        using var state = await StreamingHelpers.GetStreamingState(
                streamingRequest,
                httpContext,
                _mediaSourceManager,
                _userManager,
                _libraryManager,
                _serverConfigurationManager,
                _mediaEncoder,
                _encodingHelper,
                _transcodeManager,
                JobType,
                cancellationToken)
            .ConfigureAwait(false);
        var mediaSourceId = state.BaseRequest.MediaSourceId;
        double fps = state.TargetFramerate ?? 0.0f;
        int segmentLength = state.SegmentLength * 1000;

        // If video is transcoded and framerate is fractional (i.e. 23.976), we need to slightly adjust segment length
        if (!EncodingHelper.IsCopyCodec(state.OutputVideoCodec) && Math.Abs(fps - Math.Floor(fps + 0.001f)) > 0.001)
        {
            double nearestIntFramerate = Math.Ceiling(fps);
            segmentLength = (int)Math.Ceiling(segmentLength * (nearestIntFramerate / fps));
        }

        var request = new CreateMainPlaylistRequest(
            mediaSourceId is null ? null : Guid.Parse(mediaSourceId),
            state.MediaPath,
            segmentLength,
            state.RunTimeTicks ?? 0,
            state.Request.SegmentContainer ?? string.Empty,
            "hls1/main/",
            httpContext.Request.QueryString.ToString(),
            EncodingHelper.IsCopyCodec(state.OutputVideoCodec));
        var playlist = _dynamicHlsPlaylistGenerator.CreateMainPlaylist(request);

        return new FileContentResult(Encoding.UTF8.GetBytes(playlist), MimeTypes.GetMimeType("playlist.m3u8"));
    }

    /// <inheritdoc/>
    public async Task<ActionResult> GetSegmentAsync(
        StreamingRequestDto streamingRequest,
        int segmentId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if ((streamingRequest.StartTimeTicks ?? 0) > 0)
        {
            throw new ArgumentException("StartTimeTicks is not allowed.");
        }

        // CTS lifecycle is managed internally.
        var cancellationTokenSource = new CancellationTokenSource();
        var internalCancellationToken = cancellationTokenSource.Token;

        var state = await StreamingHelpers.GetStreamingState(
                streamingRequest,
                httpContext,
                _mediaSourceManager,
                _userManager,
                _libraryManager,
                _serverConfigurationManager,
                _mediaEncoder,
                _encodingHelper,
                _transcodeManager,
                JobType,
                internalCancellationToken)
            .ConfigureAwait(false);

        var playlistPath = Path.ChangeExtension(state.OutputFilePath, ".m3u8");

        var segmentPath = GetSegmentPath(state, playlistPath, segmentId);

        var segmentExtension = EncodingHelper.GetSegmentFileExtension(state.Request.SegmentContainer);

        TranscodingJob? job;

        if (System.IO.File.Exists(segmentPath))
        {
            job = _transcodeManager.OnTranscodeBeginRequest(playlistPath, JobType);
            _logger.LogDebug("returning {0} [it exists, try 1]", segmentPath);
            return await GetSegmentResult(state, playlistPath, segmentPath, segmentExtension, segmentId, job, httpContext, internalCancellationToken).ConfigureAwait(false);
        }

        using (await _transcodeManager.LockAsync(playlistPath, internalCancellationToken).ConfigureAwait(false))
        {
            var startTranscoding = false;
            if (System.IO.File.Exists(segmentPath))
            {
                job = _transcodeManager.OnTranscodeBeginRequest(playlistPath, JobType);
                _logger.LogDebug("returning {0} [it exists, try 2]", segmentPath);
                return await GetSegmentResult(state, playlistPath, segmentPath, segmentExtension, segmentId, job, httpContext, internalCancellationToken).ConfigureAwait(false);
            }

            var currentTranscodingIndex = GetCurrentTranscodingIndex(playlistPath, segmentExtension);
            var segmentGapRequiringTranscodingChange = 24 / state.SegmentLength;

            if (segmentId == -1)
            {
                _logger.LogDebug("Starting transcoding because fmp4 init file is being requested");
                startTranscoding = true;
                segmentId = 0;
            }
            else if (currentTranscodingIndex is null)
            {
                _logger.LogDebug("Starting transcoding because currentTranscodingIndex=null");
                startTranscoding = true;
            }
            else if (segmentId < currentTranscodingIndex.Value)
            {
                _logger.LogDebug("Starting transcoding because requestedIndex={0} and currentTranscodingIndex={1}", segmentId, currentTranscodingIndex);
                startTranscoding = true;
            }
            else if (segmentId - currentTranscodingIndex.Value > segmentGapRequiringTranscodingChange)
            {
                _logger.LogDebug("Starting transcoding because segmentGap is {0} and max allowed gap is {1}. requestedIndex={2}", segmentId - currentTranscodingIndex.Value, segmentGapRequiringTranscodingChange, segmentId);
                startTranscoding = true;
            }

            if (startTranscoding)
            {
                // If the playlist doesn't already exist, startup ffmpeg
                try
                {
                    await _transcodeManager.KillTranscodingJobs(streamingRequest.DeviceId, streamingRequest.PlaySessionId, p => false)
                        .ConfigureAwait(false);

                    if (currentTranscodingIndex.HasValue)
                    {
                        await DeleteLastFile(playlistPath, segmentExtension, 0).ConfigureAwait(false);
                    }

                    streamingRequest.StartTimeTicks = streamingRequest.CurrentRuntimeTicks;

                    state.WaitForPath = segmentPath;
                    job = await _transcodeManager.StartFfMpeg(
                        state,
                        playlistPath,
                        GetCommandLineArguments(playlistPath, state, false, segmentId),
                        httpContext.User.GetUserId(),
                        JobType,
                        cancellationTokenSource).ConfigureAwait(false);
                }
                catch
                {
                    state.Dispose();
                    throw;
                }

                // await WaitForMinimumSegmentCount(playlistPath, 1, cancellationTokenSource.Token).ConfigureAwait(false);
            }
            else
            {
                job = _transcodeManager.OnTranscodeBeginRequest(playlistPath, JobType);
                if (job?.TranscodingThrottler is not null)
                {
                    await job.TranscodingThrottler.UnpauseTranscoding().ConfigureAwait(false);
                }
            }
        }

        _logger.LogDebug("returning {0} [general case]", segmentPath);
        job ??= _transcodeManager.OnTranscodeBeginRequest(playlistPath, JobType);
        return await GetSegmentResult(state, playlistPath, segmentPath, segmentExtension, segmentId, job, httpContext, internalCancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public string GetCommandLineArguments(string outputPath, StreamState state, bool isEventPlaylist, int startNumber)
    {
        var videoCodec = _encodingHelper.GetVideoEncoder(state, _encodingOptions);
        var threads = EncodingHelper.GetNumberOfThreads(state, _encodingOptions, videoCodec);

        var mapArgs = state.IsOutputVideo ? _encodingHelper.GetMapArgs(state) : string.Empty;

        var directory = Path.GetDirectoryName(outputPath) ?? throw new ArgumentException($"Provided path ({outputPath}) is not valid.", nameof(outputPath));
        var outputFileNameWithoutExtension = Path.GetFileNameWithoutExtension(outputPath);
        var outputPrefix = Path.Combine(directory, outputFileNameWithoutExtension);
        var outputExtension = EncodingHelper.GetSegmentFileExtension(state.Request.SegmentContainer);
        var outputTsArg = outputPrefix + "%d" + outputExtension;

        var segmentFormat = string.Empty;
        var segmentContainer = outputExtension.TrimStart('.');
        var inputModifier = _encodingHelper.GetInputModifier(state, _encodingOptions, segmentContainer);
        var hlsArguments = $"-hls_playlist_type {(isEventPlaylist ? "event" : "vod")} -hls_list_size 0";

        if (string.Equals(segmentContainer, "ts", StringComparison.OrdinalIgnoreCase))
        {
            segmentFormat = "mpegts";
        }
        else if (string.Equals(segmentContainer, "mp4", StringComparison.OrdinalIgnoreCase))
        {
            var outputFmp4HeaderArg = OperatingSystem.IsWindows() switch
            {
                // on Windows, the path of fmp4 header file needs to be configured
                true => " -hls_fmp4_init_filename \"" + outputPrefix + "-1" + outputExtension + "\"",
                // on Linux/Unix, ffmpeg generate fmp4 header file to m3u8 output folder
                false => " -hls_fmp4_init_filename \"" + outputFileNameWithoutExtension + "-1" + outputExtension + "\""
            };

            var useLegacySegmentOption = _mediaEncoder.EncoderVersion < _minFFmpegHlsSegmentOptions;

            if (state.VideoStream is not null && state.IsOutputVideo)
            {
                // fMP4 needs this flag to write the audio packet DTS/PTS including the initial delay into MOOF::TRAF::TFDT
                hlsArguments += $" {(useLegacySegmentOption ? "-hls_ts_options" : "-hls_segment_options")} movflags=+frag_discont";
            }

            segmentFormat = "fmp4" + outputFmp4HeaderArg;
        }
        else
        {
            _logger.LogError("Invalid HLS segment container: {SegmentContainer}, default to mpegts", segmentContainer);
            segmentFormat = "mpegts";
        }

        var maxMuxingQueueSize = _encodingOptions.MaxMuxingQueueSize > 128
            ? _encodingOptions.MaxMuxingQueueSize.ToString(CultureInfo.InvariantCulture)
            : "128";

        var baseUrlParam = string.Empty;
        if (isEventPlaylist)
        {
            baseUrlParam = string.Format(
                CultureInfo.InvariantCulture,
                " -hls_base_url \"hls/{0}/\"",
                Path.GetFileNameWithoutExtension(outputPath));
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} -map_metadata -1 -map_chapters -1 -threads {2} {3} {4} {5} -copyts -avoid_negative_ts disabled -max_muxing_queue_size {6} -f hls -max_delay 5000000 -hls_time {7} -hls_segment_type {8} -start_number {9}{10} -hls_segment_filename \"{11}\" {12} -y \"{13}\"",
            inputModifier,
            _encodingHelper.GetInputArgument(state, _encodingOptions, segmentContainer),
            threads,
            mapArgs,
            GetVideoArguments(state, startNumber, isEventPlaylist, segmentContainer),
            GetAudioArguments(state),
            maxMuxingQueueSize,
            state.SegmentLength.ToString(CultureInfo.InvariantCulture),
            segmentFormat,
            startNumber.ToString(CultureInfo.InvariantCulture),
            baseUrlParam,
            EncodingUtils.NormalizePath(outputTsArg),
            hlsArguments,
            EncodingUtils.NormalizePath(outputPath)).Trim();
    }

    private string GetAudioArguments(StreamState state)
    {
        if (state.AudioStream is null)
        {
            return string.Empty;
        }

        var audioCodec = _encodingHelper.GetAudioEncoder(state);
        var bitStreamArgs = _encodingHelper.GetAudioBitStreamArguments(state, state.Request.SegmentContainer, state.MediaSource.Container);

        // opus, dts, truehd and flac (in FFmpeg 5 and older) are experimental in mp4 muxer
        var strictArgs = string.Empty;
        var actualOutputAudioCodec = state.ActualOutputAudioCodec;
        if (string.Equals(actualOutputAudioCodec, "opus", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actualOutputAudioCodec, "dts", StringComparison.OrdinalIgnoreCase)
            || string.Equals(actualOutputAudioCodec, "truehd", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(actualOutputAudioCodec, "flac", StringComparison.OrdinalIgnoreCase)
                && _mediaEncoder.EncoderVersion < _minFFmpegFlacInMp4))
        {
            strictArgs = " -strict -2";
        }

        if (!state.IsOutputVideo)
        {
            var audioTranscodeParams = string.Empty;

            // -vn to drop any video streams
            audioTranscodeParams += "-vn";

            if (EncodingHelper.IsCopyCodec(audioCodec))
            {
                return audioTranscodeParams + " -acodec copy" + bitStreamArgs + strictArgs;
            }

            audioTranscodeParams += " -acodec " + audioCodec + bitStreamArgs + strictArgs;

            var audioBitrate = state.OutputAudioBitrate;
            var audioChannels = state.OutputAudioChannels;

            if (audioBitrate.HasValue && !EncodingHelper.LosslessAudioCodecs.Contains(state.ActualOutputAudioCodec, StringComparison.OrdinalIgnoreCase))
            {
                var vbrParam = _encodingHelper.GetAudioVbrModeParam(audioCodec, audioBitrate.Value, audioChannels ?? 2);
                if (_encodingOptions.EnableAudioVbr && state.EnableAudioVbrEncoding && vbrParam is not null)
                {
                    audioTranscodeParams += vbrParam;
                }
                else
                {
                    audioTranscodeParams += " -ab " + audioBitrate.Value.ToString(CultureInfo.InvariantCulture);
                }
            }

            if (audioChannels.HasValue)
            {
                audioTranscodeParams += " -ac " + audioChannels.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (state.OutputAudioSampleRate.HasValue)
            {
                audioTranscodeParams += " -ar " + state.OutputAudioSampleRate.Value.ToString(CultureInfo.InvariantCulture);
            }

            return audioTranscodeParams;
        }

        if (EncodingHelper.IsCopyCodec(audioCodec))
        {
            var videoCodec = _encodingHelper.GetVideoEncoder(state, _encodingOptions);
            var copyArgs = "-codec:a:0 copy" + bitStreamArgs + strictArgs;

            return copyArgs;
        }

        var args = "-codec:a:0 " + audioCodec + bitStreamArgs + strictArgs;

        var channels = state.OutputAudioChannels;

        var useDownMixAlgorithm = DownMixAlgorithmsHelper.AlgorithmFilterStrings.ContainsKey((_encodingOptions.DownMixStereoAlgorithm, DownMixAlgorithmsHelper.InferChannelLayout(state.AudioStream)));

        if (channels.HasValue
            && (channels.Value != 2
                || (state.AudioStream?.Channels is not null && !useDownMixAlgorithm)))
        {
            args += " -ac " + channels.Value;
        }

        var bitrate = state.OutputAudioBitrate;
        if (bitrate.HasValue && !EncodingHelper.LosslessAudioCodecs.Contains(actualOutputAudioCodec, StringComparison.OrdinalIgnoreCase))
        {
            var vbrParam = _encodingHelper.GetAudioVbrModeParam(audioCodec, bitrate.Value, channels ?? 2);
            if (_encodingOptions.EnableAudioVbr && state.EnableAudioVbrEncoding && vbrParam is not null)
            {
                args += vbrParam;
            }
            else
            {
                args += " -ab " + bitrate.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        if (state.OutputAudioSampleRate.HasValue)
        {
            args += " -ar " + state.OutputAudioSampleRate.Value.ToString(CultureInfo.InvariantCulture);
        }
        else if (state.AudioStream?.CodecTag is not null && state.AudioStream.CodecTag.Equals("ac-4", StringComparison.Ordinal))
        {
            // ac-4 audio tends to have a super weird sample rate that will fail most encoders
            // force resample it to 48KHz
            args += " -ar 48000";
        }

        args += _encodingHelper.GetAudioFilterParam(state, _encodingOptions);

        return args;
    }

    private string GetVideoArguments(StreamState state, int startNumber, bool isEventPlaylist, string segmentContainer)
    {
        if (state.VideoStream is null)
        {
            return string.Empty;
        }

        if (!state.IsOutputVideo)
        {
            return string.Empty;
        }

        var codec = _encodingHelper.GetVideoEncoder(state, _encodingOptions);

        var args = "-codec:v:0 " + codec;

        var isActualOutputVideoCodecAv1 = string.Equals(state.ActualOutputVideoCodec, "av1", StringComparison.OrdinalIgnoreCase);
        var isActualOutputVideoCodecHevc = string.Equals(state.ActualOutputVideoCodec, "h265", StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(state.ActualOutputVideoCodec, "hevc", StringComparison.OrdinalIgnoreCase);

        if (isActualOutputVideoCodecHevc || isActualOutputVideoCodecAv1)
        {
            var requestedRange = state.GetRequestedRangeTypes(state.ActualOutputVideoCodec);
            // Clients reporting Dolby Vision capabilities with fallbacks may only support the fallback layer.
            // Only enable Dolby Vision remuxing if the client explicitly declares support for profiles without fallbacks.
            var clientSupportsDoVi = requestedRange.Contains(VideoRangeType.DOVI.ToString(), StringComparison.OrdinalIgnoreCase);
            var videoIsDoVi = EncodingHelper.IsDovi(state.VideoStream);

            if (EncodingHelper.IsCopyCodec(codec)
                && (videoIsDoVi && clientSupportsDoVi)
                && !_encodingHelper.IsDoviRemoved(state))
            {
                if (isActualOutputVideoCodecHevc)
                {
                    // Use hvc1 for 8.4. This is what Dolby uses for its official sample streams. Tagging with dvh1 would break some players with strict tag checking like Apple Safari.
                    var codecTag = state.VideoStream.VideoRangeType == VideoRangeType.DOVIWithHLG ? "hvc1" : "dvh1";
                    args += $" -tag:v:0 {codecTag} -strict -2";
                }
                else if (isActualOutputVideoCodecAv1)
                {
                    args += " -tag:v:0 dav1 -strict -2";
                }
            }
            else if (isActualOutputVideoCodecHevc)
            {
                // Prefer hvc1 to hev1
                args += " -tag:v:0 hvc1";
            }
        }

        // if  (state.EnableMpegtsM2TsMode)
        // {
        //     args += " -mpegts_m2ts_mode 1";
        // }

        // See if we can save come cpu cycles by avoiding encoding.
        if (EncodingHelper.IsCopyCodec(codec))
        {
            // If h264_mp4toannexb is ever added, do not use it for live tv.
            if (state.VideoStream is not null && !string.Equals(state.VideoStream.NalLengthSize, "0", StringComparison.OrdinalIgnoreCase))
            {
                string bitStreamArgs = _encodingHelper.GetBitStreamArgs(state, MediaStreamType.Video);
                if (!string.IsNullOrEmpty(bitStreamArgs))
                {
                    args += " " + bitStreamArgs;
                }
            }

            args += " -start_at_zero";
        }
        else
        {
            args += _encodingHelper.GetVideoQualityParam(state, codec, _encodingOptions, isEventPlaylist ? DefaultEventEncoderPreset : DefaultVodEncoderPreset);

            // Set the key frame params for video encoding to match the hls segment time.
            args += _encodingHelper.GetHlsVideoKeyFrameArguments(state, codec, state.SegmentLength, isEventPlaylist, startNumber);

            // Currently b-frames in libx265 breaks the FMP4-HLS playback on iOS, disable it for now.
            if (string.Equals(codec, "libx265", StringComparison.OrdinalIgnoreCase)
                && _mediaEncoder.EncoderVersion < _minFFmpegX265BframeInFmp4)
            {
                args += " -bf 0";
            }

            // video processing filters.
            var videoProcessParam = _encodingHelper.GetVideoProcessingFilterParam(state, _encodingOptions, codec);

            var negativeMapArgs = _encodingHelper.GetNegativeMapArgsByFilters(state, videoProcessParam);

            args = negativeMapArgs + args + videoProcessParam;

            // -start_at_zero is necessary to use with -ss when seeking,
            // otherwise the target position cannot be determined.
            if (state.SubtitleStream is not null)
            {
                // Disable start_at_zero for external graphical subs
                if (!(state.SubtitleStream.IsExternal && !state.SubtitleStream.IsTextSubtitleStream))
                {
                    args += " -start_at_zero";
                }
            }
        }

        // TODO why was this not enabled for VOD?
        if (isEventPlaylist && string.Equals(segmentContainer, "ts", StringComparison.OrdinalIgnoreCase))
        {
            args += " -flags -global_header";
        }

        if (!string.IsNullOrEmpty(state.OutputVideoSync))
        {
            args += EncodingHelper.GetVideoSyncOption(state.OutputVideoSync, _mediaEncoder.EncoderVersion);
        }

        args += _encodingHelper.GetOutputFFlags(state);

        return args;
    }

    private string GetSegmentPath(StreamState state, string playlist, int index)
    {
        var folder = Path.GetDirectoryName(playlist) ?? throw new ArgumentException($"Provided path ({playlist}) is not valid.", nameof(playlist));
        var filename = Path.GetFileNameWithoutExtension(playlist);

        return Path.Combine(folder, filename + index.ToString(CultureInfo.InvariantCulture) + EncodingHelper.GetSegmentFileExtension(state.Request.SegmentContainer));
    }

    private async Task<ActionResult> GetSegmentResult(
        StreamState state,
        string playlistPath,
        string segmentPath,
        string segmentExtension,
        int segmentIndex,
        TranscodingJob? transcodingJob,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var segmentExists = System.IO.File.Exists(segmentPath);
        if (segmentExists)
        {
            if (transcodingJob is not null && transcodingJob.HasExited)
            {
                // Transcoding job is over, so assume all existing files are ready
                _logger.LogDebug("serving up {0} as transcode is over", segmentPath);
                return GetSegmentResult(state, segmentPath, transcodingJob, httpContext);
            }

            var currentTranscodingIndex = GetCurrentTranscodingIndex(playlistPath, segmentExtension);

            // If requested segment is less than transcoding position, we can't transcode backwards, so assume it's ready
            if (segmentIndex < currentTranscodingIndex)
            {
                _logger.LogDebug("serving up {0} as transcode index {1} is past requested point {2}", segmentPath, currentTranscodingIndex, segmentIndex);
                return GetSegmentResult(state, segmentPath, transcodingJob, httpContext);
            }
        }

        var nextSegmentPath = GetSegmentPath(state, playlistPath, segmentIndex + 1);
        if (transcodingJob is not null)
        {
            while (!cancellationToken.IsCancellationRequested && !transcodingJob.HasExited)
            {
                // To be considered ready, the segment file has to exist AND
                // either the transcoding job should be done or next segment should also exist
                if (segmentExists)
                {
                    if (transcodingJob.HasExited || System.IO.File.Exists(nextSegmentPath))
                    {
                        _logger.LogDebug("Serving up {SegmentPath} as it deemed ready", segmentPath);
                        return GetSegmentResult(state, segmentPath, transcodingJob, httpContext);
                    }
                }
                else
                {
                    segmentExists = System.IO.File.Exists(segmentPath);
                    if (segmentExists)
                    {
                        continue; // avoid unnecessary waiting if segment just became available
                    }
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            if (!System.IO.File.Exists(segmentPath))
            {
                _logger.LogWarning("cannot serve {0} as transcoding quit before we got there", segmentPath);
            }
            else
            {
                _logger.LogDebug("serving {0} as it's on disk and transcoding stopped", segmentPath);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        else
        {
            _logger.LogWarning("cannot serve {0} as it doesn't exist and no transcode is running", segmentPath);
        }

        return GetSegmentResult(state, segmentPath, transcodingJob, httpContext);
    }

    private ActionResult GetSegmentResult(StreamState state, string segmentPath, TranscodingJob? transcodingJob, HttpContext httpContext)
    {
        var segmentEndingPositionTicks = state.Request.CurrentRuntimeTicks + state.Request.ActualSegmentLengthTicks;

        httpContext.Response.OnCompleted(() =>
        {
            _logger.LogDebug("Finished serving {SegmentPath}", segmentPath);
            if (transcodingJob is not null)
            {
                transcodingJob.DownloadPositionTicks = Math.Max(transcodingJob.DownloadPositionTicks ?? segmentEndingPositionTicks, segmentEndingPositionTicks);
                _transcodeManager.OnTranscodeEndRequest(transcodingJob);
            }

            return Task.CompletedTask;
        });

        return FileStreamResponseHelpers.GetStaticFileResult(segmentPath, MimeTypes.GetMimeType(segmentPath));
    }

    private int? GetCurrentTranscodingIndex(string playlist, string segmentExtension)
    {
        var job = _transcodeManager.GetTranscodingJob(playlist, JobType);

        if (job is null || job.HasExited)
        {
            return null;
        }

        var file = GetLastTranscodingFile(playlist, segmentExtension, _fileSystem);

        if (file is null)
        {
            return null;
        }

        var playlistFilename = Path.GetFileNameWithoutExtension(playlist.AsSpan());

        var indexString = Path.GetFileNameWithoutExtension(file.Name.AsSpan()).Slice(playlistFilename.Length);

        return int.Parse(indexString, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static FileSystemMetadata? GetLastTranscodingFile(string playlist, string segmentExtension, IFileSystem fileSystem)
    {
        var folder = Path.GetDirectoryName(playlist) ?? throw new ArgumentException("Path can't be a root directory.", nameof(playlist));

        var filePrefix = Path.GetFileNameWithoutExtension(playlist);

        try
        {
            return fileSystem.GetFiles(folder, new[] { segmentExtension }, true, false)
                .Where(i => Path.GetFileNameWithoutExtension(i.Name).StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
                .MaxBy(fileSystem.GetLastWriteTimeUtc);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private async Task DeleteLastFile(string playlistPath, string segmentExtension, int retryCount)
    {
        var file = GetLastTranscodingFile(playlistPath, segmentExtension, _fileSystem);

        if (file is null)
        {
            return;
        }

        await DeleteFile(file.FullName, retryCount).ConfigureAwait(false);
    }

    private async Task DeleteFile(string path, int retryCount)
    {
        if (retryCount >= 5)
        {
            return;
        }

        _logger.LogDebug("Deleting partial HLS file {Path}", path);

        try
        {
            _fileSystem.DeleteFile(path);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Error deleting partial stream file(s) {Path}", path);

            await Task.Delay(100).ConfigureAwait(false);
            await DeleteFile(path, retryCount + 1).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting partial stream file(s) {Path}", path);
        }
    }
}
