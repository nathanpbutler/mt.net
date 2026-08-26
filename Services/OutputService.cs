using nathanbutlerDEV.mt.net.Models;
using nathanbutlerDEV.mt.net.Utilities;
using System.Text;

namespace nathanbutlerDEV.mt.net.Services;

public class OutputService
{
    public static async Task<string> SaveContactSheetAsync(
        RgbaImage image,
        string videoPath,
        ThumbnailOptions options)
    {
        var outputPath = ApplyFormatExtension(BuildOutputPath(videoPath, options.Filename), options);

        // Check if file exists and handle skip/overwrite logic
        if (File.Exists(outputPath))
        {
            if (options.SkipExisting)
            {
                ConsoleOutput.Info($"Skipping existing file: {outputPath}");
                return outputPath;
            }

            if (!options.Overwrite)
            {
                outputPath = GetNextAvailablePath(outputPath);
            }
        }

        FileValidator.EnsureDirectoryExists(outputPath);

        try
        {
            await SaveAsync(image, outputPath, options);

            ConsoleOutput.Info($"Saved contact sheet: {outputPath}");

            // Apply input file's modified date to output file unless --no-mtime is specified
            if (!options.NoMtime)
            {
                var inputFileInfo = new FileInfo(videoPath);
                if (inputFileInfo.Exists)
                {
                    File.SetLastWriteTime(outputPath, inputFileInfo.LastWriteTime);
                }
            }

            return outputPath;
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to save contact sheet to {outputPath}: {ex.Message}", ex);
        }
    }

    public static async Task<List<string>> SaveIndividualImagesAsync(
        List<(RgbaImage Image, TimeSpan Timestamp)> frames,
        string videoPath,
        ThumbnailOptions options)
    {
        var savedPaths = new List<string>();
        var basePath = ApplyFormatExtension(BuildOutputPath(videoPath, options.Filename), options);
        var baseDir = Path.GetDirectoryName(basePath) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(basePath);
        var extension = Path.GetExtension(basePath);

        FileValidator.EnsureDirectoryExists(basePath);

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
                await SaveAsync(frame, individualPath, options);

                // Apply input file's modified date to output file unless --no-mtime is specified
                if (!options.NoMtime)
                {
                    var inputFileInfo = new FileInfo(videoPath);
                    if (inputFileInfo.Exists)
                    {
                        File.SetLastWriteTime(individualPath, inputFileInfo.LastWriteTime);
                    }
                }

                savedPaths.Add(individualPath);
            }
            catch (Exception ex)
            {
                ConsoleOutput.Error($"Failed to save individual image {individualPath}: {ex.Message}");
            }
        }

        ConsoleOutput.Info($"Saved {savedPaths.Count} individual images");
        return savedPaths;
    }

    /// <summary>
    /// Writes the WebVTT sidecar mapping time ranges to sprite regions of the contact sheet.
    /// </summary>
    /// <param name="frameCount">Number of thumbnails on the sheet.</param>
    /// <param name="imagePath">Path of the contact sheet the cues point at.</param>
    /// <param name="videoPath">Source video, used for the output path and mtime.</param>
    /// <param name="options">Thumbnail options.</param>
    /// <param name="layout">Geometry the sheet was actually composed with.</param>
    /// <param name="vttTimestamps">Cue boundaries: <c>[00:00:00, t1, ..., duration]</c>.</param>
    /// <remarks>
    /// The geometry now comes from <see cref="SheetLayout"/>. This method used to recompute
    /// header height and thumbnail size from the options with a formula that disagreed with the
    /// composer's — no DPI scaling on the line height — so every cue's y offset was wrong by the
    /// difference (~15px at default settings) and players showed the neighbouring thumbnail.
    /// </remarks>
    public static async Task<string> GenerateWebVttAsync(
        int frameCount,
        string imagePath,
        string videoPath,
        ThumbnailOptions options,
        SheetLayout layout,
        List<TimeSpan> vttTimestamps)
    {
        var vttPath = Path.ChangeExtension(BuildOutputPath(videoPath, options.Filename), ".vtt");

        var vtt = new StringBuilder();
        vtt.AppendLine("WEBVTT");
        vtt.AppendLine();

        var sheetName = Path.GetFileName(imagePath);

        for (int i = 0; i < frameCount; i++)
        {
            vtt.AppendLine($"{FormatVttTimestamp(vttTimestamps[i])} --> {FormatVttTimestamp(vttTimestamps[i + 1])}");
            vtt.AppendLine(
                $"{sheetName}#xywh={layout.ThumbnailX(i)},{layout.ThumbnailY(i)},{layout.ThumbnailWidth},{layout.ThumbnailHeight}");
            vtt.AppendLine();
        }

        try
        {
            await File.WriteAllTextAsync(vttPath, vtt.ToString());
            ConsoleOutput.Info($"Saved WebVTT file: {vttPath}");

            // Apply input file's modified date to output file unless --no-mtime is specified
            if (!options.NoMtime)
            {
                var inputFileInfo = new FileInfo(videoPath);
                if (inputFileInfo.Exists)
                {
                    File.SetLastWriteTime(vttPath, inputFileInfo.LastWriteTime);
                }
            }

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

    /// <summary>Formats a cue boundary as WebVTT's <c>HH:MM:SS.mmm</c>.</summary>
    /// <remarks>
    /// Uses <see cref="TimeSpan.TotalHours"/>, not <c>Hours</c>: the latter is the hour component
    /// within a day, so a 25-hour offset used to be written as <c>01:00:00</c> and every cue past
    /// the first day pointed at the wrong place.
    /// </remarks>
    private static string FormatVttTimestamp(TimeSpan timestamp)
    {
        return $"{(int)timestamp.TotalHours:D2}:{timestamp.Minutes:D2}:{timestamp.Seconds:D2}.{timestamp.Milliseconds:D3}";
    }

    /// <summary>Encodes <paramref name="image"/> to <paramref name="path"/> in the resolved format.</summary>
    private static Task SaveAsync(RgbaImage image, string path, ThumbnailOptions options)
    {
        return options.ResolveFormat(path) == OutputFormat.Png
            ? Task.Run(() => ImageEncoder.SavePng(image, path))
            : Task.Run(() => ImageEncoder.SaveJpeg(image, path, options.Quality));
    }

    /// <summary>
    /// Corrects the output extension when <c>--format</c> was given explicitly, so
    /// <c>--format png</c> does not write PNG bytes into a file named <c>.jpg</c>.
    /// </summary>
    private static string ApplyFormatExtension(string path, ThumbnailOptions options)
    {
        return options.Format switch
        {
            OutputFormat.Png => Path.ChangeExtension(path, ".png"),
            OutputFormat.Jpg => Path.ChangeExtension(path, ".jpg"),
            _ => path
        };
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
