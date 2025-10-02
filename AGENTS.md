# AGENTS.md

This file provides guidance to AI coding agents when working with code in this repository.

## Project Overview

This is a .NET port of the Go-based media thumbnailing tool `mt` (media thumbnailer). The original Go implementation is available as a git submodule in `reference/original-mt/` for reference during development.

The original tool generates thumbnail contact sheets from video files using FFmpeg, with features like:

- Configurable screenshot count, layout, and styling
- Header with file metadata
- Timestamps on thumbnails  
- Various filters and image processing options
- Upload functionality
- WebVTT generation for HTML5 video players

## Development Commands

### Build and Run

```bash
# Build the project
dotnet build

# Run the application
dotnet run

# Build for release
dotnet build --configuration Release

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
  - FFMpegCore (video processing) ⚠️ *See note about FFmpeg.AutoGen migration below*
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

#### ⚠️ Video Processing (VideoProcessor.cs)

`Services/VideoProcessor.cs` - Fully implemented video processing logic:

- ✅ GetVideoMetadataAsync() - Extract metadata using FFMpegCore
- ✅ CalculateTimestamps() - Calculate timestamps based on numCaps, interval, from, to, skipCredits
- ✅ ExtractFramesAsync() - Extract frames at calculated timestamps
- ✅ ExtractFrameWithRetriesAsync() - Extract frames with retry logic for content detection
- ⚠️ **Fast seeking support** - Partially implemented using `-noaccurate_seek` flag
  - Current implementation uses FFMpegCore which has limited control over frame-level seeking behavior
  - `--fast` option adds `-noaccurate_seek` flag but doesn't provide same performance as original Go implementation
  - **Recommendation**: Migrate to FFmpeg.AutoGen for direct libavcodec control (see "Future Enhancements" below)

#### ✅ Image Composition (ImageComposer.cs)

`Services/ImageComposer.cs` - Complete contact sheet creation:

- ✅ CreateContactSheet() - Create grid layout with configurable columns, padding, borders
- ✅ DrawHeader() - Generate header with file metadata (filename, dimensions, duration, codec, fps, bitrate)
- ✅ AddTimestamp() - Overlay timestamps on thumbnails with configurable opacity
- ✅ ApplyWatermark() - Apply watermarks to center or all thumbnails
- ✅ Customizable colors, fonts, and styling

#### ✅ Image Filtering (FilterService.cs)

`Services/FilterService.cs` - All filter implementations:

- ✅ ApplyFilters() - Filter chaining support
- ✅ Greyscale, Sepia, Invert filters
- ✅ Fancy filter (random rotation)
- ✅ Cross-processing effect
- ✅ Strip filter (film strip with sprocket holes)

#### ✅ Content Detection (ContentDetectionService.cs)

`Services/ContentDetectionService.cs` - Frame quality analysis:

- ✅ IsBlankFrame() - Histogram analysis with configurable threshold
- ✅ IsBlurryFrame() - Laplacian variance for blur detection
- ✅ IsSafeForWork() - Experimental skin tone detection
- ✅ FindBestFrame() - Select best frame from candidates

#### ✅ Output Management (OutputService.cs)

`Services/OutputService.cs` - File handling and export:

- ✅ SaveContactSheetAsync() - Save contact sheets in JPEG/PNG formats
- ✅ SaveIndividualImagesAsync() - Save individual thumbnail images
- ✅ GenerateWebVttAsync() - Generate WebVTT files with cue points
- ✅ BuildOutputPath() - Filename pattern substitution ({{.Path}}, {{.Name}})
- ✅ HandleFileOverwrite() - Overwrite/skip-existing logic

#### ❌ Upload Service (UploadService.cs) **Priority: LOW**

```csharp
// Services/UploadService.cs - HTTP upload functionality (NOT YET IMPLEMENTED)
- UploadFile(filePath, uploadUrl, options)
- CreateMultipartFormData(file, metadata)
- HandleUploadProgress(callback)
- RetryUpload(file, maxRetries)
```

### ✅ Integration Complete

#### ✅ Main Processing Pipeline

`Commands/RootCommand.cs` - Fully integrated async processing pipeline:

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

### 🚧 Phase 3: Advanced Features - PARTIAL

1. ❌ **UploadService.cs** - HTTP upload functionality (not started)
2. ✅ **WebVTT generation** - HTML5 video player support (complete)
3. ❌ **Configuration management** - Save/load config files (placeholders only)
4. ⚠️ **Fast seeking optimization** - Implemented with `-noaccurate_seek` but not as performant as original
5. ⏳ **Performance optimizations** - To be evaluated after testing

## Next Steps

### Immediate Testing Needed

1. **Test with real video files** - Verify end-to-end functionality
2. **FFmpeg availability check** - Ensure FFmpeg is installed and accessible
3. **Cross-platform testing** - Test on Windows, macOS, Linux
4. **Edge case handling** - Test with various video formats, corrupted files, missing codecs

### Future Enhancements (Post-Testing)

1. **FFmpeg.AutoGen Migration** ⚠️ *HIGH PRIORITY for performance*
   - Migrate from FFMpegCore to FFmpeg.AutoGen for direct libavcodec/libavformat control
   - This will enable true fast seeking behavior matching the original Go implementation
   - Original uses `screengen` library with `gen.Fast` flag that controls frame-level seeking:
     - `Fast = true`: Accept first decoded frame (keyframe-based, very fast)
     - `Fast = false`: Continue decoding until exact timestamp (frame-accurate, slower)
   - FFMpegCore's high-level API doesn't expose this level of control
   - FFmpeg.AutoGen provides P/Invoke bindings to native FFmpeg libraries with full control
   - Reference implementation: `reference/original-mt/` uses gitlab.com/opennota/screengen (similar to FFmpeg.AutoGen approach)

2. **UploadService.cs** - Implement HTTP upload functionality
3. **Configuration persistence** - Implement --save-config, --config-file, --show-config
4. **Enhanced logging** - Integrate Serilog with configurable verbosity levels
5. **Performance profiling** - Optimize frame extraction and image processing
6. **Unit tests** - Add comprehensive test coverage
7. **Documentation** - Add usage examples, troubleshooting guide

## Key Reference Files

- `reference/original-mt/mt.go` - Complete Go implementation (lines 82-865)
- `Models/ThumbnailOptions.cs` - All configuration options
- `Commands/RootCommand.cs` - CLI interface and main entry point

## Testing Strategy

- Create test videos of different formats and durations
- Verify output matches original Go tool behavior
- Test all filter combinations and edge cases
- Performance testing with large videos

## Architecture Notes

### Service Layer Design

All services are stateless and can be instantiated on-demand:

- **VideoProcessor** - Handles FFmpeg interactions via FFMpegCore
- **ImageComposer** - Pure image manipulation using ImageSharp
- **FilterService** - Applies visual effects to images
- **ContentDetectionService** - Analyzes image quality metrics
- **OutputService** - File I/O and path management

### Processing Pipeline Flow

```plaintext
Input Video → VideoProcessor.GetMetadata()
           → VideoProcessor.CalculateTimestamps()
           → VideoProcessor.ExtractFrames() (with ContentDetection)
           → FilterService.ApplyFilters()
           → ImageComposer.CreateContactSheet()
           → ImageComposer.ApplyWatermark()
           → OutputService.SaveContactSheet()
           → OutputService.GenerateWebVtt() [optional]
```

### Key Implementation Details

- **Async/Await**: All I/O operations are async for better performance
- **Resource Management**: Images are properly disposed after processing
- **Error Handling**: Try-catch blocks with user-friendly error messages
- **Progress Reporting**: Console output during long-running operations
- **Retry Logic**: Frame extraction retries up to 3 times for content detection

### Testing Requirements

- ✅ Project builds successfully (clean build, no warnings)
- ✅ FFmpeg installed and accessible in PATH
- ✅ Column layout fix verified (4 columns working correctly)
- ⚠️ Fast seeking implemented but performance not matching original (see FFmpeg.AutoGen migration note)
- ⏳ Test various video formats (MP4, AVI, MKV, etc.) - needs more coverage
- ⏳ Test edge cases (short videos, long videos, corrupted files) - needs testing
