# Copilot Instructions for mt.net

## Project Overview

**mt.net** is a .NET port of a Go-based media thumbnailing tool that generates contact sheets from video files using FFmpeg. The tool extracts frames at calculated timestamps, applies image processing filters, and creates customizable grid layouts with metadata headers.

## Architecture & Core Components

### Service-Oriented Architecture
The application uses a stateless service pattern with clear separation of concerns:

- **VideoProcessor** (`Services/VideoProcessor.cs`) - FFmpeg integration for metadata extraction and frame capture
- **ImageComposer** (`Services/ImageComposer.cs`) - Contact sheet creation, header generation, and timestamp overlays
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

## Critical Dependencies & Integration Points

### FFmpeg Integration (⚠️ Migration in Progress)
- **Current**: Uses `FFMpegCore` for high-level video processing in main codebase
- **Migration Target**: `FFmpeg.AutoGen` cloned in `temp/FFmpeg.AutoGen/` for direct libavcodec control
- **Temp Directory**: Contains working code/docs for current development - check here first before sourcing externally
- **Why**: Better performance for frame-level seeking operations (see AGENTS.md notes on fast seeking)

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

### Known Performance Considerations
- **Fast seeking** (`--fast` option) uses `-noaccurate_seek` but performance doesn't match original Go implementation
- **Large contact sheets** with many thumbnails can consume significant memory
- **Filter chaining** applies sequentially - order matters for some filters

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