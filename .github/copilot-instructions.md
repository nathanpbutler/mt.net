# Copilot Instructions for mt.net

## Project Overview

**mt.net** is a .NET port of a Go-based media thumbnailing tool that generates contact sheets from video files using FFmpeg. The tool extracts frames at calculated timestamps, applies image processing filters, and creates customizable grid layouts with metadata headers.

## Architecture & Core Components

### Service-Oriented Architecture

The application uses a stateless service pattern with clear separation of concerns:

- **VideoProcessor** (`Services/VideoProcessor.cs`) - FFmpeg integration for metadata extraction and frame capture
- **FFmpegFilterGraphComposer** (`Services/FFmpegFilterGraphComposer.cs`) - FFmpeg.AutoGen-based contact sheet creation with filter graphs
- **FFmpegFilterService** (`Services/FFmpegFilterService.cs`) - Image filters via FFmpeg filter graphs
- **ImageEncoder** (`Services/ImageEncoder.cs`) - PNG/JPEG encoding via FFmpeg
- **ImageLoader** (`Services/ImageLoader.cs`) - Still image decoding via FFmpeg
- **WatermarkService** (`Services/WatermarkService.cs`) - Watermark blending
- **ContentDetectionService** (`Services/ContentDetectionService.cs`) - Frame quality analysis (blank/blur/NSFW detection)
- **OutputService** (`Services/OutputService.cs`) - File I/O, WebVTT generation, and filename pattern substitution

### Processing Pipeline Flow

```csharp
// Main pipeline in Commands/RootCommand.cs ProcessVideoAsync()
Video Input → Extract Metadata → Calculate Timestamps → Extract Frames 
→ Apply Content Detection → Apply Filters → Create Contact Sheet 
→ Apply Watermarks → Save Output → Generate WebVTT
```

### Configuration System

- **ThumbnailOptions** (`Models/ThumbnailOptions.cs`) - Single comprehensive options class with 40+ properties
- **System.CommandLine** - Direct CLI option mapping to ThumbnailOptions properties
- **Pattern**: Each CLI option maps to a ThumbnailOptions property with default values and aliases

## Key Development Patterns

### Temp Directory Workflow

The `temp/` directory serves as temporary storage for current development work:

- **Check temp/ first** - Before sourcing external documentation or code, check if it's already available in temp/
- **Ask before sourcing** - If needed code/docs aren't in temp/, ask the user to source them before proceeding
- **Current focus**: FFmpeg.AutoGen migration work is in `temp/FFmpeg.AutoGen/`

### Service Instantiation Pattern

Services are instantiated per-operation (not injected) in the main processing method:

```csharp
var videoProcessor = new VideoProcessor();
var contentDetection = new ContentDetectionService();
var outputService = new OutputService();
// Use directly without DI container
```

### Async Resource Management

Critical pattern for image processing - always dispose images to prevent memory leaks:

```csharp
foreach (var (frame, _) in frames)
{
    frame.Dispose(); // Returns the pooled RgbaImage buffer
}
```

### CLI Option Declaration Pattern

Options follow a consistent pattern in `Commands/RootCommand.cs`:

```csharp
var numCapsOption = new Option<int>("--numcaps")
{
    Description = "Number of captures to make",
    DefaultValueFactory = _ => 4
};
numCapsOption.Aliases.Add("-n");
```

### Filename Pattern Substitution

Output paths use Go-template style patterns (`{{.Path}}{{.Name}}.jpg`) processed in `OutputService.BuildOutputPath()`.

### File Handling Behavior

When output files already exist, the tool follows this logic (matching the original Go implementation):

1. **`--skip-existing`**: Skip processing entirely if file exists
2. **`--overwrite`**: Replace existing file
3. **Default (neither flag set)**: Automatically increment filename with `-01`, `-02`, etc. suffix

Example: `output.jpg` → `output-01.jpg` → `output-02.jpg`

This behavior is implemented in `OutputService.GetNextAvailablePath()` and applies to both contact sheets and individual thumbnail images.

### Modified Time (mtime) Behavior

By default, mt.net applies the input video file's modified date to all output files (contact sheets, individual images, and WebVTT files). This behavior can be disabled using the `--no-mtime` option.

- **Default**: Output files inherit the input file's modified date
- **`--no-mtime`**: Output files use the current timestamp

This is implemented in `OutputService` methods: `SaveContactSheetAsync()`, `SaveIndividualImagesAsync()`, and `GenerateWebVttAsync()`.

### WebVTT Implementation Pattern

WebVTT generation achieves full feature parity with the Go implementation through a dual timestamp approach:

**Frame Extraction Timestamps** (VideoProcessor.cs:176-178):

```csharp
// Use (numCaps + 1) to ensure frames are extractable (not at exact video end)
var step = workingDuration.TotalSeconds / (numCaps + 1);
```

- Spacing: `workingDuration / (numCaps + 1)` ensures last frame is before video end
- Purpose: FFmpeg cannot extract frames at exact video end
- Example (40:20 video, 4 caps): Extracts at 8:04, 16:08, 24:12, 32:16

**VTT Display Timestamps** (RootCommand.cs:658-666):

```csharp
// Build VTT timestamps with evenly-spaced intervals spanning full video
var vttTimestamps = new List<TimeSpan> { TimeSpan.Zero };
var vttStep = headerInfo.Duration.TotalSeconds / frames.Count;
for (int i = 1; i <= frames.Count; i++)
{
    vttTimestamps.Add(TimeSpan.FromSeconds(vttStep * i));
}
```

- Spacing: `videoDuration / frames.Count` for even coverage
- Purpose: Display time ranges for seeking in HTML5 video players
- Example (40:20 video, 4 caps): 00:00:00, 10:05, 20:10, 30:15, 40:20

**--webvtt Option Override** (RootCommand.cs:487-495):

```csharp
if (options.WebVtt)
{
    options.Vtt = true;                    // Enable VTT generation
    options.Header = false;                 // Disable header
    options.HeaderMeta = false;            // Disable header meta
    options.DisableTimestamps = true;      // Disable timestamps
    options.Padding = 0;                   // No padding
}
```

**Key Insight**: Frame extraction and VTT timestamps serve different purposes and must be calculated differently for correct behavior.

## Critical Dependencies & Integration Points

### FFmpeg Integration (✅ Migration Complete)

- **Current**: Uses `FFmpeg.AutoGen` for direct libavcodec control and video processing
- **Previous**: Migrated from `FFMpegCore` due to performance limitations
- **Benefits**: Direct P/Invoke bindings providing full control over frame-level seeking
- **Performance**: 4x improvement over FFMpegCore, now within ~40-50% of Go implementation speed

### Image Composition Pipeline

Fully FFmpeg-based as of v2.4.0. There is no second composer.

**Per-Frame Processing (FFmpeg.AutoGen):**

- Decode frames directly to `AV_PIX_FMT_RGBA`, wrapped as `RgbaImage`
- Convert `RgbaImage` → AVFrame via `Utilities/AVFrameBridge.cs`
- Process with FFmpeg filter graphs:
  - `scale` filter (thumbnail resizing)
  - `drawtext` filter (timestamps with freetype - pixel-perfect)
  - `drawtext` filter (header text with freetype - pixel-perfect)
  - `drawbox` filter (borders), `v360` (VR conversion)
- Apply filters via `FFmpegFilterService` (native FFmpeg filters)
- Convert AVFrame → `RgbaImage`

**Final Composition (RgbaImage):**

- Create canvas with background colour (`Fill`)
- Arrange processed frames in grid layout (`DrawImage`)
- Position header
- Apply watermarks via `WatermarkService`

**Encoding**: `Services/ImageEncoder.cs` - PNG (`AV_CODEC_ID_PNG`, rgba) or JPEG
(`AV_CODEC_ID_MJPEG`, yuvj420p) via FFmpeg's own encoders.

**Requirement**: `drawtext` needs an FFmpeg build compiled with libfreetype.

### Content Detection Algorithms

Frame quality analysis uses specific thresholds:

- **Blank detection**: Histogram analysis with configurable threshold (default: 85)
- **Blur detection**: Laplacian variance (default: 62)
- **Retry logic**: Up to 3 attempts to find suitable frames

## Build & Development Commands

```bash
# Build and run (standard .NET commands)
dotnet build
dotnet run -- video.mp4 --numcaps 9 --columns 3

# Build optimized single-file executable
dotnet publish -c Release -r osx-arm64 --self-contained   # or win-x64 / linux-x64

# Test with filters and content detection
dotnet run -- video.mp4 --filter greyscale,sepia --skip-blank --header-meta
```

## Testing Strategy & Edge Cases

### Required Dependencies

- **FFmpeg must be installed** and accessible in PATH
- **Font files** for timestamp rendering (DroidSans.ttf referenced)

### Critical Test Scenarios

1. **Various video formats** (MP4, AVI, MKV) - FFmpeg compatibility
2. **Edge timing** - Very short videos, frame extraction at boundaries
3. **Content detection** - Blank frames, blurry content, retry logic
4. **Memory management** - Large videos, multiple frame processing

### Performance Benchmarks

Performance comparison (44 thumbnails / 4 columns, 1080p video):

| Version | Mode | Time | Speed vs Go |
|---------|------|------|-------------|
| Go (original) | Normal | 10.44s | Baseline |
| Go (original) | Fast | 7.52s | 28% faster |
| mt.net v1 (FFMpegCore) | Normal | 58.53s | 5.6x slower ❌ |
| mt.net v1 (FFMpegCore) | Fast | 58.99s | 7.8x slower ❌ |
| **mt.net v2 (FFmpeg.AutoGen)** | **Normal** | **14.53s** | **1.4x slower ✅** |
| **mt.net v2 (FFmpeg.AutoGen)** | **Fast** | **11.07s** | **1.5x slower ✅** |

### Known Performance Considerations

- **Fast seeking** (`--fast` option) now uses FFmpeg.AutoGen - performance within 1.5x of Go
- **Large contact sheets** with many thumbnails can consume significant memory
- **Filter chaining** applies sequentially - order matters for some filters

### Known Limitations

- **Font Rendering**: text is drawn with FFmpeg `drawtext`, which needs a build compiled with libfreetype. Homebrew stock `ffmpeg` is not; use `brew install ffmpeg-full`. `FFmpegHelper.WarnIfDrawTextMissing()` warns instead of silently omitting text.

## Project-Specific Conventions

### Error Handling Pattern

```csharp
try
{
    await ProcessVideoAsync(file.FullName, options);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error processing video: {ex.Message}");
    if (options.Verbose)
    {
        Console.Error.WriteLine(ex.StackTrace);
    }
    return 1;
}
```

### Filter Implementation Pattern

All filters in `FilterService` implement consistent interfaces and support chaining via comma-separated strings.

### v360 Filter for VR Videos

The `--v360` option applies FFmpeg's v360 filter during the frame processing stage (not during extraction):

- **Location**: Applied in `FFmpegFilterGraphComposer.BuildFrameFilterSpec()` (line 191-196)
- **Filter chain**: `v360=input=hequirect:output=flat:in_stereo=sbs:out_stereo=2d:d_fov=125:w=400:h=300:pitch=-25` → `format=pix_fmts=rgba`
- **Purpose**: Converts 360° equirectangular VR video to flat 2D projection
- **Input format**: Side-by-side stereo (SBS)
- **Output**: Flat 2D at 400x300 resolution
- **Pixel format conversion**: Critical to convert from YUV (v360 output) to RGBA (expected by AVFrameToImage)
- **Integration**: Replaces the normal `scale` filter in the processing pipeline

### Color Parsing Convention

Colors are specified as "R,G,B" strings and parsed by `Utilities/ColorParser.cs`.

## External Resource Guidelines

### When to Ask Before Sourcing

- **Documentation/code not in temp/**: Ask user to source it first before looking externally
- **FFmpeg.AutoGen examples**: Check `temp/FFmpeg.AutoGen/` before searching online
- **API references**: Use temp/ directory content as primary source for current development

### When to Proceed Independently

- **Standard .NET patterns**: Use established .NET conventions for common tasks
- **Pixel operations**: See `Models/RgbaImage.cs` and `Utilities/AVFrameBridge.cs`
- **General C# best practices**: No need to ask for standard language features

## Reference Implementation

The `reference/original-mt/` directory contains the complete Go implementation as a git submodule. When making changes that affect output compatibility, reference the Go implementation's behavior for consistency.

## Migration Status

**✅ FFmpeg.AutoGen for Image Composition** - COMPLETED (v2.4.0 - ImageSharp fully removed)

- **Status**: Complete. ImageSharp, ImageSharp.Drawing and SixLabors.Fonts removed; FFmpeg is the
  only imaging dependency. See `MIGRATION_FFMPEG_AUTOGEN.md`.
  - **FFmpeg**: Frame resizing, text rendering (freetype), borders, filters, PNG/JPEG encoding
  - **Managed**: `RgbaImage` pooled raster for grid layout, canvas creation and watermarks
- **Benefits**:
  - Pixel-perfect text rendering matching mt (uses freetype)
  - Three fewer NuGet dependencies
  - Fixed a per-frame native memory leak in the old AVFrame conversions
- **Timeline**: v2.0 introduced the hybrid composer; v2.4.0 removed ImageSharp and `--composer`
