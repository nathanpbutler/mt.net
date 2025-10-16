<!-- markdownlint-disable MD033 -->
# mt.net

A .NET port of [mt](https://github.com/mutschler/mt) (media thumbnailer). Generate video thumbnail contact sheets using FFmpeg with configurable layout, filters, and metadata.

<p align="center">
  <a href="https://www.youtube.com/watch?v=dQw4w9WgXcQ"><img src="samples/rick.jpg" alt="Sample Contact Sheet" width="680"></a>
  <br>
  <em>Example contact sheet</em>
</p>

## Features

- Generate thumbnail contact sheets from video files
- Configurable grid layout, dimensions, and styling
- Image filters (greyscale, sepia, invert, fancy, cross, strip)
- Skip blank or blurry frames automatically
- WebVTT output for HTML5 video players
- Individual thumbnail export
- Dual composer system: FFmpeg.AutoGen (default) or ImageSharp (legacy)

## Installation

**Requirements:** .NET 9.0+ and FFmpeg

```bash
# macOS
brew install ffmpeg

# Ubuntu/Debian
sudo apt-get install ffmpeg

# Windows
# Download from https://www.gyan.dev/ffmpeg/builds and add to PATH
```

**Build:**

```bash
git clone https://github.com/nathanpbutler/mt.net.git
cd mt.net
dotnet build
```

## Usage

```bash
# Basic usage (4 thumbnails, 2 columns)
mt video.mp4

# Custom layout
mt video.mp4 --numcaps 9 --columns 3 --width 300

# Apply filters and skip blank frames
mt video.mp4 --filter greyscale,sepia --skip-blank --header-meta

# Individual thumbnails
mt video.mp4 --single-images

# WebVTT for HTML5 players
mt video.mp4 --vtt
```

### Key Options

**Layout:**

- `-n, --numcaps`: Number of screenshots (default: 4)
- `-c, --columns`: Grid columns (default: 2)
- `-w, --width`: Thumbnail width in pixels (default: 400)
- `-h, --height`: Thumbnail height in pixels (default: 0 = auto)
- `-p, --padding`: Padding between images (default: 10)

**Time:**

- `-i, --interval`: Time interval between captures (overrides numcaps)
- `--from`: Start time (HH:MM:SS)
- `--to, --end`: End time (HH:MM:SS)
- `--skip-credits`: Skip last 2 minutes or 10% of video

**Visual:**

- `--filter`: Apply filters (greyscale, sepia, invert, fancy, cross, strip)
- `-f, --font`: Font for timestamps and header (default: DroidSans)
- `--font-size`: Font size in pixels (default: 12)
- `-d, --disable-timestamps`: Disable timestamp overlay
- `--timestamp-opacity`: Timestamp text opacity 0.0-1.0 (default: 1.0)
- `--header`: Include header with file information (default: true)
- `--header-meta`: Include codec, FPS, bitrate in header
- `--header-image`: Image to display in header
- `--bg-content`, `--bg-header`, `--fg-header`: Colors (R,G,B)
- `--border`: Border width around thumbnails (default: 0)
- `--watermark`: Watermark for center thumbnail
- `--watermark-all`: Watermark for all thumbnails
- `--comment`: Custom comment for header

**Processing:**

- `-b, --skip-blank`: Skip blank frames (3 retries)
- `--skip-blurry`: Skip blurry frames (3 retries)
- `--fast`: Fast but less accurate seeking
- `--sfw`: Content filtering for safe-for-work output (experimental)
- `--blur-threshold`: Blur detection threshold 0-100 (default: 62)
- `--blank-threshold`: Blank detection threshold 0-100 (default: 85)

**Output:**

- `-o, --output`: Output filename pattern
- `-s, --single-images`: Save individual images
- `--overwrite`: Overwrite existing files (default: auto-increment with -01, -02, etc.)
- `--skip-existing`: Skip processing if output already exists
- `--vtt`: Generate WebVTT file
- `--webvtt`: WebVTT mode (disables headers, padding, timestamps)

**Configuration:**

- `--config`: Configuration file path
- `--save-config`: Save settings to config file (placeholder only)
- `--config-file`: Use specific config file (placeholder only)
- `--show-config`: Show config path and values (placeholder only)

**Upload (not implemented):**

- `--upload`: Upload generated files via HTTP (placeholder only)
- `--upload-url`: URL for file upload (placeholder only)

**Global:**

- `--composer`: Image composer: `ffmpeg` (default) or `imagesharp`
- `-v, --verbose`: Verbose logging
- `--filters`: List all available image filters
- `--help`: Show all options
- `--version`: Show version information

## Configuration

Supports JSON config files, environment variables (prefix `MT_`), and CLI arguments.

**Example config.json:**

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

## Implementation Status

**Complete:**

- Full feature parity with original Go implementation (40+ CLI options)
- FFmpeg.AutoGen video decoder with direct libavcodec control
- Hybrid composer (FFmpeg filter graphs for rendering + ImageSharp for layout)
- Pixel-perfect text rendering with freetype (matches original)
- WebVTT generation with accurate coordinate mapping
- Image filters, content detection, metadata headers
- Configuration system (JSON, env vars, CLI)

**Not Implemented:**

- HTTP upload (`--upload`, `--upload-url`) - CLI placeholders only
- Config persistence (`--save-config`, `--show-config`) - CLI placeholders only
- Serilog integration - dependency included but not fully configured

**Known Issues:**

- `--filters` flag requires dummy file argument due to System.CommandLine validation
- ImageSharp composer has different text rendering than freetype (use default FFmpeg composer)

## Architecture

```plaintext
mt.net/
├── Program.cs                      # Entry point
├── Commands/
│   └── RootCommand.cs              # CLI definitions
├── Configuration/
│   ├── AppConfig.cs                # Config model
│   └── ConfigurationBuilder.cs     # Config loading
├── Models/
│   ├── ThumbnailOptions.cs         # Generation options
│   ├── HeaderInfo.cs               # Metadata
│   └── ImageFilter.cs              # Filter definitions
├── Services/
│   ├── VideoProcessor.cs           # Metadata extraction
│   ├── FFmpegAutoGenVideoDecoder.cs # Frame extraction
│   ├── FFmpegFilterGraphComposer.cs # Default composer
│   ├── ImageComposer.cs            # Legacy composer
│   ├── FFmpegFilterService.cs      # FFmpeg filters
│   ├── ContentDetectionService.cs  # Blank/blur detection
│   └── OutputService.cs            # File/WebVTT output
└── Utilities/
    ├── ColorParser.cs              # RGB parsing
    ├── TimeSpanParser.cs           # Time parsing
    └── FFmpegHelper.cs             # FFmpeg helpers
```

## Development

```bash
# Build
dotnet build

# Run
dotnet run -- video.mp4

# Release build
dotnet build --configuration Release

# Tests
dotnet test
```

The original Go source is in `reference/original-mt/` for reference.

## License

GNU General Public License v3.0. See [LICENSE](LICENSE).

## Acknowledgments

Based on the original [mt](https://github.com/mutschler/mt) tool by mutschler.
