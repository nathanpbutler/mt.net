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
    /// <summary>Inset used for the header's text and for the right-aligned header image.</summary>
    private const int HeaderMargin = 10;

    /// <summary>
    /// Resolved font paths, keyed by the requested font name.
    /// </summary>
    /// <remarks>
    /// <see cref="FindFontFile"/> recursively scans the system font directories, and it used to
    /// run once per frame *and* once per header line. Resolving a font does not change while the
    /// process runs, so cache it.
    /// </remarks>
    private static readonly Dictionary<string, string?> FontCache = [];

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
    /// Runs every frame through the rendering pipeline: v360 or scale, image filters, timestamp,
    /// border and watermarks.
    /// </summary>
    /// <param name="frames">Source frames with their timestamps.</param>
    /// <param name="options">Thumbnail options.</param>
    /// <returns>Newly allocated processed frames. The caller owns and must dispose them.</returns>
    /// <remarks>
    /// Public because <c>--single-images</c> needs it too. Until v3 this loop was buried inside
    /// <see cref="CreateContactSheet"/>, so <c>-s</c> wrote raw full-resolution frames and
    /// silently ignored <c>--filter</c>, <c>--width</c>, <c>--height</c>, <c>--border</c>,
    /// <c>--font-size</c>, <c>--timestamp-opacity</c> and <c>--disable-timestamps</c>.
    /// </remarks>
    public List<(RgbaImage Image, TimeSpan Timestamp)> ProcessFrames(
        List<(RgbaImage Image, TimeSpan Timestamp)> frames,
        ThumbnailOptions options)
    {
        var processedFrames = new List<(RgbaImage Image, TimeSpan Timestamp)>();

        try
        {
            for (int i = 0; i < frames.Count; i++)
            {
                var (frame, timestamp) = frames[i];
                var isMiddleFrame = i == (frames.Count - 1) / 2;
                processedFrames.Add((ProcessFrameWithFilters(frame, timestamp, options, isMiddleFrame), timestamp));
            }

            return processedFrames;
        }
        catch
        {
            // Don't leak the frames processed before the failure.
            foreach (var (frame, _) in processedFrames)
            {
                frame?.Dispose();
            }

            throw;
        }
    }

    /// <summary>
    /// Creates a contact sheet from extracted frames using FFmpeg filter graphs.
    /// </summary>
    /// <param name="frames">List of frames with their timestamps.</param>
    /// <param name="headerInfo">Information for the header.</param>
    /// <param name="options">Thumbnail options.</param>
    /// <returns>
    /// The composed sheet and the geometry it was composed with. The layout is what WebVTT
    /// generation reads, so its offsets always describe the image that was actually written.
    /// </returns>
    public (RgbaImage Sheet, SheetLayout Layout) CreateContactSheet(
        List<(RgbaImage Image, TimeSpan Timestamp)> frames,
        HeaderInfo headerInfo,
        ThumbnailOptions options)
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("No frames provided for contact sheet");
        }

        var processedFrames = ProcessFrames(frames, options);

        try
        {
            return ComposeContactSheet(processedFrames, headerInfo, options);
        }
        finally
        {
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
    /// <param name="isMiddleFrame">
    /// True for the centre thumbnail, which is where <c>--watermark</c> lands.
    /// </param>
    /// <returns>The processed frame image.</returns>
    private RgbaImage ProcessFrameWithFilters(
        RgbaImage frame,
        TimeSpan timestamp,
        ThumbnailOptions options,
        bool isMiddleFrame = false)
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
                ConsoleOutput.Error("Failed to build the frame filter graph; using the unprocessed frame.");
                return frame.Clone();
            }

            // Push frame through filter graph
            ffmpeg.av_buffersrc_add_frame_flags(bufferSrcCtx, inputFrame, 0).ThrowExceptionIfError();

            // Pull filtered frame
            outputFrame = ffmpeg.av_frame_alloc();
            var ret = ffmpeg.av_buffersink_get_frame(bufferSinkCtx, outputFrame);

            if (ret < 0)
            {
                ConsoleOutput.Error($"Filter graph produced no frame (error {ret}); using the unprocessed frame.");
                return frame.Clone();
            }

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

            ApplyWatermarks(processedFrame, options, isMiddleFrame);

            return processedFrame;
        }
        catch (Exception ex)
        {
            ConsoleOutput.Error($"Error processing frame with filters: {ex.Message}");
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
    /// Blends <c>--watermark-all</c> onto every thumbnail and <c>--watermark</c> onto the centre one.
    /// </summary>
    /// <remarks>
    /// Both were broken before v3. <c>--watermark-all</c> ran in <c>ProcessVideoAsync</c> against
    /// the *source* frames after the sheet had already been composed, so it never reached the
    /// output and the frames were disposed without being re-saved. <c>--watermark</c> was blended
    /// over the centre of the whole sheet rather than the centre thumbnail its help text promises.
    /// Doing both here, per frame and before composition, makes them behave as documented and
    /// carries them to <c>--single-images</c> as well.
    /// </remarks>
    private static void ApplyWatermarks(RgbaImage frame, ThumbnailOptions options, bool isMiddleFrame)
    {
        if (!string.IsNullOrEmpty(options.WatermarkAll))
        {
            WatermarkService.ApplyWatermark(frame, options.WatermarkAll, center: true);
        }

        if (isMiddleFrame && !string.IsNullOrEmpty(options.Watermark))
        {
            WatermarkService.ApplyWatermark(frame, options.Watermark, center: true);
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

        // v360 handles projection conversion and resizing in one step; plain scaling otherwise.
        // Either way the output honours --width/--height, which is new in v3: the v360 branch used
        // to hardcode w=400:h=300 while the grid was laid out from options.Width/Height, so any
        // --v360 run at a non-default size produced a misaligned sheet.
        var targetWidth = options.Width;
        int targetHeight;

        if (options.Height > 0)
        {
            targetHeight = options.Height;
        }
        else if (options.V360)
        {
            // A flat projection has no meaningful source aspect to inherit, so fall back to the
            // 4:3 that v2's hardcoded 400x300 produced. `mt vr.mp4 --v360` looks unchanged;
            // `--width 640` now actually widens the output instead of being ignored.
            targetHeight = targetWidth * 3 / 4;
        }
        else
        {
            targetHeight = (int)(height * (targetWidth / (double)width));
        }

        if (options.V360)
        {
            filters.Add(
                $"v360=input={options.V360Input}:output={options.V360Output}" +
                $":in_stereo={options.V360Stereo}:out_stereo=2d" +
                $":d_fov={options.V360Fov}:pitch={options.V360Pitch}" +
                $":w={targetWidth}:h={targetHeight}");

            // v360 emits YUV; AVFrameBridge expects RGBA.
            filters.Add("format=pix_fmts=rgba");
        }
        else
        {
            filters.Add($"scale={targetWidth}:{targetHeight}");
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
                ConsoleOutput.Verbose("Failed to find the buffer/buffersink filters.");
                return false;
            }

            // Create buffer source
            var args = $"video_size={width}x{height}:pix_fmt={(int)AVPixelFormat.AV_PIX_FMT_RGBA}:time_base=1/1000";
            var ret = ffmpeg.avfilter_graph_create_filter(bufferSrcCtx, bufferSrc, "in", args, null, filterGraph);
            if (ret < 0)
            {
                ConsoleOutput.Verbose($"Failed to create buffer source: {ret}");
                return false;
            }

            // Create buffer sink
            ret = ffmpeg.avfilter_graph_create_filter(bufferSinkCtx, bufferSink, "out", null, null, filterGraph);
            if (ret < 0)
            {
                ConsoleOutput.Verbose($"Failed to create buffer sink: {ret}");
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
                ConsoleOutput.Verbose($"Failed to parse filter graph: {ret}");
                return false;
            }

            // Configure the graph
            ret = ffmpeg.avfilter_graph_config(filterGraph, null);
            if (ret < 0)
            {
                ConsoleOutput.Verbose($"Failed to configure filter graph: {ret}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            ConsoleOutput.Verbose($"Exception in CreateFilterGraph: {ex.Message}");
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
    /// <returns>The composed contact sheet and the geometry used to build it.</returns>
    private (RgbaImage Sheet, SheetLayout Layout) ComposeContactSheet(
        List<(RgbaImage Image, TimeSpan Timestamp)> frames,
        HeaderInfo headerInfo,
        ThumbnailOptions options)
    {
        // Measure the frames that were actually produced rather than re-deriving from options.
        // --v360 and the aspect-preserving auto height both mean the rendered size can differ
        // from what options alone would suggest; taking it from the frame keeps the grid, the
        // canvas and the WebVTT offsets in agreement no matter what the filter chain did.
        var thumbnailWidth = frames[0].Image.Width;
        var thumbnailHeight = frames[0].Image.Height;

        var columns = Math.Min(options.Columns, frames.Count);
        var rows = (int)Math.Ceiling(frames.Count / (double)columns);

        // Header height is only known once the header is rendered, so build it first against the
        // content width, then let the real image height define the layout.
        var contentWidth = (columns * thumbnailWidth) + ((columns + 1) * options.Padding);

        RgbaImage? headerImage = null;
        if (options.Header)
        {
            headerImage = CreateHeaderWithFFmpeg(headerInfo, options, contentWidth);
        }

        var layout = new SheetLayout(
            HeaderHeight: headerImage?.Height ?? 0,
            ThumbnailWidth: thumbnailWidth,
            ThumbnailHeight: thumbnailHeight,
            Columns: columns,
            Rows: rows,
            Padding: options.Padding);

        var canvas = new RgbaImage(layout.ContentWidth, layout.TotalHeight);
        canvas.Fill(ColorParser.ParseRgb(options.BgContent));

        if (headerImage != null)
        {
            canvas.DrawImage(headerImage, 0, 0);
            headerImage.Dispose();
        }

        for (int i = 0; i < frames.Count; i++)
        {
            canvas.DrawImage(frames[i].Image, layout.ThumbnailX(i), layout.ThumbnailY(i));
        }

        return (canvas, layout);
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
        RgbaImage? headerImage = null;

        try
        {
            // --header-image was declared, bound to ThumbnailOptions.HeaderImage and then read by
            // nothing at all until v3. Load it first so the header can be made tall enough for it.
            if (!string.IsNullOrEmpty(options.HeaderImage))
            {
                headerImage = ImageLoader.Load(options.HeaderImage);
                if (headerImage == null)
                {
                    ConsoleOutput.Error($"Could not decode --header-image '{options.HeaderImage}'; continuing without it.");
                }
            }

            var height = CalculateHeaderHeight(options, headerImage?.Height ?? 0);
            var bgColor = ColorParser.ParseRgb(options.BgHeader);
            var fgColor = ColorParser.ParseRgb(options.FgHeader);

            var header = new RgbaImage(width, height);
            header.Fill(bgColor);

            // Right-align the header image so it never lands under the left-aligned text.
            if (headerImage != null)
            {
                var imageX = Math.Max(0, width - headerImage.Width - HeaderMargin);
                var imageY = Math.Max(0, (height - headerImage.Height) / 2);
                header.DrawImage(headerImage, imageX, imageY);
            }

            var headerLines = BuildHeaderTextLines(headerInfo, options);
            var filterSpec = BuildHeaderFilterSpec(headerLines, options, fgColor);

            // No font, or an FFmpeg without drawtext: keep the header band anyway so
            // --header-image and the background colour still appear.
            if (string.IsNullOrEmpty(filterSpec))
            {
                return header;
            }

            // Convert to AVFrame and apply text using drawtext filter
            var frame = AVFrameBridge.ToAVFrame(header);
            header.Dispose();

            try
            {
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
            ConsoleOutput.Error($"Error creating header with FFmpeg: {ex.Message}");
            return null;
        }
        finally
        {
            headerImage?.Dispose();
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
    /// Calculates the header height needed for the text lines, and for the header image if one
    /// is taller than the text.
    /// </summary>
    /// <param name="options">Thumbnail options.</param>
    /// <param name="headerImageHeight">Height of the <c>--header-image</c>, or 0 when absent.</param>
    /// <returns>The calculated header height in pixels.</returns>
    /// <remarks>
    /// This is the only place header height is computed. It used to be one of three competing
    /// formulas — <c>OutputService</c> had its own un-DPI-scaled variant for WebVTT, and the
    /// composer separately trusted the rendered image — which is what desynced the VTT offsets.
    /// Everything downstream now reads <see cref="SheetLayout.HeaderHeight"/> instead.
    /// </remarks>
    private static int CalculateHeaderHeight(ThumbnailOptions options, int headerImageHeight)
    {
        // Use DPI-scaled line height to match actual rendering (Go mt draws at 96 DPI,
        // FFmpeg's drawtext defaults to 72).
        var lineHeight = (int)((options.FontSize + 4) * 96.0 / 72.0);

        var lines = 4; // File Name, File Size, Duration, Resolution

        if (options.HeaderMeta)
        {
            lines += 2; // FPS/Bitrate and Codec lines
        }

        if (!string.IsNullOrEmpty(options.Comment))
        {
            lines += 1; // Comment line
        }

        var textHeight = lineHeight * lines + 5;

        return headerImageHeight > 0
            ? Math.Max(textHeight, headerImageHeight + (HeaderMargin * 2))
            : textHeight;
    }

    /// <summary>
    /// Finds a suitable font file for drawtext filter.
    /// </summary>
    /// <param name="fontName">The font name or path.</param>
    /// <returns>The path to the font file, or null if not found.</returns>
    private static string? FindFontFile(string fontName)
    {
        lock (FontCache)
        {
            if (FontCache.TryGetValue(fontName, out var cached))
            {
                return cached;
            }

            var resolved = ResolveFontFile(fontName);
            FontCache[fontName] = resolved;
            return resolved;
        }
    }

    /// <summary>Scans the platform's font directories for <paramref name="fontName"/>.</summary>
    private static string? ResolveFontFile(string fontName)
    {
        // A path to an actual font file should be taken at its word.
        if (File.Exists(fontName))
        {
            return fontName;
        }

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