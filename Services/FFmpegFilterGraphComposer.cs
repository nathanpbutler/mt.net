using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
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
    private readonly FFmpegFilterService _filterService;
    private bool _disposed = false;

    public FFmpegFilterGraphComposer()
    {
        _filterService = new FFmpegFilterService();
    }

    /// <summary>
    /// Creates a contact sheet from extracted frames using FFmpeg filter graphs.
    /// </summary>
    public Image<Rgba32> CreateContactSheet(
        List<(Image<Rgba32> Image, TimeSpan Timestamp)> frames,
        HeaderInfo headerInfo,
        ThumbnailOptions options)
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("No frames provided for contact sheet");
        }

        // For now, we'll implement a hybrid approach:
        // 1. Use FFmpeg filter graphs for individual frame processing (resize, text, filters)
        // 2. Use basic composition for the final layout
        // This allows incremental migration while maintaining functionality

        var processedFrames = new List<(Image<Rgba32> Image, TimeSpan Timestamp)>();

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
    private Image<Rgba32> ProcessFrameWithFilters(
        Image<Rgba32> frame,
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
            inputFrame = ImageToAVFrame(frame);

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
            var processedFrame = AVFrameToImage(outputFrame);

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
    private static string BuildFrameFilterSpec(int width, int height, TimeSpan timestamp, ThumbnailOptions options)
    {
        var filters = new List<string>();

        // Calculate target dimensions
        var thumbnailWidth = options.Width;
        var thumbnailHeight = options.Height > 0
            ? options.Height
            : (int)(height * (thumbnailWidth / (double)width));

        // Scale filter
        filters.Add($"scale={thumbnailWidth}:{thumbnailHeight}");

        // Add timestamp if enabled
        if (!options.DisableTimestamps)
        {
            var timestampText = FormatTimestamp(timestamp);
            var fontFile = FindFontFile(options.FontPath ?? "Arial");
            var fontSize = options.FontSize;
            var opacity = options.TimestampOpacity;

            // Position: bottom-left with padding
            var x = 5;
            var y = $"h-{fontSize}-5"; // bottom position

            if (fontFile != null)
            {
                // Escape special characters in font path and text
                var escapedFont = fontFile.Replace(":", "\\:").Replace("\\", "\\\\");
                var escapedText = timestampText.Replace(":", "\\:");

                // Add semi-transparent background box
                filters.Add($"drawbox=x=0:y=h-{fontSize + 10}:w=iw:h={fontSize + 10}:color=black@0.7:t=fill");

                // Add text overlay
                filters.Add($"drawtext=fontfile='{escapedFont}':text='{escapedText}':fontcolor=white@{opacity}:fontsize={fontSize}:x={x}:y={y}");
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
            var args = $"video_size={width}x{height}:pix_fmt={((int)AVPixelFormat.AV_PIX_FMT_RGBA)}:time_base=1/1000";
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
    private static string FormatTimestamp(TimeSpan timestamp)
    {
        return $"{(int)timestamp.TotalHours:D2}:{timestamp.Minutes:D2}:{timestamp.Seconds:D2}";
    }

    /// <summary>
    /// Composes processed frames into a contact sheet with header.
    /// </summary>
    private Image<Rgba32> ComposeContactSheet(
        List<(Image<Rgba32> Image, TimeSpan Timestamp)> frames,
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
        Image<Rgba32>? headerImage = null;
        if (options.Header)
        {
            headerImage = CreateHeaderWithFFmpeg(headerInfo, options, contentWidth);
            headerHeight = headerImage?.Height ?? 0;
        }

        var totalHeight = headerHeight + contentHeight;

        // Create canvas
        var bgColor = ColorParser.ParseRgb(options.BgContent);
        var canvas = new Image<Rgba32>(contentWidth, totalHeight);
        canvas.Mutate(ctx => ctx.Fill(bgColor));

        // Draw header if created
        if (headerImage != null)
        {
            canvas.Mutate(ctx => ctx.DrawImage(headerImage, new Point(0, 0), 1f));
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

            canvas.Mutate(ctx => ctx.DrawImage(frame, new Point(x, y), 1f));
        }

        return canvas;
    }

    /// <summary>
    /// Creates a header image using FFmpeg drawtext filters.
    /// </summary>
    private Image<Rgba32>? CreateHeaderWithFFmpeg(HeaderInfo headerInfo, ThumbnailOptions options, int width)
    {
        try
        {
            var height = CalculateHeaderHeight(headerInfo, options);
            var bgColor = ColorParser.ParseRgb(options.BgHeader);
            var fgColor = ColorParser.ParseRgb(options.FgHeader);

            // Create blank header canvas
            var header = new Image<Rgba32>(width, height);
            header.Mutate(ctx => ctx.Fill(bgColor));

            // Build header text lines
            var headerLines = BuildHeaderTextLines(headerInfo, options);

            // Convert to AVFrame and apply text using drawtext filter
            var frame = ImageToAVFrame(header);
            header.Dispose();

            try
            {
                var filterSpec = BuildHeaderFilterSpec(headerLines, options, fgColor);
                var processedFrame = ApplyFilterToFrame(frame, width, height, filterSpec);

                if (processedFrame != null)
                {
                    return AVFrameToImage(processedFrame);
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
    private static List<string> BuildHeaderTextLines(HeaderInfo headerInfo, ThumbnailOptions options)
    {
        var lines = new List<string>();

        lines.Add($"File Name: {headerInfo.Filename}");
        lines.Add($"File Size: {FormatFileSize(headerInfo.FileSize)}");
        lines.Add($"Duration: {FormatDurationForHeader(headerInfo.Duration)}");
        lines.Add($"Resolution: {headerInfo.Width}x{headerInfo.Height}");

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
    private static string BuildHeaderFilterSpec(List<string> lines, ThumbnailOptions options, Rgba32 fgColor)
    {
        var fontFile = FindFontFile(options.FontPath ?? "Arial");
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

        var escapedFont = fontFile.Replace(":", "\\:").Replace("\\", "\\\\");

        // Convert color to hex
        var colorHex = $"0x{fgColor.R:X2}{fgColor.G:X2}{fgColor.B:X2}";

        // Add drawtext filter for each line
        // Balanced positioning: first line at y=10 to match x=10
        for (int i = 0; i < lines.Count; i++)
        {
            var escapedText = EscapeFilterText(lines[i]);
            // First line at y=10, subsequent lines spaced by lineHeight
            var y = 10 + (lineHeightPixels * i);

            filters.Add($"drawtext=fontfile='{escapedFont}':text='{escapedText}':fontcolor={colorHex}:fontsize={headerFontSize}:x=10:y={y}");
        }

        return string.Join(",", filters);
    }

    /// <summary>
    /// Escapes text for use in FFmpeg filter strings.
    /// </summary>
    private static string EscapeFilterText(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace(":", "\\:")
            .Replace("[", "\\[")
            .Replace("]", "\\]");
    }

    /// <summary>
    /// Applies a filter specification to an AVFrame.
    /// </summary>
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
    private static string FormatDurationForHeader(TimeSpan duration)
    {
        return $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
    }

    /// <summary>
    /// Creates a blank canvas with specified background color using FFmpeg.
    /// </summary>
    private static Image<Rgba32> CreateCanvasWithFFmpeg(int width, int height, string bgColorString)
    {
        // TODO: Implement using FFmpeg color source filter
        // For now, create a basic image
        var bgColor = ColorParser.ParseRgb(bgColorString);
        var canvas = new Image<Rgba32>(width, height);
        canvas.Mutate(ctx => ctx.Fill(bgColor));
        return canvas;
    }

    /// <summary>
    /// Calculates the required header height based on content.
    /// Balanced padding: 10px top + (lineHeight * numLines) + 10px bottom
    /// Must account for DPI-scaled line height and font size.
    /// </summary>
    private static int CalculateHeaderHeight(HeaderInfo headerInfo, ThumbnailOptions options)
    {
        var fontSize = options.FontSize;

        // Use DPI-scaled line height to match actual rendering
        var lineHeight = (int)((fontSize + 4) * 96.0 / 72.0);

        var lines = 4; // File Name, File Size, Duration, Resolution

        if (options.HeaderMeta)
        {
            lines += 2; // FPS/Bitrate and Codec lines
        }

        if (!string.IsNullOrEmpty(options.Comment))
        {
            lines += 1; // Comment line
        }

        // Balanced padding: 10px top + (lineHeight * lines) + 10px bottom
        return 10 + (lineHeight * lines) + 10;
    }

    /// <summary>
    /// Converts an ImageSharp Image to FFmpeg AVFrame.
    /// </summary>
    private AVFrame* ImageToAVFrame(Image<Rgba32> image)
    {
        var frame = ffmpeg.av_frame_alloc();
        if (frame == null)
        {
            throw new InvalidOperationException("Failed to allocate AVFrame");
        }

        frame->width = image.Width;
        frame->height = image.Height;
        frame->format = (int)AVPixelFormat.AV_PIX_FMT_RGBA;

        // Allocate buffer for frame
        var bufferSize = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_RGBA, image.Width, image.Height, 1);
        var buffer = (byte*)ffmpeg.av_malloc((ulong)bufferSize);

        // Fill frame with image data
        var dstData = new byte_ptr4();
        var dstLinesize = new int4();
        ffmpeg.av_image_fill_arrays(
            ref dstData,
            ref dstLinesize,
            buffer,
            AVPixelFormat.AV_PIX_FMT_RGBA,
            image.Width,
            image.Height,
            1);

        // Copy pointers to frame's byte_ptr8 and int8 arrays
        frame->data[0] = dstData[0];
        frame->data[1] = dstData[1];
        frame->data[2] = dstData[2];
        frame->data[3] = dstData[3];

        frame->linesize[0] = dstLinesize[0];
        frame->linesize[1] = dstLinesize[1];
        frame->linesize[2] = dstLinesize[2];
        frame->linesize[3] = dstLinesize[3];

        // Copy pixel data from ImageSharp to AVFrame
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < image.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var destRow = buffer + (y * dstLinesize[0]);

                for (int x = 0; x < image.Width; x++)
                {
                    var pixel = row[x];
                    destRow[x * 4 + 0] = pixel.R;
                    destRow[x * 4 + 1] = pixel.G;
                    destRow[x * 4 + 2] = pixel.B;
                    destRow[x * 4 + 3] = pixel.A;
                }
            }
        });

        return frame;
    }

    /// <summary>
    /// Converts an FFmpeg AVFrame to ImageSharp Image.
    /// </summary>
    private Image<Rgba32> AVFrameToImage(AVFrame* frame)
    {
        var image = new Image<Rgba32>(frame->width, frame->height);

        var srcData = frame->data[0];
        var stride = frame->linesize[0];

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < frame->height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var srcRow = srcData + (y * stride);

                for (int x = 0; x < frame->width; x++)
                {
                    var pixelPtr = srcRow + (x * 4);
                    row[x] = new Rgba32(
                        pixelPtr[0],  // R
                        pixelPtr[1],  // G
                        pixelPtr[2],  // B
                        pixelPtr[3]   // A
                    );
                }
            }
        });

        return image;
    }

    /// <summary>
    /// Finds a suitable font file for drawtext filter.
    /// </summary>
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
        var fallbackFonts = new[] { "Arial.ttf", "DejaVuSans.ttf", "LiberationSans-Regular.ttf" };
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _filterService?.Dispose();
    }
}
