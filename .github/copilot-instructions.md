# Copilot Instructions for mt.net

## Project Overview

**mt.net** is a .NET port of a Go-based media thumbnailing tool that generates contact sheets from video files using FFmpeg. The tool extracts frames at calculated timestamps, applies image processing filters, and creates customizable grid layouts with metadata headers.

## Architecture & Core Components

### Service-Oriented Architecture

The application uses a stateless service pattern with clear separation of concerns:

- **VideoProcessor** (`Services/VideoProcessor.cs`) - FFmpeg integration for metadata extraction and frame capture
- **FFmpegFilterGraphComposer** (`Services/FFmpegFilterGraphComposer.cs`) - FFmpeg.AutoGen-based contact sheet creation with filter graphs (default)
- **ImageComposer** (`Services/ImageComposer.cs`) - Legacy ImageSharp-based contact sheet creation (fallback via `--composer imagesharp`)
- **FilterService** (`Services/FilterService.cs`) - Image processing filters (greyscale, sepia, strip effects, etc.)
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
- **Composer Selection**: `--composer` option chooses between `ffmpeg` (default) and `imagesharp` (legacy)

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
var filterService = new FilterService();
// Use directly without DI container
```

### Async Resource Management

Critical pattern for image processing - always dispose images to prevent memory leaks:

```csharp
foreach (var (frame, _) in frames)
{
    frame.Dispose(); // Essential for ImageSharp images
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

**Default (FFmpeg.AutoGen - Hybrid)**: Uses FFmpeg for frame operations, ImageSharp for final composition:

**Per-Frame Processing (FFmpeg.AutoGen):**

- Load frames as `Image<Rgba32>` from video
- Convert ImageSharp → AVFrame
- Process with FFmpeg filter graphs:
  - `scale` filter (thumbnail resizing)
  - `drawtext` filter (timestamps with freetype - pixel-perfect)
  - `drawtext` filter (header text with freetype - pixel-perfect)
  - `drawbox` filter (borders)
- Apply filters via `FFmpegFilterService` (native FFmpeg filters)
- Convert AVFrame → ImageSharp

**Final Composition (ImageSharp):**

- Create canvas with background color
- Arrange processed frames in grid layout (DrawImage)
- Position header
- Apply watermarks

**Legacy (ImageSharp)**: Available via `--composer imagesharp`:

- Load frames as `Image<Rgba32>`
- Resize with ImageSharp
- Apply filters via `FilterService.ApplyFilters()`
- Text rendering via `SixLabors.Fonts` (differs from freetype)
- Compose contact sheets in `ImageComposer`

**Key Difference**: FFmpeg composer achieves **pixel-perfect text** matching Go mt (freetype), while keeping grid layout simple in C# code. ImageSharp composer uses different text engine.

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
dotnet publish -c Release -r osx-arm64 --self-contained

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

- **Font Rendering** (ImageSharp composer only): When using `--composer imagesharp`, text rendering uses a different engine than freetype, resulting in slightly different text appearance. The default FFmpeg composer provides pixel-perfect text rendering matching the original Go implementation.

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

### Color Parsing Convention

Colors are specified as "R,G,B" strings and parsed by `Utilities/ColorParser.cs`.

## External Resource Guidelines

### When to Ask Before Sourcing

- **Documentation/code not in temp/**: Ask user to source it first before looking externally
- **FFmpeg.AutoGen examples**: Check `temp/FFmpeg.AutoGen/` before searching online
- **API references**: Use temp/ directory content as primary source for current development

### When to Proceed Independently

- **Standard .NET patterns**: Use established .NET conventions for common tasks
- **ImageSharp operations**: Standard SixLabors.ImageSharp documentation is acceptable
- **General C# best practices**: No need to ask for standard language features

## Reference Implementation

The `reference/original-mt/` directory contains the complete Go implementation as a git submodule. When making changes that affect output compatibility, reference the Go implementation's behavior for consistency.

## Migration Status

**✅ FFmpeg.AutoGen for Image Composition** - COMPLETED (v2.0 - Hybrid Approach)

- **Status**: Fully implemented and set as default composer
- **Implementation**: Hybrid approach combining FFmpeg.AutoGen and ImageSharp
  - **FFmpeg.AutoGen**: Frame resizing, text rendering (freetype), borders, image filters
  - **ImageSharp**: Grid layout, canvas creation, watermarks (simpler than FFmpeg xstack/tile)
- **Benefits**:
  - Pixel-perfect text rendering matching mt (uses freetype)
  - Simple, maintainable grid layout code in C#
  - Best of both worlds: exact rendering + clean architecture
- **Why Hybrid**: Critical goal (pixel-perfect text) achieved while keeping code maintainable
- **Access**: Use `--composer ffmpeg` (default) or `--composer imagesharp` (legacy fallback)
- **Future**: Full FFmpeg migration possible but not necessary - current approach is optimal
- **Migration Period**: ImageSharp composer kept temporarily as fallback, will be removed after testing period
