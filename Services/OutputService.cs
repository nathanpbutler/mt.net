using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using nathanbutlerDEV.mt.net.Models;
using System.Text;

namespace nathanbutlerDEV.mt.net.Services;

public class OutputService
{
    public static async Task<string> SaveContactSheetAsync(
        Image<Rgba32> image,
        string videoPath,
        ThumbnailOptions options)
    {
        var outputPath = BuildOutputPath(videoPath, options.Filename);

        // Check if file exists and handle skip/overwrite logic
        if (File.Exists(outputPath))
        {
            if (options.SkipExisting)
            {
                Console.WriteLine($"Skipping existing file: {outputPath}");
                return outputPath;
            }

            if (!options.Overwrite)
            {
                outputPath = GetNextAvailablePath(outputPath);
            }
        }

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // Save image based on extension
        var extension = Path.GetExtension(outputPath).ToLowerInvariant();

        try
        {
            if (extension == ".png")
            {
                await image.SaveAsPngAsync(outputPath, new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression });
            }
            else
            {
                await image.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = 90 });
            }

            Console.WriteLine($"Saved contact sheet: {outputPath}");
            return outputPath;
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to save contact sheet to {outputPath}: {ex.Message}", ex);
        }
    }

    public static async Task<List<string>> SaveIndividualImagesAsync(
        List<(Image<Rgba32> Image, TimeSpan Timestamp)> frames,
        string videoPath,
        ThumbnailOptions options)
    {
        var savedPaths = new List<string>();
        var basePath = BuildOutputPath(videoPath, options.Filename);
        var baseDir = Path.GetDirectoryName(basePath) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(basePath);
        var extension = Path.GetExtension(basePath);

        // Ensure output directory exists
        if (!Directory.Exists(baseDir))
        {
            Directory.CreateDirectory(baseDir);
        }

        for (int i = 0; i < frames.Count; i++)
        {
            var (frame, _) = frames[i];
            var individualPath = Path.Combine(baseDir, $"{baseName}_{i + 1:D3}{extension}");

            // Handle file existence with skip/overwrite logic
            if (File.Exists(individualPath))
            {
                if (options.SkipExisting)
                {
                    savedPaths.Add(individualPath);
                    continue;
                }

                if (!options.Overwrite)
                {
                    individualPath = GetNextAvailablePath(individualPath);
                }
            }

            try
            {
                if (extension.Equals(".png", StringComparison.InvariantCultureIgnoreCase))
                {
                    await frame.SaveAsPngAsync(individualPath);
                }
                else
                {
                    await frame.SaveAsJpegAsync(individualPath, new JpegEncoder { Quality = 90 });
                }

                savedPaths.Add(individualPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to save individual image {individualPath}: {ex.Message}");
            }
        }

        Console.WriteLine($"Saved {savedPaths.Count} individual images");
        return savedPaths;
    }

    public static async Task<string> GenerateWebVttAsync(
        List<(Image<Rgba32> Image, TimeSpan Timestamp)> frames,
        string imagePath,
        string videoPath,
        ThumbnailOptions options,
        List<TimeSpan> vttTimestamps)
    {
        var vttPath = Path.ChangeExtension(BuildOutputPath(videoPath, options.Filename), ".vtt");

        var vtt = new StringBuilder();
        vtt.AppendLine("WEBVTT");
        vtt.AppendLine();

        var thumbnailWidth = options.Width;
        var thumbnailHeight = options.Height > 0
            ? options.Height
            : (int)(frames[0].Image.Height * (thumbnailWidth / (double)frames[0].Image.Width));

        var columns = options.Columns;

        // Calculate header height for Y-offset (matching Go implementation at mt.go:396)
        var headerHeight = 0;
        if (options.Header && !options.WebVtt)
        {
            // Header height calculation matches ImageComposer logic
            var numLines = 4; // Base: File Name, Size, Duration, Resolution
            if (options.HeaderMeta) numLines += 2; // +Codec/FPS lines
            if (!string.IsNullOrEmpty(options.Comment)) numLines += 1;
            headerHeight = (5 + (options.FontSize + 4) * numLines) + 10;
        }

        // Use calculated timestamps array (matching Go implementation at mt.go:396)
        // vttTimestamps = [00:00:00, timestamp1, timestamp2, ..., videoDuration]
        for (int i = 0; i < frames.Count; i++)
        {
            var row = i / columns;
            var col = i % columns;

            // Calculate positions WITH padding (matching Go implementation at mt.go:380-388)
            var padding = options.Padding;
            var x = (col * thumbnailWidth) + (padding * col) + padding;
            var y = (row * thumbnailHeight) + (padding * row) + padding + headerHeight;

            // Use calculated timestamps: timestamps[i] --> timestamps[i+1]
            vtt.AppendLine($"{FormatVttTimestamp(vttTimestamps[i])} --> {FormatVttTimestamp(vttTimestamps[i + 1])}");
            vtt.AppendLine($"{Path.GetFileName(imagePath)}#xywh={x},{y},{thumbnailWidth},{thumbnailHeight}");
            vtt.AppendLine();
        }

        try
        {
            await File.WriteAllTextAsync(vttPath, vtt.ToString());
            Console.WriteLine($"Saved WebVTT file: {vttPath}");
            return vttPath;
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to save WebVTT file to {vttPath}: {ex.Message}", ex);
        }
    }

    private static string BuildOutputPath(string videoPath, string pattern)
    {
        var videoDir = Path.GetDirectoryName(videoPath) ?? "";
        var videoName = Path.GetFileNameWithoutExtension(videoPath);

        // Simple pattern replacement
        // Note: If pattern contains {{.Path}}, ensure proper path separator after directory
        var output = pattern
            .Replace("{{.Path}}", string.IsNullOrEmpty(videoDir) ? "" : videoDir + Path.DirectorySeparatorChar)
            .Replace("{{.Name}}", videoName);

        // If pattern doesn't contain path info, use video directory
        if (!Path.IsPathRooted(output) && !output.Contains(Path.DirectorySeparatorChar))
        {
            output = Path.Combine(videoDir, output);
        }

        return output;
    }

    private static string FormatVttTimestamp(TimeSpan timestamp)
    {
        return $"{timestamp.Hours:D2}:{timestamp.Minutes:D2}:{timestamp.Seconds:D2}.{timestamp.Milliseconds:D3}";
    }

    /// <summary>
    /// Finds the next available filename by incrementing a counter suffix (-01, -02, etc.)
    /// Matches the behavior of the original mt Go implementation.
    /// </summary>
    private static string GetNextAvailablePath(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath) ?? "";
        var filename = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);

        var counter = 1;
        string newPath;

        do
        {
            var newFilename = $"{filename}-{counter:D2}{extension}";
            newPath = Path.Combine(directory, newFilename);
            counter++;
        } while (File.Exists(newPath));

        return newPath;
    }
}
