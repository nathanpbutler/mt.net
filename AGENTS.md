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

## Current Project Status (✅ = Complete, 🚧 = In Progress, ❌ = Not Started)

### ✅ Foundation Complete
- **Project Structure**: Organized folders for Commands, Configuration, Models, Services, Utilities
- **Dependencies**: All required NuGet packages added
  - System.CommandLine (CLI parsing)
  - FFMpegCore (video processing)
  - SixLabors.ImageSharp (image manipulation)
  - Microsoft.Extensions.Configuration.* (config management)
  - Serilog (logging)

### ✅ Command-Line Interface Complete
- **100% Feature Parity**: All 40+ options from original Go implementation
- **Direct File Interface**: `mt.net video.mp4 [options]` (matches original behavior)
- **Comprehensive Options**: Basic, time, visual, processing, upload, WebVTT, configuration options
- **Help System**: Built-in help with descriptions, defaults, and examples
- **Option Parsing**: Complete ThumbnailOptions object creation from CLI args

### ✅ Configuration System
- **Models**: ThumbnailOptions, ImageFilter, HeaderInfo classes
- **Configuration**: JSON config support with environment variables and CLI overrides
- **Utilities**: Color parsing, time parsing, file validation helpers

### 🚧 Core Implementation Needed

#### ❌ Video Processing (VideoProcessor.cs)
**Priority: HIGH**
```csharp
// Services/VideoProcessor.cs - Main video processing logic
- ExtractFrames(videoPath, timestamps) using FFMpegCore
- CalculateTimestamps(duration, numCaps, interval, from, to)
- HandleSeekingModes(fast vs accurate)
- ApplyTimeRanges(from, to, skipCredits)
- GetVideoMetadata(videoPath) -> HeaderInfo
```

#### ❌ Image Composition (ImageComposer.cs)  
**Priority: HIGH**
```csharp
// Services/ImageComposer.cs - Contact sheet creation
- CreateContactSheet(thumbnails, options) using ImageSharp
- ApplyLayout(images, columns, padding, background)
- GenerateHeader(headerInfo, options)
- AddTimestamps(images, timestamps, options)
- ApplyWatermarks(images, watermarkPaths, options)
```

#### ❌ Image Filtering (FilterService.cs)
**Priority: MEDIUM**
```csharp
// Services/FilterService.cs - Image filter implementations
- ApplyFilters(image, filterNames) 
- GreyscaleFilter, SepiaFilter, InvertFilter
- FancyFilter (rotation), CrossProcessingFilter
- StripFilter (film strip effect)
- ChainFilters(image, filterList)
```

#### ❌ Content Detection (ContentDetectionService.cs)
**Priority: MEDIUM**
```csharp
// Services/ContentDetectionService.cs - Frame analysis
- IsBlankFrame(image, threshold) using histogram analysis
- IsBlurryFrame(image, threshold) using Laplacian variance
- IsSafeForWork(image) - basic content filtering
- FindBestFrame(candidates, skipBlank, skipBlurry)
```

#### ❌ Output Management (OutputService.cs)
**Priority: HIGH**
```csharp
// Services/OutputService.cs - File handling and export
- SaveContactSheet(image, outputPath, options)
- SaveIndividualImages(images, outputPattern, options)
- GenerateWebVTT(timestamps, imagePath, dimensions)
- HandleFileOverwrite(path, overwrite, skipExisting)
```

#### ❌ Upload Service (UploadService.cs)
**Priority: LOW**
```csharp
// Services/UploadService.cs - HTTP upload functionality
- UploadFile(filePath, uploadUrl, options)
- CreateMultipartFormData(file, metadata)
- HandleUploadProgress(callback)
- RetryUpload(file, maxRetries)
```

### 🚧 Integration Tasks

#### ❌ Main Processing Pipeline
**File**: `Commands/RootCommand.cs` (update SetAction)
```csharp
1. Validate input file using FileValidator
2. Load configuration from files/environment
3. Extract video metadata using VideoProcessor  
4. Generate timestamps based on options
5. Extract frames using VideoProcessor
6. Apply content detection filters
7. Apply image filters using FilterService
8. Create contact sheet using ImageComposer
9. Save output using OutputService
10. Generate WebVTT if requested
11. Upload files if requested
```

#### ❌ Error Handling & Logging
- Comprehensive error handling for FFmpeg operations
- Progress reporting for long operations
- Detailed logging using Serilog with verbosity levels
- User-friendly error messages

#### ❌ Configuration Enhancements
- Save configuration to JSON files (--save-config)
- Load custom configuration files (--config-file)
- Show current configuration (--show-config)
- Environment variable support with MT_ prefix

## Implementation Priority

### Phase 1: Core Video Processing (Immediate)
1. **VideoProcessor.cs** - Frame extraction and metadata
2. **ImageComposer.cs** - Basic contact sheet creation
3. **OutputService.cs** - File saving and management
4. **Integration** - Wire up the main processing pipeline

### Phase 2: Image Enhancement (Soon)
1. **FilterService.cs** - All image filters from original
2. **ContentDetectionService.cs** - Blank/blur detection
3. **Enhanced timestamps and headers**

### Phase 3: Advanced Features (Later)
1. **UploadService.cs** - HTTP upload functionality
2. **WebVTT generation** - HTML5 video player support
3. **Configuration management** - Save/load config files
4. **Performance optimizations**

## Key Reference Files
- `reference/original-mt/mt.go` - Complete Go implementation (lines 82-865)
- `Models/ThumbnailOptions.cs` - All configuration options
- `Commands/RootCommand.cs` - CLI interface and main entry point

## Testing Strategy
- Create test videos of different formats and durations
- Verify output matches original Go tool behavior
- Test all filter combinations and edge cases
- Performance testing with large videos

## Notes for Future Development
- The CLI interface is complete and matches the original exactly
- All business logic classes are designed and ready for implementation  
- FFMpegCore and ImageSharp provide the core capabilities needed
- Focus on VideoProcessor.cs first - it's the foundation for everything else