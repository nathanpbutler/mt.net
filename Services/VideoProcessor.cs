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

    /// <summary>
    /// Extracts one frame at <paramref name="timestamp"/>, stepping forward a second at a time
    /// when the frame cannot be decoded or is rejected by <paramref name="skipCondition"/>.
    /// </summary>
    /// <remarks>
    /// Takes an already-open decoder. Until v3 this opened a brand new
    /// <see cref="FFmpegAutoGenVideoDecoder"/> for every frame, re-parsing the container once per
    /// thumbnail; the caller now opens one per file and seeks within it, which is what
    /// <c>SeekAndExtractFrame</c> was always built to support (it seeks and flushes the codec
    /// buffers on entry, so repeated calls are independent).
    /// </remarks>
    public static async Task<RgbaImage?> ExtractFrameWithRetriesAsync(
        FFmpegAutoGenVideoDecoder decoder,
        TimeSpan timestamp,
        ThumbnailOptions options,
        Func<RgbaImage, bool>? skipCondition = null,
        int maxRetries = 3)
    {
        RgbaImage? frame = null;
        var currentTimestamp = timestamp;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                frame = await Task.Run(() => decoder.SeekAndExtractFrame(currentTimestamp, options.Fast));
            }
            catch (Exception ex)
            {
                ConsoleOutput.Verbose($"Frame at {currentTimestamp:hh\\:mm\\:ss} failed to decode: {ex.Message}");
                frame = null;
            }

            if (frame == null)
            {
                currentTimestamp = currentTimestamp.Add(TimeSpan.FromSeconds(1));
                continue;
            }

            if (skipCondition != null && skipCondition(frame))
            {
                ConsoleOutput.Verbose($"Frame at {currentTimestamp:hh\\:mm\\:ss} rejected by content detection; retrying.");
                frame.Dispose();
                frame = null;
                currentTimestamp = currentTimestamp.Add(TimeSpan.FromSeconds(1));
                continue;
            }

            return frame;
        }

        return frame;
    }
}
