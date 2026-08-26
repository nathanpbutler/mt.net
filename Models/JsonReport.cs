using System.Text.Json;
using System.Text.Json.Serialization;

namespace nathanbutlerDEV.mt.net.Models;

/// <summary>Machine-readable summary of a run, emitted to stdout under <c>--json</c>.</summary>
/// <param name="Results">One entry per input file, in the order they were processed.</param>
public sealed record JsonReport(
    [property: JsonPropertyName("results")] IReadOnlyList<JsonFileResult> Results);

/// <summary>The outcome for a single input file.</summary>
public sealed record JsonFileResult(
    [property: JsonPropertyName("input")] string Input,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("outputs")] IReadOnlyList<string> Outputs,
    [property: JsonPropertyName("vtt")] string? Vtt,
    [property: JsonPropertyName("frames")] int Frames,
    [property: JsonPropertyName("metadata")] JsonVideoMetadata? Metadata,
    [property: JsonPropertyName("layout")] JsonSheetLayout? Layout,
    [property: JsonPropertyName("error")] string? Error);

/// <summary>Source video properties, as read from the container.</summary>
public sealed record JsonVideoMetadata(
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("fileSizeBytes")] long FileSizeBytes,
    [property: JsonPropertyName("durationSeconds")] double DurationSeconds,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("videoCodec")] string VideoCodec,
    [property: JsonPropertyName("audioCodec")] string AudioCodec,
    [property: JsonPropertyName("frameRate")] double FrameRate,
    [property: JsonPropertyName("bitRate")] long BitRate,
    [property: JsonPropertyName("format")] string Format)
{
    public static JsonVideoMetadata From(HeaderInfo info) => new(
        info.Filename,
        info.FileSize,
        Math.Round(info.Duration.TotalSeconds, 3),
        info.Width,
        info.Height,
        info.VideoCodec,
        info.AudioCodec,
        Math.Round(info.FrameRate, 3),
        info.BitRate,
        info.Format);
}

/// <summary>The geometry the contact sheet was composed with.</summary>
public sealed record JsonSheetLayout(
    [property: JsonPropertyName("headerHeight")] int HeaderHeight,
    [property: JsonPropertyName("thumbnailWidth")] int ThumbnailWidth,
    [property: JsonPropertyName("thumbnailHeight")] int ThumbnailHeight,
    [property: JsonPropertyName("columns")] int Columns,
    [property: JsonPropertyName("rows")] int Rows,
    [property: JsonPropertyName("padding")] int Padding,
    [property: JsonPropertyName("sheetWidth")] int SheetWidth,
    [property: JsonPropertyName("sheetHeight")] int SheetHeight)
{
    public static JsonSheetLayout From(SheetLayout layout) => new(
        layout.HeaderHeight,
        layout.ThumbnailWidth,
        layout.ThumbnailHeight,
        layout.Columns,
        layout.Rows,
        layout.Padding,
        layout.ContentWidth,
        layout.TotalHeight);
}

/// <summary>
/// Source-generated serialisation context, so <c>--json</c> keeps working under the trimmed,
/// ReadyToRun single-file publish this project uses.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JsonReport))]
public partial class JsonReportContext : JsonSerializerContext;
