using FFmpeg.AutoGen.Abstractions;
using nathanbutlerDEV.mt.net.Models;
using nathanbutlerDEV.mt.net.Utilities;

namespace nathanbutlerDEV.mt.net.Services;

public class VideoProcessor
{
    /// <summary>
    /// Extracts metadata from the specified video file using FFmpeg.AutoGen.
    /// </summary>
    /// <param name="videoPath">The path to the video file.</param>
    /// <returns>A HeaderInfo object containing the extracted metadata.</returns>
    public static unsafe Task<HeaderInfo> GetVideoMetadataAsync(string videoPath)
    {
        return Task.Run(() =>
        {
            AVFormatContext* pFormatContext = ffmpeg.avformat_alloc_context();

            try
            {
                // Open input file
                ffmpeg.avformat_open_input(&pFormatContext, videoPath, null, null).ThrowExceptionIfError();
                ffmpeg.avformat_find_stream_info(pFormatContext, null).ThrowExceptionIfError();

                // Find video stream
                AVCodec* videoCodec = null;
                var videoStreamIndex = ffmpeg
                    .av_find_best_stream(pFormatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &videoCodec, 0)
                    .ThrowExceptionIfError();

                var videoStream = pFormatContext->streams[videoStreamIndex];

                // Find audio stream (may not exist)
                AVCodec* audioCodec = null;
                var audioStreamIndex = ffmpeg
                    .av_find_best_stream(pFormatContext, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, &audioCodec, 0);

                // Extract video codec name
                var videoCodecName = videoCodec != null
                    ? System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)videoCodec->name) ?? "unknown"
                    : "unknown";

                // Extract audio codec name
                var audioCodecName = "unknown";
                if (audioStreamIndex >= 0 && audioCodec != null)
                {
                    audioCodecName = System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)audioCodec->name) ?? "unknown";
                }

                // Extract format name
                var formatName = pFormatContext->iformat != null
                    ? System.Runtime.InteropServices.Marshal.PtrToStringAnsi((IntPtr)pFormatContext->iformat->name) ?? "unknown"
                    : "unknown";

                // Calculate duration
                TimeSpan duration;
                if (videoStream->duration != ffmpeg.AV_NOPTS_VALUE)
                {
                    duration = TimeSpan.FromSeconds(videoStream->duration * ffmpeg.av_q2d(videoStream->time_base));
                }
                else if (pFormatContext->duration != ffmpeg.AV_NOPTS_VALUE)
                {
                    duration = TimeSpan.FromSeconds(pFormatContext->duration / (double)ffmpeg.AV_TIME_BASE);
                }
                else
                {
                    duration = TimeSpan.Zero;
                }

                // Calculate frame rate
                double frameRate;
                var avgFrameRate = videoStream->avg_frame_rate;
                if (avgFrameRate.num != 0 && avgFrameRate.den != 0)
                {
                    frameRate = ffmpeg.av_q2d(avgFrameRate);
                }
                else
                {
                    frameRate = 0;
                }

                // Get dimensions
                var width = videoStream->codecpar->width;
                var height = videoStream->codecpar->height;

                // Get bitrate
                var bitRate = pFormatContext->bit_rate;

                return new HeaderInfo
                {
                    Filename = Path.GetFileName(videoPath),
                    FilePath = videoPath,
                    FileSize = new FileInfo(videoPath).Length,
                    Duration = duration,
                    Width = width,
                    Height = height,
                    VideoCodec = videoCodecName,
                    AudioCodec = audioCodecName,
                    FrameRate = frameRate,
                    BitRate = bitRate,
                    Format = formatName
                };
            }
            finally
            {
                // Clean up
                if (pFormatContext != null)
                {
                    ffmpeg.avformat_close_input(&pFormatContext);
                }
            }
        });
    }

    public static List<TimeSpan> CalculateTimestamps(
        TimeSpan duration,
        ThumbnailOptions options)
    {
        var timestamps = new List<TimeSpan>();

        // Parse from and to times. TimeSpanParser reports malformed input as an ArgumentException
        // with the offending string; TimeSpan.Parse used to throw a bare FormatException from here.
        var fromTime = TimeSpanParser.ParseTimeString(options.From);
        var endTime = options.End == "00:00:00" ? duration : TimeSpanParser.ParseTimeString(options.End);

        // Handle skip credits - cut off last 2 minutes or 10% of duration
        if (options.SkipCredits)
        {
            var creditsDuration = TimeSpan.FromMinutes(2);
            var tenPercent = TimeSpan.FromSeconds(duration.TotalSeconds * 0.1);
            var skipDuration = creditsDuration > tenPercent ? tenPercent : creditsDuration;
            endTime = duration - skipDuration;
        }

        // Ensure endTime doesn't exceed duration
        if (endTime > duration)
        {
            endTime = duration;
        }

        if (fromTime < TimeSpan.Zero)
        {
            fromTime = TimeSpan.Zero;
        }

        // An empty or inverted range yields a negative step and silently produces garbage
        // timestamps, so say so instead.
        if (fromTime >= endTime)
        {
            throw new ArgumentException(
                $"Capture range is empty: --from {fromTime:hh\\:mm\\:ss} is not before the end of the range ({endTime:hh\\:mm\\:ss}).");
        }

        var workingDuration = endTime - fromTime;

        // Calculate timestamps based on interval or numcaps
        if (options.Interval > 0)
        {
            // Use interval-based calculation
            var intervalSeconds = options.Interval;
            var currentTime = fromTime;

            while (currentTime < endTime)
            {
                timestamps.Add(currentTime);
                currentTime = currentTime.Add(TimeSpan.FromSeconds(intervalSeconds));
            }
        }
        else
        {
            // Use numcaps-based calculation
            var numCaps = options.NumCaps;

            if (numCaps <= 1)
            {
                timestamps.Add(fromTime + TimeSpan.FromSeconds(workingDuration.TotalSeconds / 2));
            }
            else
            {
                // Use (numCaps + 1) to ensure frames are extractable (not at exact video end)
                // This spacing ensures the last frame is safely before the video end
                var step = workingDuration.TotalSeconds / (numCaps + 1);

                for (int i = 1; i <= numCaps; i++)
                {
                    var timestamp = fromTime + TimeSpan.FromSeconds(step * i);
                    timestamps.Add(timestamp);
                }
            }
        }

        return timestamps;
    }

    /// <summary>A frame chosen for the sheet, with the measurements behind the choice.</summary>
    /// <param name="Image">The decoded frame. The caller owns it.</param>
    /// <param name="Timestamp">Where it was actually taken from, which may differ from the target.</param>
    /// <param name="Analysis">Its metrics, reused by deduplication.</param>
    /// <param name="FellBack">True when no candidate passed and this is the least-bad one.</param>
    public sealed record FrameSelection(
        RgbaImage Image,
        TimeSpan Timestamp,
        FrameAnalysis Analysis,
        bool FellBack);

    /// <summary>
    /// Picks the frame to use for one grid position, retrying forward when a candidate is
    /// rejected and falling back to the best candidate seen if none passes.
    /// </summary>
    /// <param name="decoder">An already-open decoder for the file.</param>
    /// <param name="timestamp">Target timestamp.</param>
    /// <param name="options">Thumbnail options.</param>
    /// <param name="needs">Which metrics to compute.</param>
    /// <param name="chosenFingerprints">Fingerprints already accepted, for <c>--dedupe</c>.</param>
    /// <returns>The chosen frame, or null when nothing could be decoded at all.</returns>
    /// <remarks>
    /// Takes an already-open decoder; v2 opened a new one per frame. The fallback matters as much
    /// as the retry: v2 returned a frame it had already disposed when the last attempt was
    /// rejected, and v3.0 dropped the thumbnail outright, so an aggressive threshold could
    /// silently shrink the sheet or fail the run. Keeping the least-bad candidate means
    /// <c>--numcaps N</c> yields N thumbnails whatever the detectors decide.
    /// </remarks>
    public static async Task<FrameSelection?> SelectFrameAsync(
        FFmpegAutoGenVideoDecoder decoder,
        TimeSpan timestamp,
        ThumbnailOptions options,
        AnalysisNeeds needs,
        IReadOnlyList<ulong> chosenFingerprints)
    {
        var startTimestamp = options.SceneDetect
            ? await FindSceneStartAsync(decoder, timestamp, options)
            : timestamp;

        var currentTimestamp = startTimestamp;

        RgbaImage? best = null;
        var bestTimestamp = startTimestamp;
        var bestAnalysis = default(FrameAnalysis);
        var bestRatio = double.NegativeInfinity;

        for (var attempt = 0; attempt < options.Retries; attempt++)
        {
            var frame = await DecodeAsync(decoder, currentTimestamp, options);

            if (frame == null)
            {
                currentTimestamp = currentTimestamp.Add(TimeSpan.FromSeconds(options.RetryStep));
                continue;
            }

            // No checks enabled means the first frame that decodes is the answer.
            if (needs == AnalysisNeeds.None)
            {
                return new FrameSelection(frame, currentTimestamp, default, false);
            }

            var analysis = ContentDetectionService.Analyse(frame, needs);
            var ratio = ContentDetectionService.AcceptanceRatio(analysis, options);

            if (options.Dedupe && chosenFingerprints.Count > 0)
            {
                ratio = Math.Min(ratio, DuplicateRatio(analysis.Fingerprint, chosenFingerprints, options));
            }

            if (ratio >= 1.0)
            {
                best?.Dispose();
                return new FrameSelection(frame, currentTimestamp, analysis, false);
            }

            ConsoleOutput.Verbose(
                $"  {currentTimestamp:hh\\:mm\\:ss} rejected: {DescribeRejection(analysis, options, chosenFingerprints)}");

            if (ratio > bestRatio)
            {
                best?.Dispose();
                best = frame;
                bestTimestamp = currentTimestamp;
                bestAnalysis = analysis;
                bestRatio = ratio;
            }
            else
            {
                frame.Dispose();
            }

            currentTimestamp = currentTimestamp.Add(TimeSpan.FromSeconds(options.RetryStep));
        }

        if (best == null)
        {
            return null;
        }

        ConsoleOutput.Verbose(
            $"  no candidate passed after {options.Retries} attempts; keeping {bestTimestamp:hh\\:mm\\:ss}");

        return new FrameSelection(best, bestTimestamp, bestAnalysis, true);
    }

    /// <summary>
    /// Looks forward from <paramref name="timestamp"/> for the start of a new shot.
    /// </summary>
    /// <remarks>
    /// Samples the window at <c>--retry-step</c> intervals and returns the sample that differs
    /// most from the one before it, provided the difference clears
    /// <see cref="SceneChangeMinDistance"/>. Falling on a hard cut is what makes a contact sheet
    /// look representative rather than arbitrary. Costs one decode per sample, so it pairs well
    /// with <c>--fast</c>.
    /// </remarks>
    private static async Task<TimeSpan> FindSceneStartAsync(
        FFmpegAutoGenVideoDecoder decoder,
        TimeSpan timestamp,
        ThumbnailOptions options)
    {
        var step = TimeSpan.FromSeconds(Math.Max(0.25, options.RetryStep));
        var samples = Math.Max(2, (int)(options.SceneWindow / step.TotalSeconds));

        ulong previous = 0;
        var havePrevious = false;

        var bestTimestamp = timestamp;
        var bestDistance = 0;

        for (var i = 0; i < samples; i++)
        {
            var at = timestamp.Add(step * i);

            using var frame = await DecodeAsync(decoder, at, options);
            if (frame == null)
            {
                break;
            }

            var fingerprint = ContentDetectionService.Analyse(frame, AnalysisNeeds.Fingerprint).Fingerprint;

            if (havePrevious)
            {
                var distance = ContentDetectionService.FingerprintDistance(previous, fingerprint);
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    bestTimestamp = at;
                }
            }

            previous = fingerprint;
            havePrevious = true;
        }

        if (bestDistance >= SceneChangeMinDistance)
        {
            ConsoleOutput.Verbose(
                $"  scene change {bestDistance}/64 found at {bestTimestamp:hh\\:mm\\:ss} (target {timestamp:hh\\:mm\\:ss})");
            return bestTimestamp;
        }

        ConsoleOutput.Verbose($"  no scene change within {options.SceneWindow:F0}s of {timestamp:hh\\:mm\\:ss}");
        return timestamp;
    }

    /// <summary>Fingerprint distance, out of 64, that counts as a shot boundary.</summary>
    private const int SceneChangeMinDistance = 12;

    private static async Task<RgbaImage?> DecodeAsync(
        FFmpegAutoGenVideoDecoder decoder, TimeSpan at, ThumbnailOptions options)
    {
        try
        {
            return await Task.Run(() => decoder.SeekAndExtractFrame(at, options.Fast));
        }
        catch (Exception ex)
        {
            ConsoleOutput.Verbose($"  {at:hh\\:mm\\:ss} failed to decode: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Scores a candidate against frames already chosen: below 1.0 when it is a near-duplicate,
    /// scaled by how similar so the least-duplicated candidate still wins a fallback.
    /// </summary>
    private static double DuplicateRatio(
        ulong fingerprint, IReadOnlyList<ulong> chosen, ThumbnailOptions options)
    {
        if (options.DedupeThreshold <= 0)
        {
            return double.PositiveInfinity;
        }

        var nearest = int.MaxValue;
        foreach (var other in chosen)
        {
            nearest = Math.Min(nearest, ContentDetectionService.FingerprintDistance(fingerprint, other));
        }

        return nearest >= options.DedupeThreshold
            ? double.PositiveInfinity
            : nearest / (double)options.DedupeThreshold;
    }

    private static string DescribeRejection(
        FrameAnalysis analysis, ThumbnailOptions options, IReadOnlyList<ulong> chosen)
    {
        if (options.Dedupe && chosen.Count > 0 &&
            DuplicateRatio(analysis.Fingerprint, chosen, options) < 1.0)
        {
            return "near-duplicate of a frame already chosen";
        }

        return ContentDetectionService.DescribeRejection(analysis, options);
    }
}
