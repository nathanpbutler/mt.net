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

# Run with arguments
dotnet run -- <arguments>
```

### Project Structure
- Single console application targeting .NET 9.0
- Entry point: `Program.cs` (currently basic template)
- Project file: `mt.net.csproj`
- Solution file: `mt.net.sln`

## Original Implementation Reference

The original Go code in `reference/original-mt/` provides the complete feature set to port:

### Key Components (from Go version):
- **Configuration System**: JSON config files with environment variable and CLI flag overrides
- **Core Functionality**: Video thumbnail extraction using FFmpeg bindings
- **Image Processing**: Screenshot layout, headers, timestamps, filters
- **Output Options**: Single images vs contact sheets, WebVTT files
- **Advanced Features**: Blank/blur detection, content filtering, upload capabilities

### Configuration Structure
The original uses a comprehensive config system (see `reference/original-mt/mt.json`) with 30+ options including numcaps, columns, padding, fonts, colors, filters, and upload settings.

## Implementation Notes

This is a ground-up .NET implementation, not a direct translation. Key considerations:
- Will need .NET equivalents for FFmpeg integration (likely FFMpegCore or similar)
- Image processing using System.Drawing, ImageSharp, or SkiaSharp
- Configuration using .NET's IConfiguration system
- CLI argument parsing with System.CommandLine or similar

The original Go code should be referenced for understanding the complete feature set, algorithms, and expected behavior patterns.