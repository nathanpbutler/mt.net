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

## Usage

### Basic Usage

```bash
# Generate a 3x3 grid of thumbnails
dotnet run -- video.mp4

# Custom layout with 9 thumbnails in 3 columns, 300px width
dotnet run -- video.mp4 --numcaps 9 --columns 3 --width 300

# Apply filters and skip blank frames
dotnet run -- video.mp4 --filter greyscale,sepia --skip-blank --header-meta

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
- **Command-Line Interface**: 100% feature parity with original Go implementation
- **Configuration System**: JSON config support with environment variables and CLI overrides

### 🚧 In Development

- **Video Processing**: Frame extraction and metadata handling
- **Image Composition**: Contact sheet creation and layout
- **Image Filtering**: Implementation of all filter types
- **Content Detection**: Blank and blur frame detection
- **Output Management**: File handling and export functionality

## Dependencies

- **System.CommandLine**: Command-line interface parsing
- **FFMpegCore**: Video processing and frame extraction
- **SixLabors.ImageSharp**: Image manipulation and processing
- **Microsoft.Extensions.Configuration**: Configuration management
- **Serilog**: Structured logging

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
├── Commands/           # CLI command definitions
├── Configuration/      # Configuration management
├── Models/            # Data models and options
├── Services/          # Business logic services
├── Utilities/         # Helper utilities
└── reference/         # Original Go implementation
```

## License

This project is licensed under the GNU General Public License v3.0. See the [LICENSE](LICENSE) file for details.

## Acknowledgments

This project is based on the original `mt` tool written in Go. Thanks to the original developers for creating such a useful media processing utility.
