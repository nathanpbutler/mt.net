using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;
using nathanbutlerDEV.mt.net.Models;
using nathanbutlerDEV.mt.net.Utilities;

namespace nathanbutlerDEV.mt.net.Services;

/// <summary>
/// Composes contact sheets using FFmpeg filter graphs for native FFmpeg-based image composition.
/// This provides pixel-perfect text rendering using freetype (matching the original Go implementation)
/// and potentially better performance than ImageSharp-based composition.
/// </summary>
public sealed unsafe class FFmpegFilterGraphComposer : IDisposable
{
    // Service for applying image filters
    private readonly FFmpegFilterService _filterService;
    // Disposal flag
    private bool _disposed = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="FFmpegFilterGraphComposer"/> class.
    /// </summary>
    public FFmpegFilterGraphComposer()
    {
        _filterService = new FFmpegFilterService();
    }

    /// <summary>
    /// Creates a contact sheet from extracted frames using FFmpeg filter graphs.
    /// </summary>
    /// <param name="frames">List of frames with their timestamps.</param>
    /// <param name="headerInfo">Information for the header.</param>
    /// <param name="options">Thumbnail options.</param>
    /// <returns>The composed contact sheet image.</returns>
    public RgbaImage CreateContactSheet(
        List<(RgbaImage Image, TimeSpan Timestamp)> frames,
        HeaderInfo headerInfo,
        ThumbnailOptions options)
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("No frames provided for contact sheet");
        }

        // TODO: Fully migrate to FFmpeg filter graphs for the entire contact sheet composition
        // For now, we'll implement a hybrid approach:
        // 1. Use FFmpeg filter graphs for individual frame processing (resize, text, filters)
        // 2. Use basic composition for the final layout
        // This allows incremental migration while maintaining functionality

        var processedFrames = new List<(RgbaImage Image, TimeSpan Timestamp)>();

        try
        {
            // Process each frame through FFmpeg filters
            for (int i = 0; i < frames.Count; i++)
            {
                var (frame, timestamp) = frames[i];
                var isMiddleFrame = i == (frames.Count - 1) / 2;
                var processedFrame = ProcessFrameWithFilters(frame, timestamp, options, isMiddleFrame);
                processedFrames.Add((processedFrame, timestamp));
            }

            // Compose the final contact sheet
            return ComposeContactSheet(processedFrames, headerInfo, options);
        }
        finally
        {
            // Clean up processed frames
            foreach (var (frame, _) in processedFrames)
            {
                frame?.Dispose();
            }
        }
    }

    /// <summary>
    /// Processes a single frame through FFmpeg filters (resize, timestamp, borders, watermarks).
    /// </summary>
    /// <param name="frame">The input frame to process.</param>
    /// <param name="timestamp">The timestamp of the frame.</param>
    /// <param name="options">Thumbnail options.</param>
    /// <param name="isMiddleFrame">Indicates if this is the middle frame (for special watermarking).</param>
    /// <returns>The processed frame image.</returns>
    private RgbaImage ProcessFrameWithFilters(
        RgbaImage frame,
        TimeSpan timestamp,
        ThumbnailOptions options,
        bool isMiddleFrame = false) // TODO: find way to use this
    {
        AVFilterGraph* filterGraph = null;
        AVFilterContext* bufferSrcCtx = null;
        AVFilterContext* bufferSinkCtx = null;
        AVFrame* inputFrame = null;
        AVFrame* outputFrame = null;

        try
        {
            // Convert ImageSharp to AVFrame
            inputFrame = AVFrameBridge.ToAVFrame(frame);

            // Create filter graph
            filterGraph = ffmpeg.avfilter_graph_alloc();
            if (filterGraph == null)
            {
                throw new InvalidOperationException("Failed to allocate filter graph");
            }

            // Build filter chain: buffer -> scale -> drawtext -> buffersink
            var filterSpec = BuildFrameFilterSpec(frame.Width, frame.Height, timestamp, options);

            if (!CreateFilterGraph(filterGraph, &bufferSrcCtx, &bufferSinkCtx, frame.Width, frame.Height, filterSpec))
            {
                Console.WriteLine("Failed to create filter graph, using original frame");
                return frame.Clone();
            }

            // Push frame through filter graph
            ffmpeg.av_buffersrc_add_frame_flags(bufferSrcCtx, inputFrame, 0).ThrowExceptionIfError();

            // Pull filtered frame
            outputFrame = ffmpeg.av_frame_alloc();
            var ret = ffmpeg.av_buffersink_get_frame(bufferSinkCtx, outputFrame);

            if (ret < 0)
            {
                Console.WriteLine($"Failed to get frame from filter: {ret}");
                return frame.Clone();
            }

            // Convert back to ImageSharp
            var processedFrame = AVFrameBridge.ToRgbaImage(outputFrame);

            // Apply image filters if specified
            if (!string.IsNullOrEmpty(options.Filter) && !options.Filter.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                var filteredFrame = _filterService.ApplyFilters(processedFrame, options.Filter);
                if (filteredFrame != processedFrame)
                {
                    processedFrame.Dispose();
                    processedFrame = filteredFrame;
                }
            }

            return processedFrame;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing frame with filters: {ex.Message}");
            return frame.Clone();
        }
        finally
        {
            if (outputFrame != null)
            {
                var pFrame = outputFrame;
                ffmpeg.av_frame_free(&pFrame);
            }

            if (inputFrame != null)
            {
                var pFrame = inputFrame;
                ffmpeg.av_frame_free(&pFrame);
            }

            if (filterGraph != null)
            {
                var pGraph = filterGraph;
                ffmpeg.avfilter_graph_free(&pGraph);
            }
        }
    }

    /// <summary>
    /// Builds the filter specification string for frame processing.
    /// </summary>
    /// <param name="width">Original frame width.</param>
    /// <param name="height">Original frame height.</param>
    /// <param name="timestamp">Timestamp of the frame.</param>
    /// <param name="options">Thumbnail options.</param>
    /// <returns>The filter specification string.</returns>
    private static string BuildFrameFilterSpec(int width, int height, TimeSpan timestamp, ThumbnailOptions options)
    {
        var filters = new List<string>();

        // Add v360 filter if enabled (applies 360-to-flat transformation with built-in resizing)
        if (options.V360)
        {
            // v360 filter handles both projection conversion and resizing in one step
            filters.Add("v360=input=hequirect:output=flat:in_stereo=sbs:out_stereo=2d:d_fov=125:w=400:h=300:pitch=-25");
            // Convert from YUV (v360 output) to RGBA (expected by AVFrameToImage)
            filters.Add("format=pix_fmts=rgba");
        }
        else
        {
            // Calculate target dimensions for regular scaling
            var thumbnailWidth = options.Width;
            var thumbnailHeight = options.Height > 0
                ? options.Height
                : (int)(height * (thumbnailWidth / (double)width));

            // Scale filter (only when not using v360, as v360 includes resizing)
            filters.Add($"scale={thumbnailWidth}:{thumbnailHeight}");
        }

        // Add timestamp if enabled
        if (!options.DisableTimestamps)
        {
            var timestampText = FormatTimestamp(timestamp);
            var fontFile = FindFontFile(options.FontPath ?? "DroidSans");
            var fontSize = options.FontSize;
            var opacity = options.TimestampOpacity;

            // Position: bottom-left with padding
            var textPaddingX = 5;
            var textPaddingY = 5;

            if (fontFile != null)
            {
                // Escape special characters in font path and text
                var fontPath = fontFile.Replace("\\", "/").Replace(":", "\\\\:").Replace(" ", "\\ ").Replace("[", "\\[").Replace("]", "\\]").Replace(",", "\\,");
                var escapedText = EscapeFilterText(timestampText);

                // Use drawtext's built-in box feature for perfect alignment
                // box=1 enables background box, boxcolor sets the color, boxborderw sets padding
                // Position text at bottom-left with padding
                filters.Add($"drawtext=fontfile={fontPath}:text='{escapedText}':fontcolor=white@{opacity}:fontsize={fontSize}:x={textPaddingX}:y=h-th-{textPaddingY}:box=1:boxcolor=black@0.7:boxborderw=5");
            }
        }

        // Add border if specified
        if (options.Border > 0)
        {
            filters.Add($"drawbox=x=0:y=0:w=iw:h=ih:color=white:t={options.Border}");
        }

        return string.Join(",", filters);
    }

    /// <summary>
    /// Creates and configures a filter graph from a filter specification.
    /// </summary>
    /// <param name="filterGraph">The filter graph to configure.</param>
    /// <param name="bufferSrcCtx">Pointer to the buffer source context.</param>
    /// <param name="bufferSinkCtx">Pointer to the buffer sink context.</param>
    /// <param name="width">Width of the input frames.</param>
    /// <param name="height">Height of the input frames.</param>
    /// <param name="filterSpec">The filter specification string.</param>
    /// <returns>True if successful, false otherwise.</returns>
    private bool CreateFilterGraph(
        AVFilterGraph* filterGraph,
        AVFilterContext** bufferSrcCtx,
        AVFilterContext** bufferSinkCtx,
        int width,
        int height,
        string filterSpec)
    {
        try
        {
            // Get buffer source and sink filters
            var bufferSrc = ffmpeg.avfilter_get_by_name("buffer");
            var bufferSink = ffmpeg.avfilter_get_by_name("buffersink");

            if (bufferSrc == null || bufferSink == null)
            {
                Console.WriteLine("Failed to find buffer filters");
                return false;
            }

            // Create buffer source
            var args = $"video_size={width}x{height}:pix_fmt={(int)AVPixelFormat.AV_PIX_FMT_RGBA}:time_base=1/1000";
            var ret = ffmpeg.avfilter_graph_create_filter(bufferSrcCtx, bufferSrc, "in", args, null, filterGraph);
            if (ret < 0)
            {
                Console.WriteLine($"Failed to create buffer source: {ret}");
                return false;
            }

            // Create buffer sink
            ret = ffmpeg.avfilter_graph_create_filter(bufferSinkCtx, bufferSink, "out", null, null, filterGraph);
            if (ret < 0)
            {
                Console.WriteLine($"Failed to create buffer sink: {ret}");
                return false;
            }

            // Parse and configure the filter chain
            AVFilterInOut* outputs = ffmpeg.avfilter_inout_alloc();
            AVFilterInOut* inputs = ffmpeg.avfilter_inout_alloc();

            outputs->name = ffmpeg.av_strdup("in");
            outputs->filter_ctx = *bufferSrcCtx;
            outputs->pad_idx = 0;
            outputs->next = null;

            inputs->name = ffmpeg.av_strdup("out");
            inputs->filter_ctx = *bufferSinkCtx;
            inputs->pad_idx = 0;
            inputs->next = null;

            ret = ffmpeg.avfilter_graph_parse_ptr(filterGraph, filterSpec, &inputs, &outputs, null);

            ffmpeg.avfilter_inout_free(&inputs);
            ffmpeg.avfilter_inout_free(&outputs);

            if (ret < 0)
            {
                Console.WriteLine($"Failed to parse filter graph: {ret}");
                return false;
            }

            // Configure the graph
            ret = ffmpeg.avfilter_graph_config(filterGraph, null);
            if (ret < 0)
            {
                Console.WriteLine($"Failed to configure filter graph: {ret}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception in CreateFilterGraph: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Formats a timestamp as HH:MM:SS.
    /// </summary>
    /// <param name="timestamp">The timestamp to format.</param>
    /// <returns>The formatted timestamp string.</returns>
    private static string FormatTimestamp(TimeSpan timestamp)
    {
        // TODO: Figure out timecode formatting
        return $"{(int)timestamp.TotalHours:D2}:{timestamp.Minutes:D2}:{timestamp.Seconds:D2}";
    }

    /// <summary>
    /// Composes processed frames into a contact sheet with header.
    /// </summary>
    /// <param name="frames">List of processed frames with timestamps.</param>
    /// <param name="headerInfo">Information for the header.</param>
    /// <param name="options">Thumbnail options.</param>
    /// <returns>The composed contact sheet image.</returns>
    private RgbaImage ComposeContactSheet(
        List<(RgbaImage Image, TimeSpan Timestamp)> frames,
        HeaderInfo headerInfo,
        ThumbnailOptions options)
    {
        // Calculate dimensions
        var thumbnailWidth = options.Width;
        var thumbnailHeight = options.Height > 0
            ? options.Height
            : (int)(frames[0].Image.Height * (thumbnailWidth / (double)frames[0].Image.Width));

        var columns = options.Columns;
        var rows = (int)Math.Ceiling(frames.Count / (double)columns);

        var padding = options.Padding;

        // Calculate canvas dimensions
        var contentWidth = (columns * thumbnailWidth) + ((columns + 1) * padding);
        var contentHeight = (rows * thumbnailHeight) + ((rows + 1) * padding);

        // Calculate header height
        var headerHeight = 0;
        RgbaImage? headerImage = null;
        if (options.Header)
        {
            headerImage = CreateHeaderWithFFmpeg(headerInfo, options, contentWidth);
            headerHeight = headerImage?.Height ?? 0;
        }

        var totalHeight = headerHeight + contentHeight;

        // Create canvas
        var bgColor = ColorParser.ParseRgb(options.BgContent);
        var canvas = new RgbaImage(contentWidth, totalHeight);
        canvas.Fill(bgColor);

        // Draw header if created
        if (headerImage != null)
        {
            canvas.DrawImage(headerImage, 0, 0);
            headerImage.Dispose();
        }

        // Compose thumbnails onto canvas
        for (int i = 0; i < frames.Count; i++)
        {
            var (frame, _) = frames[i];
            var row = i / columns;
            var col = i % columns;

            var x = padding + (col * (thumbnailWidth + padding));
            var y = headerHeight + padding + (row * (thumbnailHeight + padding));

            canvas.DrawImage(frame, x, y);
        }

        return canvas;
    }

    /// <summary>
    /// Creates a header image using FFmpeg drawtext filters.
    /// </summary>
    /// <param name="headerInfo">Information for the header.</param>
    /// <param name="options">Thumbnail options.</param>
    /// <param name="width">Width of the header.</param>
    /// <returns>The created header image, or null if creation failed.</returns>
    private RgbaImage? CreateHeaderWithFFmpeg(HeaderInfo headerInfo, ThumbnailOptions options, int width)
    {
        try
        {
            var height = CalculateHeaderHeight(headerInfo, options);
            var bgColor = ColorParser.ParseRgb(options.BgHeader);
            var fgColor = ColorParser.ParseRgb(options.FgHeader);

            // Create blank header canvas
            var header = new RgbaImage(width, height);
            header.Fill(bgColor);

            // Build header text lines
            var headerLines = BuildHeaderTextLines(headerInfo, options);

            // Convert to AVFrame and apply text using drawtext filter
            var frame = AVFrameBridge.ToAVFrame(header);
            header.Dispose();

            try
            {
                var filterSpec = BuildHeaderFilterSpec(headerLines, options, fgColor);
                
                var processedFrame = ApplyFilterToFrame(frame, width, height, filterSpec);

                if (processedFrame != null)
                {
                    return AVFrameBridge.ToRgbaImage(processedFrame);
                }
            }
            finally
            {
                var pFrame = frame;
                ffmpeg.av_frame_free(&pFrame);
            }

            // Fallback if filter fails
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating header with FFmpeg: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Builds the header text lines.
    /// </summary>
    /// <param name="headerInfo">Information for the header.</param>
    /// <param name="options">Thumbnail options.</param>
    /// <returns>List of header text lines.</returns>
    private static List<string> BuildHeaderTextLines(HeaderInfo headerInfo, ThumbnailOptions options)
    {
        var lines = new List<string>
        {
            $"File Name: {headerInfo.Filename}",
            $"File Size: {FormatFileSize(headerInfo.FileSize)}",
            $"Duration: {FormatDurationForHeader(headerInfo.Duration)}",
            $"Resolution: {headerInfo.Width}x{headerInfo.Height}"
        };

        if (options.HeaderMeta)
        {
            lines.Add($"FPS: {headerInfo.FrameRate:F2}, Bitrate: {headerInfo.BitRate / 1000} kbps");
            lines.Add($"Codec: {headerInfo.VideoCodec} / {headerInfo.AudioCodec}");
        }

        if (!string.IsNullOrEmpty(options.Comment))
        {
            lines.Add(options.Comment);
        }

        return lines;
    }

    /// <summary>
    /// Builds filter specification for drawing header text.
    /// </summary>
    /// <param name="lines">Header text lines.</param>
    /// <param name="options">Thumbnail options.</param>
    /// <param name="fgColor">Foreground color for text.</param>
    /// <returns>Filter specification string.</returns>
    private static string BuildHeaderFilterSpec(List<string> lines, ThumbnailOptions options, RgbaColor fgColor)
    {
        var fontFile = FindFontFile(options.FontPath ?? "DroidSans");
        if (fontFile == null)
        {
            return ""; // No filter if no font found
        }

        var filters = new List<string>();

        // Original Go mt uses 96 DPI for header text, but FFmpeg drawtext defaults to 72 DPI
        // So we need to scale: 96/72 = 1.333 (or 4/3)
        var baseFontSize = options.FontSize;
        var headerFontSize = (int)(baseFontSize * 96.0 / 72.0); // Scale to match 96 DPI

        // Line height also needs 96 DPI scaling: (fontSize + 4) * 96/72
        // Original Go: PointToFix32(fontSize+4) at 96 DPI
        var lineHeightPixels = (int)((baseFontSize + 4) * 96.0 / 72.0);

        var fontPath = fontFile.Replace("\\", "/").Replace(":", "\\\\:").Replace(" ", "\\ ").Replace("[", "\\[").Replace("]", "\\]").Replace(",", "\\,");

        // Convert color to hex
        var colorHex = fgColor.ToFilterHex();

        // Add drawtext filter for each line
        // Balanced positioning: first line at y=10 to match x=10
        for (int i = 0; i < lines.Count; i++)
        {
            var escapedText = EscapeFilterText(lines[i]);
            // First line at y=10, subsequent lines spaced by lineHeight
            var y = 10 + (lineHeightPixels * i);

            filters.Add($"drawtext=fontfile={fontPath}:text='{escapedText}':fontcolor={colorHex}:fontsize={headerFontSize}:x=10:y={y}");
        }

        return string.Join(",", filters);
    }

    /// <summary>
    /// Escapes text for use in FFmpeg filter strings.
    /// </summary>
    /// <param name="text">The text to escape.</param>
    /// <returns>Escaped text.</returns>
    private static string EscapeFilterText(string text)
    {
        // FFmpeg drawtext filter requires escaping special characters
        // DO NOT add quotes - just escape the special chars
        return text
            .Replace("\\", "\\\\")  // Escape backslash first
            .Replace(":", "\\:")     // Escape colons in timestamps
            .Replace("'", "\\'")     // Escape single quotes
            .Replace(",", "\\,")     // Escape commas
            .Replace("[", "\\[")     // Escape brackets
            .Replace("]", "\\]")
            .Replace(";", "\\;")     // Escape semicolons
            .Replace("=", "\\=");    // Escape equals
                                     // Note: Don't escape spaces in text - they're fine
    }

    /// <summary>
    /// Applies a filter specification to an AVFrame.
    /// </summary>
    /// <param name="inputFrame">The input AVFrame to filter.</param>
    /// <param name="width">Width of the frame.</param>
    /// <param name="height">Height of the frame.</param>
    /// <param name="filterSpec">The filter specification string.</param>
    /// <returns>The filtered AVFrame, or null if filtering failed.</returns>
    private AVFrame* ApplyFilterToFrame(AVFrame* inputFrame, int width, int height, string filterSpec)
    {
        if (string.IsNullOrEmpty(filterSpec))
        {
            return null;
        }

        AVFilterGraph* filterGraph = null;
        AVFilterContext* bufferSrcCtx = null;
        AVFilterContext* bufferSinkCtx = null;
        AVFrame* outputFrame = null;

        try
        {
            filterGraph = ffmpeg.avfilter_graph_alloc();
            if (filterGraph == null || !CreateFilterGraph(filterGraph, &bufferSrcCtx, &bufferSinkCtx, width, height, filterSpec))
            {
                return null;
            }

            ffmpeg.av_buffersrc_add_frame_flags(bufferSrcCtx, inputFrame, 0).ThrowExceptionIfError();

            outputFrame = ffmpeg.av_frame_alloc();
            var ret = ffmpeg.av_buffersink_get_frame(bufferSinkCtx, outputFrame);

            if (ret < 0)
            {
                var pFrame = outputFrame;
                ffmpeg.av_frame_free(&pFrame);
                return null;
            }

            return outputFrame;
        }
        finally
        {
            if (filterGraph != null)
            {
                var pGraph = filterGraph;
                ffmpeg.avfilter_graph_free(&pGraph);
            }
        }
    }

    /// <summary>
    /// Formats file size using binary units (KiB, MiB, GiB).
    /// </summary>
    /// <param name="bytes">File size in bytes.</param>
    /// <returns>Formatted file size string.</returns>
    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KiB", "MiB", "GiB", "TiB"];
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return order >= 3 ? $"{len:0.0} {sizes[order]}" : $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Formats duration as HH:MM:SS for header display.
    /// </summary>
    /// <param name="duration">The duration to format.</param>
    /// <returns>The formatted duration string.</returns>
    private static string FormatDurationForHeader(TimeSpan duration)
    {
        // TODO: Figure out timecode formatting
        return $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
    }

    /// <summary>
    /// Calculates the required header height based on content.
    /// Balanced padding: 10px top + (lineHeight * numLines) + 10px bottom
    /// Must account for DPI-scaled line height and font size.
    /// </summary>
    /// <param name="headerInfo">Information for the header.</param>
    /// <param name="options">Thumbnail options.</param>
    /// <returns>The calculated header height in pixels.</returns>
    private static int CalculateHeaderHeight(HeaderInfo headerInfo, ThumbnailOptions options)
    {
        var fontSize = options.FontSize;

        // Use DPI-scaled line height to match actual rendering
        var lineHeight = (int)((fontSize + 4) * 96.0 / 72.0);

        // TODO: Calculate based on headerInfo content
        // For now, assume fixed number of lines based on options
        var lines = 4; // File Name, File Size, Duration, Resolution

        if (options.HeaderMeta)
        {
            lines += 2; // FPS/Bitrate and Codec lines
        }

        if (!string.IsNullOrEmpty(options.Comment))
        {
            lines += 1; // Comment line
        }

        return lineHeight * lines + 5;
    }

    /// <summary>
    /// Finds a suitable font file for drawtext filter.
    /// </summary>
    /// <param name="fontName">The font name or path.</param>
    /// <returns>The path to the font file, or null if not found.</returns>
    private static string? FindFontFile(string fontName)
    {
        // Common font directories on different platforms
        var fontDirs = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            fontDirs.Add(@"C:\Windows\Fonts");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            fontDirs.Add("/Library/Fonts");
            fontDirs.Add("/System/Library/Fonts");
            fontDirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Fonts"));
        }
        else // Linux
        {
            fontDirs.Add("/usr/share/fonts");
            fontDirs.Add("/usr/local/share/fonts");
            fontDirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts"));
        }

        // Search for font file
        foreach (var dir in fontDirs)
        {
            if (!Directory.Exists(dir)) continue;

            var fontFiles = Directory.GetFiles(dir, $"*{fontName}*.ttf", SearchOption.AllDirectories);
            if (fontFiles.Length > 0)
            {
                return fontFiles[0];
            }
        }

        // Try common font names as fallback
        var fallbackFonts = new[] { "DroidSans.ttf", "Roboto-Regular.ttf", "Arial.ttf", "DejaVuSans.ttf", "LiberationSans-Regular.ttf" };
        foreach (var dir in fontDirs)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var fallback in fallbackFonts)
            {
                var fontFiles = Directory.GetFiles(dir, fallback, SearchOption.AllDirectories);
                if (fontFiles.Length > 0)
                {
                    return fontFiles[0];
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Disposes the resources used by the <see cref="FFmpegFilterGraphComposer"/> class.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _filterService?.Dispose();
    }
}