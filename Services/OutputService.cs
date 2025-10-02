using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using nathanbutlerDEV.mt.net.Models;
using System.Text;

namespace nathanbutlerDEV.mt.net.Services;

public class OutputService
{
    public async Task<string> SaveContactSheetAsync(
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
                Console.WriteLine($"File exists and overwrite not enabled: {outputPath}");
                return outputPath;
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

    public async Task<List<string>> SaveIndividualImagesAsync(
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
            var (frame, timestamp) = frames[i];
            var individualPath = Path.Combine(baseDir, $"{baseName}_{i + 1:D3}{extension}");

            try
            {
                if (extension.ToLowerInvariant() == ".png")
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

    public async Task<string> GenerateWebVttAsync(
        List<(Image<Rgba32> Image, TimeSpan Timestamp)> frames,
        string imagePath,
        string videoPath,
        ThumbnailOptions options)
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

        for (int i = 0; i < frames.Count; i++)
        {
            var timestamp = frames[i].Timestamp;
            var nextTimestamp = i < frames.Count - 1
                ? frames[i + 1].Timestamp
                : timestamp + TimeSpan.FromSeconds(5);

            var row = i / columns;
            var col = i % columns;

            var x = col * thumbnailWidth;
            var y = row * thumbnailHeight;

            vtt.AppendLine($"{FormatVttTimestamp(timestamp)} --> {FormatVttTimestamp(nextTimestamp)}");
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

    private string BuildOutputPath(string videoPath, string pattern)
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

    private string FormatVttTimestamp(TimeSpan timestamp)
    {
        return $"{timestamp.Hours:D2}:{timestamp.Minutes:D2}:{timestamp.Seconds:D2}.{timestamp.Milliseconds:D3}";
    }
}
