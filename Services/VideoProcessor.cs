using FFmpeg.AutoGen.Abstractions;
using nathanbutlerDEV.mt.net.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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

        // Parse from and to times
        var fromTime = TimeSpan.Parse(options.From);
        var endTime = options.End == "00:00:00" ? duration : TimeSpan.Parse(options.End);

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

    public static async Task<List<(Image<Rgba32> Image, TimeSpan Timestamp)>> ExtractFramesAsync(
        string videoPath,
        List<TimeSpan> timestamps,
        ThumbnailOptions options)
    {
        var frames = new List<(Image<Rgba32>, TimeSpan)>();

        // Use FFmpeg.AutoGen decoder for better control over seeking
        using (var decoder = new FFmpegAutoGenVideoDecoder(videoPath))
        {
            foreach (var timestamp in timestamps)
            {
                var frame = await Task.Run(() => decoder.SeekAndExtractFrame(timestamp, options.Fast));
                if (frame != null)
                {
                    frames.Add((frame, timestamp));
                }
            }
        }

        return frames;
    }

    private static async Task<Image<Rgba32>?> ExtractFrameAtTimestampAsync(
        string videoPath,
        TimeSpan timestamp,
        ThumbnailOptions options)
    {
        // This method is now a simple wrapper around the FFmpeg.AutoGen decoder
        // It's kept for backward compatibility with the retry logic
        try
        {
            using var decoder = new FFmpegAutoGenVideoDecoder(videoPath);
            return await Task.Run(() => decoder.SeekAndExtractFrame(timestamp, options.Fast));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error extracting frame at {timestamp}: {ex.Message}");
            return null;
        }
    }

    public static async Task<Image<Rgba32>?> ExtractFrameWithRetriesAsync(
        string videoPath,
        TimeSpan timestamp,
        ThumbnailOptions options,
        Func<Image<Rgba32>, bool>? skipCondition = null,
        int maxRetries = 3)
    {
        Image<Rgba32>? frame = null;
        var currentTimestamp = timestamp;
        var retryCount = 0;

        while (retryCount < maxRetries)
        {
            frame = await ExtractFrameAtTimestampAsync(videoPath, currentTimestamp, options);

            if (frame == null)
            {
                retryCount++;
                currentTimestamp = currentTimestamp.Add(TimeSpan.FromSeconds(1));
                continue;
            }

            if (skipCondition != null && skipCondition(frame))
            {
                frame.Dispose();
                retryCount++;
                currentTimestamp = currentTimestamp.Add(TimeSpan.FromSeconds(1));
                continue;
            }

            return frame;
        }

        return frame;
    }
}
