# mt.net

A .NET port of the Go-based media thumbnailing tool `mt` (media thumbnailer). This tool generates thumbnail contact sheets from video files using FFmpeg, with configurable screenshot count, layout, and styling options.

## Features

- **Video Thumbnail Generation**: Extract frames from video files and create contact sheets
- **Flexible Layout**: Configurable number of screenshots, columns, and dimensions
- **Rich Metadata**: Display file information in headers with timestamps
- **Image Filters**: Apply various filters like greyscale, sepia, invert, and more
- **Content Detection**: Skip blank or blurry frames automatically
- **Multiple Output Formats**: Save contact sheets and individual thumbnails
- **WebVTT Support**: Generate WebVTT files for HTML5 video players
- **Upload Functionality**: HTTP upload capabilities for generated images
- **Comprehensive CLI**: Feature-complete command-line interface

## Installation

### Prerequisites

- .NET 9.0 or later
- FFmpeg (required for video processing)

### Build from Source

```bash
git clone https://github.com/yourusername/mt.net.git
cd mt.net
dotnet build
```

## Quick Start

1. **Install FFmpeg** (required):

   ```bash
   # macOS
   brew install ffmpeg

   # Ubuntu/Debian
   sudo apt-get install ffmpeg

   # Windows: Download from https://ffmpeg.org/download.html
   ```

2. **Build the project**:

   ```bash
   dotnet build
   ```

3. **Generate your first contact sheet**:

   ```bash
   dotnet run -- path/to/your/video.mp4
   ```

## Usage

### Basic Usage

```bash
# Generate a 3x3 grid of thumbnails (default: 4 thumbnails, 2 columns)
dotnet run -- video.mp4

# Custom layout with 9 thumbnails in 3 columns, 300px width
dotnet run -- video.mp4 --numcaps 9 --columns 3 --width 300

# Apply filters and skip blank frames
dotnet run -- video.mp4 --filter greyscale,sepia --skip-blank --header-meta

# Generate individual thumbnail images instead of contact sheet
dotnet run -- video.mp4 --single-images

# Create WebVTT file for HTML5 video players
dotnet run -- video.mp4 --vtt

# Show available filters
dotnet run -- --filters

# Show all available options
dotnet run -- --help
```

### Command Line Options

The tool provides comprehensive command-line options organized into several categories:

#### Basic Options

- `--numcaps, -n`: Number of screenshots to generate (default: 16)
- `--columns, -c`: Number of columns in the grid (default: 4)
- `--width, -w`: Width of individual thumbnails (default: 120px)
- `--output, -o`: Output filename pattern

#### Time Options

- `--interval`: Time interval between screenshots
- `--from`: Start time for screenshot extraction
- `--to`: End time for screenshot extraction
- `--skip-credits`: Skip credits at the end

#### Visual Options

- `--filter`: Apply image filters (greyscale, sepia, invert, etc.)
- `--background`: Background color for the contact sheet
- `--padding`: Padding between thumbnails
- `--header-meta`: Include file metadata in header

#### Processing Options

- `--skip-blank`: Skip blank or nearly blank frames
- `--skip-blur`: Skip blurry frames
- `--accurate-seek`: Use accurate seeking (slower but more precise)

#### Output Options

- `--save-individual`: Save individual thumbnail images
- `--webvtt`: Generate WebVTT file for video players
- `--overwrite`: Overwrite existing files

For a complete list of options, run:

```bash
dotnet run -- --help
```

## Configuration

The application supports configuration through:

1. **JSON Configuration Files**: Store settings in JSON format
2. **Environment Variables**: Use `MT_` prefix for environment variables
3. **Command Line Arguments**: Override any configuration option

### Example Configuration

```json
{
  "numCaps": 16,
  "columns": 4,
  "width": 120,
  "filters": ["greyscale"],
  "skipBlank": true,
  "headerMeta": true
}
```

## Development Status

### ✅ Completed Features

- **Project Structure**: Organized codebase with proper separation of concerns
- **Dependencies**: All required NuGet packages integrated
- **Command-Line Interface**: 100% feature parity with original Go implementation (40+ options)
  - ✅ **Bug Fix (Oct 2025)**: Resolved `-c` alias conflict (now only `--columns` uses `-c`)
- **Configuration System**: JSON config support with environment variables and CLI overrides
- **Video Processing**: Complete FFmpeg integration for metadata extraction and frame capture
- **Image Composition**: Full contact sheet creation with headers, timestamps, and watermarks
- **Image Filtering**: All filter types implemented (greyscale, sepia, invert, fancy, cross, strip)
- **Content Detection**: Blank and blur frame detection with configurable thresholds
- **Output Management**: File handling, WebVTT generation, and multiple output formats
- **Processing Pipeline**: Fully integrated async workflow with progress reporting
- **Testing**: Verified with real video files, layout working correctly

### ⚠️ Known Limitations

- **Fast Seeking Performance**: The `--fast` option is implemented using `-noaccurate_seek` but doesn't achieve the same performance as the original Go implementation
  - FFMpegCore's high-level API has limited control over frame-level seeking behavior
  - **Future Enhancement**: Migrate to FFmpeg.AutoGen for direct libavcodec control (see roadmap below)

### 🚧 In Development / Not Yet Implemented

- **Upload Functionality**: HTTP upload feature (placeholder only)
- **Configuration Persistence**: --save-config, --config-file, --show-config (placeholders only)
- **Enhanced Logging**: Serilog integration for structured logging

### ⚠️ Prerequisites

**FFmpeg Required**: This tool requires FFmpeg to be installed and available in your system PATH. Download from [ffmpeg.org](https://ffmpeg.org/download.html)

## Dependencies

- **System.CommandLine**: Command-line interface parsing
- **FFMpegCore**: Video processing and frame extraction
- **SixLabors.ImageSharp**: Image manipulation and processing
- **SixLabors.ImageSharp.Drawing**: Drawing operations for contact sheets
- **SixLabors.Fonts**: Text rendering for timestamps and headers
- **Microsoft.Extensions.Configuration**: Configuration management
- **Serilog**: Structured logging (planned)

## Contributing

This project is a .NET port of the original Go implementation. The original source code is available in the `reference/original-mt/` directory for reference during development.

### Development Commands

```bash
# Build the project
dotnet build

# Run the application
dotnet run

# Build for release
dotnet build --configuration Release

# Run tests
dotnet test
```

### Project Structure

```text
mt.net/
├── Commands/           # CLI command definitions (RootCommand.cs)
├── Configuration/      # Configuration management (AppConfig, ConfigurationBuilder)
├── Models/            # Data models (ThumbnailOptions, HeaderInfo, ImageFilter)
├── Services/          # Business logic services
│   ├── VideoProcessor.cs          # Video metadata and frame extraction
│   ├── ImageComposer.cs           # Contact sheet creation
│   ├── FilterService.cs           # Image filters
│   ├── ContentDetectionService.cs # Blank/blur detection
│   └── OutputService.cs           # File saving and WebVTT
├── Utilities/         # Helper utilities (ColorParser, TimeSpanParser, FileValidator)
└── reference/         # Original Go implementation
```

## How It Works

1. **Video Analysis**: Extracts video metadata (resolution, duration, codec, fps, bitrate)
2. **Timestamp Calculation**: Determines optimal frame positions based on video duration and options
3. **Frame Extraction**: Uses FFmpeg to capture frames at calculated timestamps
4. **Content Detection**: Optionally skips blank or blurry frames (with retry logic)
5. **Filter Application**: Applies visual effects (greyscale, sepia, fancy rotation, etc.)
6. **Contact Sheet Creation**: Arranges thumbnails in a grid with customizable layout
7. **Header Generation**: Adds metadata header with file information
8. **Timestamp Overlay**: Adds time codes to each thumbnail
9. **Output**: Saves final contact sheet and optionally generates WebVTT file

## License

This project is licensed under the GNU General Public License v3.0. See the [LICENSE](LICENSE) file for details.

## Future Roadmap

### High Priority

1. **FFmpeg.AutoGen Migration** - Migrate from FFMpegCore to FFmpeg.AutoGen for better performance
   - Enable true fast seeking behavior matching the original Go implementation
   - Provide direct control over libavcodec/libavformat for frame-level seeking
   - The original Go tool uses `screengen` library with fine-grained control over frame decoding

### Medium Priority

1. **Upload Service** - Implement HTTP upload functionality for generated contact sheets
2. **Configuration Management** - Implement save/load configuration file features
3. **Enhanced Logging** - Integrate Serilog for structured, configurable logging

### Low Priority

1. **Unit Testing** - Add comprehensive test coverage
2. **Documentation** - Expand usage examples and troubleshooting guides
3. **Performance Profiling** - Optimize image processing and filter operations

## Acknowledgments

This project is based on the original `mt` tool written in Go. Thanks to the original developers for creating such a useful media processing utility.
