using nathanbutlerDEV.mt.net.Utilities;

namespace nathanbutlerDEV.mt.net.Models;

/// <summary>Output image format for the contact sheet and individual thumbnails.</summary>
public enum OutputFormat
{
    /// <summary>Derive the format from the output filename's extension.</summary>
    Auto,

    /// <summary>JPEG, honouring <see cref="ThumbnailOptions.Quality"/>.</summary>
    Jpg,

    /// <summary>PNG, lossless; <see cref="ThumbnailOptions.Quality"/> does not apply.</summary>
    Png
}

public class ThumbnailOptions
{
    // Basic
    public int NumCaps { get; set; } = 4;
    public int Columns { get; set; } = 2;
    public int Padding { get; set; } = 10;
    public int Width { get; set; } = 400;
    public int Height { get; set; } = 0;

    // Time
    public int Interval { get; set; } = 0;
    public string From { get; set; } = "00:00:00";
    public string End { get; set; } = "00:00:00";
    public bool SkipCredits { get; set; } = false;

    // Visual
    public string Filter { get; set; } = "none";
    public string FontPath { get; set; } = "DroidSans";
    public int FontSize { get; set; } = 12;
    public bool DisableTimestamps { get; set; } = false;
    public double TimestampOpacity { get; set; } = 1.0;
    public bool Header { get; set; } = true;
    public string HeaderImage { get; set; } = "";
    public bool HeaderMeta { get; set; } = false;
    public string BgContent { get; set; } = "0,0,0";
    public string BgHeader { get; set; } = "0,0,0";
    public string FgHeader { get; set; } = "255,255,255";
    public int Border { get; set; } = 0;
    public string Watermark { get; set; } = "";
    public string WatermarkAll { get; set; } = "";
    public string Comment { get; set; } = "contactsheet created with mt.net (https://github.com/nathanpbutler/mt.net)";

    // v360 (VR) conversion
    public bool V360 { get; set; } = false;
    public string V360Input { get; set; } = "hequirect";
    public string V360Output { get; set; } = "flat";
    public string V360Stereo { get; set; } = "sbs";
    public int V360Fov { get; set; } = 125;
    public int V360Pitch { get; set; } = -25;

    // Processing
    public bool SkipBlank { get; set; } = false;
    public bool SkipBlurry { get; set; } = false;
    public bool Sfw { get; set; } = false;
    public bool Fast { get; set; } = false;
    public int BlurThreshold { get; set; } = 62;
    public int BlankThreshold { get; set; } = 85;

    // Output
    public string Filename { get; set; } = "{{.Path}}{{.Name}}.jpg";
    public bool SingleImages { get; set; } = false;
    public bool SkipExisting { get; set; } = false;
    public bool Overwrite { get; set; } = false;
    public bool Vtt { get; set; } = false;
    public bool WebVtt { get; set; } = false;
    public bool NoMtime { get; set; } = false;
    public OutputFormat Format { get; set; } = OutputFormat.Auto;
    public int Quality { get; set; } = 90;

    // Input
    public bool Recursive { get; set; } = false;

    // Global
    public bool Verbose { get; set; } = false;
    public bool Quiet { get; set; } = false;
    public bool Json { get; set; } = false;

    /// <summary>
    /// Resolves the encoder to use for <paramref name="outputPath"/>: an explicit
    /// <c>--format</c> wins, otherwise the extension decides, defaulting to JPEG.
    /// </summary>
    public OutputFormat ResolveFormat(string outputPath)
    {
        if (Format != OutputFormat.Auto)
        {
            return Format;
        }

        return Path.GetExtension(outputPath).Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? OutputFormat.Png
            : OutputFormat.Jpg;
    }

    /// <summary>
    /// Validates option combinations that parse cleanly but cannot be honoured, returning a
    /// message for the first problem found or null when the options are coherent.
    /// </summary>
    /// <remarks>
    /// These used to fail silently or throw deep in the pipeline. Checking up front means the
    /// user hears about it before any frames are decoded.
    /// </remarks>
    public string? Validate()
    {
        if (Quiet && Verbose)
        {
            return "--quiet and --verbose are mutually exclusive.";
        }

        if (SingleImages && (Vtt || WebVtt))
        {
            return "--vtt describes sprite regions within a single contact sheet, so it cannot be combined with --single-images.";
        }

        if (Quality is < 1 or > 100)
        {
            return $"--quality must be between 1 and 100 (got {Quality}).";
        }

        if (TimestampOpacity is < 0.0 or > 1.0)
        {
            return $"--timestamp-opacity must be between 0.0 and 1.0 (got {TimestampOpacity}).";
        }

        if (NumCaps < 1)
        {
            return $"--numcaps must be at least 1 (got {NumCaps}).";
        }

        if (Columns < 1)
        {
            return $"--columns must be at least 1 (got {Columns}).";
        }

        if (Width < 1)
        {
            return $"--width must be at least 1 (got {Width}).";
        }

        if (Padding < 0)
        {
            return $"--padding cannot be negative (got {Padding}).";
        }

        if (BlurThreshold is < 0 or > 100)
        {
            return $"--blur-threshold must be between 0 and 100 (got {BlurThreshold}).";
        }

        if (BlankThreshold is < 0 or > 100)
        {
            return $"--blank-threshold must be between 0 and 100 (got {BlankThreshold}).";
        }

        foreach (var (label, value) in new[] { ("--from", From), ("--to", End) })
        {
            try
            {
                TimeSpanParser.ParseTimeString(value);
            }
            catch (ArgumentException ex)
            {
                return $"{label}: {ex.Message}";
            }
        }

        foreach (var (label, path) in new[] { ("--watermark", Watermark), ("--watermark-all", WatermarkAll), ("--header-image", HeaderImage) })
        {
            if (!string.IsNullOrEmpty(path) && !File.Exists(path))
            {
                return $"{label}: file not found: {path}";
            }
        }

        return null;
    }
}
