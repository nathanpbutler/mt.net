using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;
using nathanbutlerDEV.mt.net.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace nathanbutlerDEV.mt.net.Services;

public class VideoProcessor
{
    public async Task<HeaderInfo> GetVideoMetadataAsync(string videoPath)
    {
        var mediaInfo = await FFProbe.AnalyseAsync(videoPath);

        var videoStream = mediaInfo.PrimaryVideoStream;
        var audioStream = mediaInfo.PrimaryAudioStream;

        return new HeaderInfo
        {
            Filename = Path.GetFileName(videoPath),
            FilePath = videoPath,
            FileSize = new FileInfo(videoPath).Length,
            Duration = mediaInfo.Duration,
            Width = videoStream?.Width ?? 0,
            Height = videoStream?.Height ?? 0,
            VideoCodec = videoStream?.CodecName ?? "unknown",
            AudioCodec = audioStream?.CodecName ?? "unknown",
            FrameRate = videoStream?.FrameRate ?? 0,
            BitRate = (long)mediaInfo.Format.BitRate,
            Format = mediaInfo.Format.FormatName ?? "unknown"
        };
    }

    public List<TimeSpan> CalculateTimestamps(
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

    public async Task<List<(Image<Rgba32> Image, TimeSpan Timestamp)>> ExtractFramesAsync(
        string videoPath,
        List<TimeSpan> timestamps,
        ThumbnailOptions options)
    {
        var frames = new List<(Image<Rgba32>, TimeSpan)>();

        foreach (var timestamp in timestamps)
        {
            var frame = await ExtractFrameAtTimestampAsync(videoPath, timestamp, options);
            if (frame != null)
            {
                frames.Add((frame, timestamp));
            }
        }

        return frames;
    }

    private async Task<Image<Rgba32>?> ExtractFrameAtTimestampAsync(
        string videoPath,
        TimeSpan timestamp,
        ThumbnailOptions options)
    {
        try
        {
            using var memoryStream = new MemoryStream();

            // Configure FFmpeg arguments for frame extraction
            var success = await FFMpegArguments
                .FromFileInput(videoPath, verifyExists: true, args =>
                {
                    // Use accurate seeking by default, fast seeking if requested
                    if (options.Fast)
                    {
                        args.Seek(timestamp); // Fast seek (less accurate)
                    }
                    else
                    {
                        args.Seek(timestamp); // Accurate seek
                    }
                })
                .OutputToPipe(new StreamPipeSink(memoryStream), options =>
                {
                    options
                        .WithVideoCodec(VideoCodec.Png)
                        .WithFrameOutputCount(1)
                        .ForceFormat("image2pipe");
                })
                .ProcessAsynchronously();

            if (!success || memoryStream.Length == 0)
            {
                return null;
            }

            memoryStream.Position = 0;
            return await Image.LoadAsync<Rgba32>(memoryStream);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error extracting frame at {timestamp}: {ex.Message}");
            return null;
        }
    }

    public async Task<Image<Rgba32>?> ExtractFrameWithRetriesAsync(
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
