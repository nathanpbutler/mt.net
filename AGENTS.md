# AGENTS.md

This file provides guidance to AI coding agents when working with code in this repository.

## Project Overview

**mt.net** is a .NET port of a Go-based media thumbnailing tool that generates contact sheets from video files using FFmpeg. The tool extracts frames at calculated timestamps, applies image processing filters, and creates customizable grid layouts with metadata headers.

### Core Features

- Configurable screenshot count, layout, and styling
- Header with file metadata
- Timestamps on thumbnails
- Various filters and image processing options (greyscale, sepia, strip effects, etc.)
- Upload functionality
- WebVTT generation for HTML5 video players

### Reference Implementation

The `reference/original-mt/` directory contains the complete Go implementation as a git submodule. When making changes that affect output compatibility, reference the Go implementation's behavior for consistency.

## Architecture & Core Components

### Service-Oriented Architecture

The application uses a stateless service pattern with clear separation of concerns:

- **VideoProcessor** ([Services/VideoProcessor.cs](Services/VideoProcessor.cs)) - FFmpeg integration for metadata extraction and frame capture
- **ImageComposer** ([Services/ImageComposer.cs](Services/ImageComposer.cs)) - Contact sheet creation, header generation, and timestamp overlays
- **FilterService** ([Services/FilterService.cs](Services/FilterService.cs)) - Image processing filters (greyscale, sepia, strip effects, etc.)
- **ContentDetectionService** ([Services/ContentDetectionService.cs](Services/ContentDetectionService.cs)) - Frame quality analysis (blank/blur/NSFW detection)
- **OutputService** ([Services/OutputService.cs](Services/OutputService.cs)) - File I/O, WebVTT generation, and filename pattern substitution

### Processing Pipeline Flow

```csharp
// Main pipeline in Commands/RootCommand.cs ProcessVideoAsync()
Video Input → Extract Metadata → Calculate Timestamps → Extract Frames
→ Apply Content Detection → Apply Filters → Create Contact Sheet
→ Apply Watermarks → Save Output → Generate WebVTT
```

### Configuration System

- **ThumbnailOptions** ([Models/ThumbnailOptions.cs](Models/ThumbnailOptions.cs)) - Single comprehensive options class with 40+ properties
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

## Development Commands

```bash
# Build and run (standard .NET commands)
dotnet build
dotnet run

# Build for release
dotnet build --configuration Release

# Build optimized single-file executable
dotnet publish -c Release -r osx-arm64 --self-contained

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
  - SixLabors.ImageSharp (image manipulation)
  - SixLabors.ImageSharp.Drawing (drawing operations)
  - SixLabors.Fonts (text rendering)
  - Microsoft.Extensions.Configuration.* (config management)
  - Serilog (logging)

### ✅ Command-Line Interface Complete

- **100% Feature Parity**: All 40+ options from original Go implementation
- **Direct File Interface**: `mt video.mp4 [options]` (matches original behavior)
- **Comprehensive Options**: Basic, time, visual, processing, upload, WebVTT, configuration options
- **Help System**: Built-in help with descriptions, defaults, and examples
- **Option Parsing**: Complete ThumbnailOptions object creation from CLI args
- **✅ Bug Fix (Oct 2025)**: Resolved `-c` alias conflict between `--config` and `--columns` (removed `-c` from `--config`, kept it for `--columns` to match original mt behavior)

### ✅ Configuration System

- **Models**: ThumbnailOptions, ImageFilter, HeaderInfo classes
- **Configuration**: JSON config support with environment variables and CLI overrides
- **Utilities**: Color parsing, time parsing, file validation helpers

### ✅ Core Services Implementation Complete

#### ✅ Video Processing (VideoProcessor.cs)

[Services/VideoProcessor.cs](Services/VideoProcessor.cs) - Fully implemented video processing logic:

- ✅ GetVideoMetadataAsync() - Extract metadata using FFmpeg.AutoGen
- ✅ CalculateTimestamps() - Calculate timestamps based on numCaps, interval, from, to, skipCredits
- ✅ ExtractFramesAsync() - Extract frames at calculated timestamps
- ✅ ExtractFrameWithRetriesAsync() - Extract frames with retry logic for content detection
- ✅ **Fast seeking support** - Fully implemented using FFmpeg.AutoGen with direct libavcodec control
  - Migrated from FFMpegCore to FFmpeg.AutoGen for frame-level seeking behavior
  - `--fast` option now provides true keyframe-based seeking matching original Go implementation
  - **Performance**: Achieved 4x improvement over FFMpegCore, now within ~40-50% of Go speed

#### ✅ Image Composition (ImageComposer.cs)

[Services/ImageComposer.cs](Services/ImageComposer.cs) - Complete contact sheet creation:

- ✅ CreateContactSheet() - Create grid layout with configurable columns, padding, borders
- ✅ DrawHeader() - Generate header with file metadata (filename, dimensions, duration, codec, fps, bitrate)
- ✅ AddTimestamp() - Overlay timestamps on thumbnails with configurable opacity
- ✅ ApplyWatermark() - Apply watermarks to center or all thumbnails
- ✅ Customizable colors, fonts, and styling

#### ✅ Image Filtering (FilterService.cs)

[Services/FilterService.cs](Services/FilterService.cs) - All filter implementations:

- ✅ ApplyFilters() - Filter chaining support
- ✅ Greyscale, Sepia, Invert filters
- ✅ Fancy filter (random rotation)
- ✅ Cross-processing effect
- ✅ Strip filter (film strip with sprocket holes)

#### ✅ Content Detection (ContentDetectionService.cs)

[Services/ContentDetectionService.cs](Services/ContentDetectionService.cs) - Frame quality analysis:

- ✅ IsBlankFrame() - Histogram analysis with configurable threshold
- ✅ IsBlurryFrame() - Laplacian variance for blur detection
- ✅ IsSafeForWork() - Experimental skin tone detection
- ✅ FindBestFrame() - Select best frame from candidates

#### ✅ Output Management (OutputService.cs)

[Services/OutputService.cs](Services/OutputService.cs) - File handling and export:

- ✅ SaveContactSheetAsync() - Save contact sheets in JPEG/PNG formats
- ✅ SaveIndividualImagesAsync() - Save individual thumbnail images
- ✅ GenerateWebVttAsync() - Generate WebVTT files with cue points
- ✅ BuildOutputPath() - Filename pattern substitution ({{.Path}}, {{.Name}})
- ✅ HandleFileOverwrite() - Overwrite/skip-existing logic

#### ❌ Upload Service (UploadService.cs) **Priority: LOW**

[Services/UploadService.cs](Services/UploadService.cs) - HTTP upload functionality (NOT YET IMPLEMENTED):

- UploadFile(filePath, uploadUrl, options)
- CreateMultipartFormData(file, metadata)
- HandleUploadProgress(callback)
- RetryUpload(file, maxRetries)

### ✅ Integration Complete

#### ✅ Main Processing Pipeline

[Commands/RootCommand.cs](Commands/RootCommand.cs) - Fully integrated async processing pipeline:

1. ✅ Validate input file
2. ✅ Extract video metadata using VideoProcessor
3. ✅ Generate timestamps based on options
4. ✅ Extract frames with content detection (skip blank/blurry/NSFW)
5. ✅ Apply image filters using FilterService
6. ✅ Create contact sheet using ImageComposer
7. ✅ Apply watermarks if specified
8. ✅ Save output using OutputService
9. ✅ Generate WebVTT if requested
10. ✅ Comprehensive error handling and progress reporting
11. ❌ Upload files (not yet implemented)

#### ✅ Error Handling & Progress

- ✅ Try-catch blocks for FFmpeg operations
- ✅ Console progress reporting during frame extraction
- ✅ User-friendly error messages with optional verbose stack traces
- ✅ Proper resource cleanup (image disposal)

#### 🚧 Configuration Enhancements (Partial)

- ❌ Save configuration to JSON files (--save-config) - placeholder only
- ❌ Load custom configuration files (--config-file) - placeholder only
- ❌ Show current configuration (--show-config) - placeholder only
- ❌ Environment variable support with MT_ prefix - not yet implemented

## Implementation Status Summary

### ✅ Phase 1: Core Video Processing - COMPLETE

1. ✅ **VideoProcessor.cs** - Frame extraction and metadata
2. ✅ **ImageComposer.cs** - Contact sheet creation with headers and timestamps
3. ✅ **OutputService.cs** - File saving and WebVTT generation
4. ✅ **Integration** - Fully wired main processing pipeline

### ✅ Phase 2: Image Enhancement - COMPLETE

1. ✅ **FilterService.cs** - All image filters from original (greyscale, sepia, invert, fancy, cross, strip)
2. ✅ **ContentDetectionService.cs** - Blank/blur detection with configurable thresholds
3. ✅ **Enhanced timestamps and headers** - Full metadata display support

### ✅ Phase 3: Advanced Features - MOSTLY COMPLETE

1. ❌ **UploadService.cs** - HTTP upload functionality (not started)
2. ✅ **WebVTT generation** - HTML5 video player support (complete)
3. ❌ **Configuration management** - Save/load config files (placeholders only)
4. ✅ **Fast seeking optimization** - Fully implemented with FFmpeg.AutoGen, 4x performance improvement
5. ⏳ **Performance optimizations** - Further optimization possible to close ~40% gap with Go

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

### ImageSharp Processing Chain

All image operations use `SixLabors.ImageSharp` with specific patterns:

- Load frames as `Image<Rgba32>`
- Apply filters via `FilterService.ApplyFilters()`
- Compose contact sheets with precise pixel calculations in `ImageComposer`

### Content Detection Algorithms

Frame quality analysis uses specific thresholds:

- **Blank detection**: Histogram analysis with configurable threshold (default: 85)
- **Blur detection**: Laplacian variance (default: 62)
- **Retry logic**: Up to 3 attempts to find suitable frames

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

## Next Steps

### Immediate Testing Needed

1. **Test with real video files** - Verify end-to-end functionality
2. **FFmpeg availability check** - Ensure FFmpeg is installed and accessible
3. **Cross-platform testing** - Test on Windows, macOS, Linux
4. **Edge case handling** - Test with various video formats, corrupted files, missing codecs

### Future Enhancements (Post-Testing)

1. ✅ **FFmpeg.AutoGen Migration** - COMPLETED - Achieved 4x performance improvement
2. **Performance optimization** - Further optimize to close remaining ~40% gap with Go implementation
3. **UploadService.cs** - Implement HTTP upload functionality
4. **Configuration persistence** - Implement --save-config, --config-file, --show-config
5. **Enhanced logging** - Integrate Serilog with configurable verbosity levels
6. **Unit tests** - Add comprehensive test coverage
7. **Documentation** - Add usage examples, troubleshooting guide
8. **Code cleanup** - Remove legacy FFMpegCore references and dependencies

## Key Reference Files

- [reference/original-mt/mt.go](reference/original-mt/mt.go) - Complete Go implementation (lines 82-865)
- [Models/ThumbnailOptions.cs](Models/ThumbnailOptions.cs) - All configuration options
- [Commands/RootCommand.cs](Commands/RootCommand.cs) - CLI interface and main entry point

## Key Implementation Details

- **Async/Await**: All I/O operations are async for better performance
- **Resource Management**: Images are properly disposed after processing
- **Error Handling**: Try-catch blocks with user-friendly error messages
- **Progress Reporting**: Console output during long-running operations
- **Retry Logic**: Frame extraction retries up to 3 times for content detection
