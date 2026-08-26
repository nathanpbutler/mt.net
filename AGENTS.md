# AGENTS.md

This file provides guidance to AI coding agents when working with code in this repository.

## Project Overview

**mt.net** is a .NET port of a Go-based media thumbnailing tool that generates contact sheets from video files using FFmpeg. The tool extracts frames at calculated timestamps, applies image processing filters, and creates customizable grid layouts with metadata headers.

### Core Features

- Configurable screenshot count, layout, and styling
- Header with file metadata
- Timestamps on thumbnails
- Various filters and image processing options (greyscale, sepia, strip effects, etc.)
- Batch input: multiple files, directories, and globs
- WebVTT generation for HTML5 video players
- JSON reporting for scripting

### Reference Implementation

The `reference/original-mt/` directory contains the complete Go implementation as a git submodule. When making changes that affect output compatibility, reference the Go implementation's behavior for consistency.

## Architecture & Core Components

### Service-Oriented Architecture

The application uses a stateless service pattern with clear separation of concerns:

- **VideoProcessor** ([Services/VideoProcessor.cs](Services/VideoProcessor.cs)) - FFmpeg integration for metadata extraction and frame capture
- **FFmpegFilterGraphComposer** ([Services/FFmpegFilterGraphComposer.cs](Services/FFmpegFilterGraphComposer.cs)) - FFmpeg.AutoGen-based contact sheet creation with filter graphs
- **FFmpegFilterService** ([Services/FFmpegFilterService.cs](Services/FFmpegFilterService.cs)) - Image filters via FFmpeg filter graphs
- **ImageEncoder** ([Services/ImageEncoder.cs](Services/ImageEncoder.cs)) - PNG/JPEG encoding via FFmpeg
- **ImageLoader** ([Services/ImageLoader.cs](Services/ImageLoader.cs)) - Still image decoding via FFmpeg
- **WatermarkService** ([Services/WatermarkService.cs](Services/WatermarkService.cs)) - Watermark blending
- **v360 Filter** - 360-degree VR video conversion (applied in FFmpegFilterGraphComposer)
- **ContentDetectionService** ([Services/ContentDetectionService.cs](Services/ContentDetectionService.cs)) - Frame quality analysis (blank/blur/NSFW detection)
- **OutputService** ([Services/OutputService.cs](Services/OutputService.cs)) - File I/O, WebVTT generation, and filename pattern substitution

### Processing Pipeline Flow

```csharp
// Commands/RootCommand.cs: ProcessAllAsync() loops inputs, ProcessVideoAsync() does one file
Resolve Inputs (files/dirs/globs) → for each file:
  Extract Metadata → Calculate Timestamps → Extract Frames (one decoder, reused)
  → Apply Content Detection
  → ProcessFrames (v360/scale → image filters → timestamp → border → watermarks)
  → Compose Contact Sheet (returns SheetLayout)  ─or─  Save Individual Images
  → Save Output → Generate WebVTT (from SheetLayout)
```

### Options

- **ThumbnailOptions** ([Models/ThumbnailOptions.cs](Models/ThumbnailOptions.cs)) - Single comprehensive options class, plus `Validate()` for combinations that parse but cannot be honoured
- **System.CommandLine** - Direct CLI option mapping to ThumbnailOptions properties
- **Pattern**: Each CLI option maps to a ThumbnailOptions property with default values and aliases

There is no config-file or environment-variable layer. v3 removed one that had been built and
then discarded on every run; do not reintroduce it without asking.

## Key Development Patterns

### Reference Material

`reference/original-mt/` is a git submodule holding the Go implementation; populate it with
`git submodule update --init` when output compatibility is in question. The `temp/` directory, if
present, is scratch space excluded from the build.

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

Options follow a consistent pattern in [Commands/RootCommand.cs](Commands/RootCommand.cs):

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

## Development Commands

```bash
# Build and run (standard .NET commands)
dotnet build
dotnet run

# Build for release
dotnet build --configuration Release

# Build optimized single-file executable
dotnet publish -c Release -r osx-arm64 --self-contained   # or win-x64 / linux-x64

# Run with arguments (examples)
dotnet run -- video.mp4 --numcaps 9 --columns 3 --width 300
dotnet run -- video.mp4 --filter greyscale,sepia --skip-blank --header-meta
dotnet run -- --filters  # Show available filters
dotnet run -- --help     # Show all options
```

## Current Project Status (✅ = Complete, ⚠️ = Partial/Needs Work, 🚧 = In Progress, ❌ = Not Started)

### ✅ Foundation Complete

- **Project Structure**: Organized folders for Commands, Configuration, Models, Services, Utilities
- **Dependencies**: All required NuGet packages added
  - System.CommandLine (CLI parsing)
  - ✅ **FFmpeg.AutoGen** (direct FFmpeg bindings for video processing) - Migration complete!
  - That is the whole dependency list. v3 removed the five
    Microsoft.Extensions.Configuration.* packages and both Serilog packages along with the
    config layer and the discarded logger they served.

### ✅ Command-Line Interface

- **Every option is wired**: v3 audited the surface and either implemented or removed each one.
  Do not add an option without a code path that reads it.
- **Batch Interface**: `mt video.mp4 [options]`, or several files, a directory, or a glob
- **Help System**: Built-in help with descriptions, defaults, and examples
- **Validation**: `ThumbnailOptions.Validate()` rejects incoherent combinations before decoding

### ✅ Models and Utilities

- **Models**: ThumbnailOptions, SheetLayout, JsonReport, HeaderInfo, RgbaImage
- **Utilities**: Colour parsing, time parsing, input resolution, output verbosity routing

### ✅ Core Services Implementation Complete

#### ✅ Video Processing (VideoProcessor.cs)

[Services/VideoProcessor.cs](Services/VideoProcessor.cs) - Fully implemented video processing logic:

- ✅ GetVideoMetadataAsync() - Extract metadata using FFmpeg.AutoGen
- ✅ CalculateTimestamps() - Calculate timestamps based on numCaps, interval, from, to, skipCredits
- ✅ SelectFrameAsync() - Chooses the frame for one grid position: optional scene search, then
  `--retries` attempts spaced `--retry-step` apart, falling back to the best candidate seen.
  Takes an already-open decoder; the caller opens one per file. v2 constructed a new decoder per
  frame, and its unused batch method has been removed.
- ✅ **Fast seeking support** - Fully implemented using FFmpeg.AutoGen with direct libavcodec control
  - Migrated from FFMpegCore to FFmpeg.AutoGen for frame-level seeking behavior
  - `--fast` option now provides true keyframe-based seeking matching original Go implementation
  - **Performance**: Achieved 4x improvement over FFMpegCore, now within ~40-50% of Go speed

#### ✅ Image Composition

**Default: FFmpegFilterGraphComposer.cs** ([Services/FFmpegFilterGraphComposer.cs](Services/FFmpegFilterGraphComposer.cs)) - FFmpeg.AutoGen-based composition:

- ✅ CreateContactSheet() - Create grid layout using FFmpeg filter graphs
- ✅ Text rendering using FFmpeg's `drawtext` filter with freetype (pixel-perfect match to mt)
- ✅ Frame processing with `scale`, `drawbox` filters
- ✅ All image filters implemented via FFmpegFilterService
- ✅ Timestamps and headers with exact visual parity to original Go mt

Supporting services:

- ✅ CalculateHeaderHeight() - Header height from font size and line count
- ✅ BuildHeaderTextLines() - Header text matching mt's format (File Name:, File Size:, Duration:, Resolution:)
- ✅ WatermarkService.ApplyWatermark() - Watermark blending
- ✅ FormatFileSize() - Binary units (GiB, MiB) matching mt's output
- ✅ Customizable colors, fonts, and styling

**Note**: text rendering requires an FFmpeg build compiled with libfreetype. See `FFmpegHelper.HasDrawText`.

#### ✅ Image Filtering (FilterService.cs)

[Services/FFmpegFilterService.cs](Services/FFmpegFilterService.cs) - All filter implementations:

- ✅ ApplyFilters() - Filter chaining support
- ✅ Greyscale, Sepia, Invert filters
- ✅ Fancy filter (random rotation)
- ✅ Cross-processing effect
- ✅ Strip filter (film strip with sprocket holes)

#### ✅ Content Detection (ContentDetectionService.cs)

[Services/ContentDetectionService.cs](Services/ContentDetectionService.cs) - Frame quality analysis:

- ✅ Analyse() - One pass over a downscaled copy producing every metric the options need
  (`AnalysisNeeds` controls which are computed)
- ✅ AcceptanceRatio() - Comparable headroom across all checks; >= 1.0 accepts, and the value
  ranks rejects so exhaustion can keep the least-bad candidate
- ✅ BlankCutoff() / BlurCutoff() - Threshold-to-cutoff mappings, linear and logarithmic
- ✅ FingerprintDistance() - Average-hash comparison behind `--dedupe` and `--scene-detect`

#### ✅ Output Management (OutputService.cs)

[Services/OutputService.cs](Services/OutputService.cs) - File handling and export:

- ✅ SaveContactSheetAsync() - Save contact sheets in JPEG/PNG formats
  - Applies input file's modified date to output by default (unless `--no-mtime` is specified)
- ✅ SaveIndividualImagesAsync() - Save individual thumbnail images
  - Applies input file's modified date to each output image by default (unless `--no-mtime` is specified)
- ✅ GenerateWebVttAsync() - Generate WebVTT files with cue points
  - Takes pre-calculated VTT timestamps array (evenly-spaced intervals from 00:00:00 to video duration)
  - Takes the `SheetLayout` the sheet was composed with; it no longer computes geometry itself
    (v2's separate formula desynced every cue by ~25px — see SheetLayout's remarks)
  - Timestamps use `TotalHours`, so videos past 24h do not wrap
  - Generates xywh coordinates for HTML5 video player sprite sheet navigation
  - Each cue maps timestamp range to thumbnail region in contact sheet
  - Applies input file's modified date to VTT file by default (unless `--no-mtime` is specified)
- ✅ BuildOutputPath() - Filename pattern substitution ({{.Path}}, {{.Name}})
- ✅ GetNextAvailablePath() - Automatic filename incrementing with -01, -02, etc. suffix (matches Go implementation)
- ✅ File handling logic - Overwrite/skip-existing/auto-increment behavior matching original mt
- ✅ Modified time preservation - Applies input file's mtime to all outputs by default (disable with `--no-mtime`)

### ✅ Integration Complete

#### ✅ Main Processing Pipeline

[Commands/RootCommand.cs](Commands/RootCommand.cs) - Fully integrated async processing pipeline:

1. ✅ Validate input file
2. ✅ Extract video metadata using VideoProcessor
3. ✅ Generate timestamps based on options
4. ✅ Extract frames with content detection (skip blank/blurry/NSFW)
5. ✅ Apply image filters using FilterService
6. ✅ Create contact sheet using FFmpegFilterGraphComposer
7. ✅ Apply watermarks if specified
8. ✅ Save output using OutputService
9. ✅ Generate WebVTT if requested
10. ✅ Comprehensive error handling and progress reporting
11. ✅ Per-file failures are reported and the batch continues

#### ✅ Error Handling & Progress

- ✅ Try-catch blocks for FFmpeg operations
- ✅ Console progress reporting during frame extraction
- ✅ User-friendly error messages with optional verbose stack traces
- ✅ Proper resource cleanup (image disposal)

## Implementation Status Summary

### ✅ Phase 1: Core Video Processing - COMPLETE

1. ✅ **VideoProcessor.cs** - Frame extraction and metadata
2. ✅ **FFmpegFilterGraphComposer.cs** - Contact sheet creation with headers and timestamps
3. ✅ **OutputService.cs** - File saving and WebVTT generation
4. ✅ **Integration** - Fully wired main processing pipeline

### ✅ Phase 2: Image Enhancement - COMPLETE

1. ✅ **FilterService.cs** - All image filters from original (greyscale, sepia, invert, fancy, cross, strip)
2. ✅ **ContentDetectionService.cs** - Blank/blur detection with configurable thresholds
3. ✅ **Enhanced timestamps and headers** - Full metadata display support

### ✅ Phase 3: Advanced Features - MOSTLY COMPLETE

1. ✅ **WebVTT generation** - HTML5 video player support, offsets taken from `SheetLayout`
2. ✅ **Fast seeking optimization** - FFmpeg.AutoGen with direct codec control
3. ✅ **Decoder reuse** - one decoder per file rather than one per frame
4. ⏳ **Performance optimizations** - further work possible to close the remaining gap with Go

## Critical Dependencies & Integration Points

### FFmpeg Integration (✅ Migration Complete)

- **Current**: Uses `FFmpeg.AutoGen` for direct libavcodec control and video processing
- **Previous**: Migrated from `FFMpegCore` due to performance limitations
- **Benefits**: Direct P/Invoke bindings to native FFmpeg libraries with full control over frame-level seeking
- **Performance Results**:
  - 4x faster than FFMpegCore implementation
  - Normal mode: 14.53s vs Go's 10.44s (1.4x slower)
  - Fast mode: 11.07s vs Go's 7.52s (1.5x slower)
- **Implementation Details**:
  - `Fast = true`: Accept first decoded frame (keyframe-based, very fast)
  - `Fast = false`: Continue decoding until exact timestamp (frame-accurate, slower)
  - Similar approach to original Go implementation using `screengen` library
  - Reference implementation: `reference/original-mt/` uses gitlab.com/opennota/screengen

### Image Composition Pipeline

Fully FFmpeg-based as of v2.4.0. There is no second composer.

**Per-Frame Processing (FFmpeg.AutoGen):**

- Decode frames directly to `AV_PIX_FMT_RGBA`, wrapped as `RgbaImage`
- Convert `RgbaImage` → AVFrame via `Utilities/AVFrameBridge.cs`
- Process with FFmpeg filter graphs:
  - `scale` filter for thumbnail resizing
  - `drawtext` filter for timestamps (freetype - pixel-perfect)
  - `drawtext` filter for header text (freetype - pixel-perfect)
  - `drawbox` filter for borders
  - `v360` filter for 360-degree VR conversion
- Apply filters via `FFmpegFilterService` using native FFmpeg filters
- Convert AVFrame → `RgbaImage` for composition

**Final Composition (RgbaImage):**

- Create canvas with background colour (`RgbaImage.Fill`)
- Arrange processed frames in grid layout (`RgbaImage.DrawImage`)
- Position header image
- Apply watermarks via `WatermarkService` (alpha blit)

**Encoding:** `Services/ImageEncoder.cs` writes PNG (`AV_CODEC_ID_PNG`, rgba) or JPEG
(`AV_CODEC_ID_MJPEG`, yuvj420p) using FFmpeg's own encoders.

**Requirement**: `drawtext` needs an FFmpeg build compiled with libfreetype. Homebrew's stock
`ffmpeg` formula is not; use `brew install ffmpeg-full`. `FFmpegHelper.WarnIfDrawTextMissing()`
warns when text cannot be rendered instead of silently omitting it.

### Content Detection Algorithms

Frame quality analysis uses specific thresholds:

All metrics come from one `ContentDetectionService.Analyse` call per candidate, computed on a
copy downscaled to `DetectionMaxEdge` (480px). One analysis serves the accept/reject decision,
the ranking of rejects, and `--dedupe`.

- **Blank detection**: luma standard deviation below a cutoff derived linearly from
  `--blank-threshold` (default 50 -> 38.25)
- **Blur detection**: variance of the Laplacian below a cutoff derived *logarithmically* from
  `--blur-threshold` (default 60 -> ~122). The metric spans roughly 6 to 6500, so v2's linear
  `threshold * 2` mapping only reached the bottom 3% of it
- **Polarity**: both thresholds read 0 = never skip, 100 = most aggressive. v2 had them
  inverted relative to each other, and `--blank-threshold 50` rejected every frame of ordinary
  video, failing the run
- **Retry logic**: `--retries` attempts spaced `--retry-step` seconds apart. `AcceptanceRatio`
  gives every check a comparable headroom number so exhaustion keeps the least-bad candidate
  rather than dropping the thumbnail
- **Deduplication**: 8x8 average hash compared by Hamming distance
- **Scene detection**: samples the window ahead and takes the largest fingerprint jump when it
  clears `SceneChangeMinDistance`

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

Performance comparison against the original Go implementation (44 thumbnails / 4 columns, 1080p video):

| Version | Mode | Time (seconds) | Speed vs Go |
|----------------|----------------|--------------|-------------|
| Go (original mt) | Normal | 10.44s | Baseline |
| Go (original mt) | Fast | 7.52s | 28% faster |
| mt.net v1 (FFMpegCore) | Normal | 58.53s | 5.6x slower ❌ |
| mt.net v1 (FFMpegCore) | Fast | 58.99s | 7.8x slower ❌ |
| **mt.net v2 (FFmpeg.AutoGen)** | **Normal** | **14.53s** | **1.4x slower ✅** |
| **mt.net v2 (FFmpeg.AutoGen)** | **Fast** | **11.07s** | **1.5x slower ✅** |

**Key Takeaway**: FFmpeg.AutoGen migration achieved **4x performance improvement**, bringing mt.net to within ~40-50% of Go's speed.

### Known Performance Considerations

- **Fast seeking** (`--fast` option) now uses FFmpeg.AutoGen with direct codec control - performance within 1.5x of Go
- **Large contact sheets** with many thumbnails can consume significant memory
- **Filter chaining** applies sequentially - order matters for some filters

### Testing Requirements

- ✅ Project builds successfully (clean build, no warnings)
- ✅ FFmpeg installed and accessible in PATH
- ✅ Column layout fix verified (4 columns working correctly)
- ✅ Fast seeking implemented with FFmpeg.AutoGen - performance within 40-50% of Go implementation
- ✅ Performance benchmarks completed and documented
- ⏳ Test various video formats (MP4, AVI, MKV, etc.) - needs more coverage
- ⏳ Test edge cases (short videos, long videos, corrupted files) - needs testing

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

The `--v360` option applies FFmpeg's v360 filter for 360-degree VR video processing:

- **Implementation**: Applied in `FFmpegFilterGraphComposer.BuildFrameFilterSpec()` during frame processing
- **Filter specification**: built from `--v360-input`, `--v360-output`, `--v360-stereo`, `--v360-fov`
  and `--v360-pitch`; defaults reproduce v2's `hequirect`/`flat`/`sbs`/125/-25
- **Sizing**: honours `--width`/`--height`. v2 hardcoded `w=400:h=300` while the grid was sized
  from `--width`, so any non-default width left gaps. With `--height 0` the output falls back to
  4:3, matching v2's default 400x300.
- **Critical detail**: Must convert from YUV (v360 output) to RGBA format using `format=pix_fmts=rgba`
- **Pipeline integration**: Replaces standard scale filter when enabled
- **Example usage**: `mt vr-video.mp4 --v360 --v360-fov 100 --width 640`

### Color Parsing Convention

Colors are specified as "R,G,B" strings and parsed by `Utilities/ColorParser.cs`.

## External Resource Guidelines

### When to Ask Before Sourcing

- **Behavioural questions about the original**: check `reference/original-mt/` first
- **FFmpeg.AutoGen usage**: prefer the existing call sites in `Services/` as the house style

### When to Proceed Independently

- **Standard .NET patterns**: Use established .NET conventions for common tasks
- **Pixel operations**: See `Models/RgbaImage.cs` and `Utilities/AVFrameBridge.cs`
- **General C# best practices**: No need to ask for standard language features

## Next Steps

### Immediate Testing Needed

1. **Test with real video files** - Verify end-to-end functionality
2. **FFmpeg availability check** - Ensure FFmpeg is installed and accessible
3. **Cross-platform testing** - Test on Windows, macOS, Linux
4. **Edge case handling** - Test with various video formats, corrupted files, missing codecs

### Future Enhancements (Post-Testing)

1. ✅ **FFmpeg.AutoGen Migration** - COMPLETED - Achieved 4x performance improvement
2. **Performance optimization** - Further optimize to close remaining ~40% gap with Go implementation
3. **Unit tests** - the main outstanding v3 item. Cover the FFmpeg-free logic:
   `CalculateTimestamps`, `BuildOutputPath`, `GetNextAvailablePath`, `SheetLayout` geometry,
   WebVTT coordinates, `TimeSpanParser`, `ColorParser`, `InputResolver`. Keep these methods
   static and dependency-free so they stay testable.
4. **Documentation** - Add usage examples, troubleshooting guide

### Migration Status

**✅ FFmpeg.AutoGen for Image Composition** - COMPLETED (v2.4.0 - ImageSharp fully removed)

- **Status**: Complete. ImageSharp, ImageSharp.Drawing and SixLabors.Fonts are gone; FFmpeg is
  the only imaging dependency. See [MIGRATION_FFMPEG_AUTOGEN.md](MIGRATION_FFMPEG_AUTOGEN.md).

  **FFmpeg handles:**
  - `scale` filter for resizing thumbnails
  - `drawtext` filter for timestamps and header text (freetype, matches mt exactly)
  - `drawbox` filter for borders, `v360` for VR conversion
  - Native FFmpeg filters for image effects (greyscale, sepia, etc.)
  - PNG/JPEG encoding (`ImageEncoder`) and still-image decoding (`ImageLoader`)

  **Managed code handles:**
  - `RgbaImage` - pooled RGBA raster; fill, opaque blit, alpha blit
  - Grid layout composition and canvas creation
  - `AVFrameBridge` - the single `RgbaImage` ↔ AVFrame converter

- **Benefits Achieved**:
  - ✅ Pixel-perfect text rendering matching mt (freetype)
  - ✅ Three fewer NuGet dependencies
  - ✅ Simple, maintainable grid layout code in C#
  - ✅ Fixed a per-frame native memory leak in the old AVFrame conversions

- **Why not full filter-graph layout**: grid layout is simpler in C# than FFmpeg's `xstack`/`tile`
  filters, and a raw RGBA blit is trivial. The critical goal (pixel-perfect text) comes from
  `drawtext` either way.

- **Timeline**: v2.0 introduced the hybrid composer; v2.4.0 removed ImageSharp and `--composer`.

## Key Reference Files

- [reference/original-mt/mt.go](reference/original-mt/mt.go) - Complete Go implementation (lines 82-865)
- [Models/ThumbnailOptions.cs](Models/ThumbnailOptions.cs) - All configuration options
- [Commands/RootCommand.cs](Commands/RootCommand.cs) - CLI interface and main entry point

## Key Implementation Details

- **Async/Await**: All I/O operations are async for better performance
- **Resource Management**: Images are properly disposed after processing
- **Error Handling**: Try-catch blocks with user-friendly error messages
- **Progress Reporting**: Console output during long-running operations
- **Retry Logic**: `--retries` attempts per frame, then the best candidate is kept rather than dropped
