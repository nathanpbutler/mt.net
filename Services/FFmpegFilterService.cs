using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace nathanbutlerDEV.mt.net.Services;

/// <summary>
/// Applies image filters using FFmpeg filter graphs for native FFmpeg-based processing.
/// Provides filters like greyscale, sepia, invert, fancy rotation, cross-processing, and film strip effects.
/// </summary>
public sealed unsafe class FFmpegFilterService : IDisposable
{
    // Random number generator for filter effects
    private static readonly Random Random = new();
    // Disposal flag
    private bool _disposed = false;

    /// <summary>
    /// Applies multiple filters to an image based on a filter string.
    /// </summary>
    /// <param name="image">The input image to process.</param>
    /// <param name="filterString">A comma-separated string of filter names to apply.</param>
    /// <returns>The processed image after applying the filters.</returns>
    public Image<Rgba32> ApplyFilters(Image<Rgba32> image, string filterString)
    {
        if (string.IsNullOrEmpty(filterString) || filterString.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return image;
        }

        var filters = filterString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var currentImage = image;

        foreach (var filter in filters)
        {
            var processedImage = ApplyFilter(currentImage, filter);
            if (processedImage != currentImage && currentImage != image)
            {
                currentImage.Dispose();
            }
            currentImage = processedImage;
        }

        return currentImage;
    }

    /// <summary>
    /// Applies a single filter to an image.
    /// </summary>
    /// <param name="image">The input image to process.</param>
    /// <param name="filterName">The name of the filter to apply.</param>
    /// <returns>The processed image after applying the filter.</returns>
    private Image<Rgba32> ApplyFilter(Image<Rgba32> image, string filterName)
    {
        return filterName.ToLowerInvariant() switch
        {
            "greyscale" or "grayscale" => ApplyGreyscaleFFmpeg(image),
            "sepia" => ApplySepiaFFmpeg(image),
            "invert" => ApplyInvertFFmpeg(image),
            "fancy" => ApplyFancyFFmpeg(image),
            "cross" => ApplyCrossProcessingFFmpeg(image),
            "strip" => ApplyStripFFmpeg(image),
            _ => image
        };
    }

    /// <summary>
    /// Applies greyscale filter using FFmpeg colorchannelmixer.
    /// </summary>
    /// <param name="image">The input image to process.</param>
    /// <returns>The processed image after applying the greyscale filter.</returns>
    private Image<Rgba32> ApplyGreyscaleFFmpeg(Image<Rgba32> image)
    {
        var filterSpec = "colorchannelmixer=.3:.4:.3:0:.3:.4:.3:0:.3:.4:.3";
        return ApplyFFmpegFilter(image, filterSpec);
    }

    /// <summary>
    /// Applies sepia filter using FFmpeg colorchannelmixer.
    /// </summary>
    /// <param name="image">The input image to process.</param>
    /// <returns>The processed image after applying the sepia filter.</returns>
    private Image<Rgba32> ApplySepiaFFmpeg(Image<Rgba32> image)
    {
        var filterSpec = "colorchannelmixer=.393:.769:.189:0:.349:.686:.168:0:.272:.534:.131";
        return ApplyFFmpegFilter(image, filterSpec);
    }

    /// <summary>
    /// Applies invert filter using FFmpeg negate.
    /// </summary>
    /// <param name="image">The input image to process.</param>
    /// <returns>The processed image after applying the invert filter.</returns>
    private Image<Rgba32> ApplyInvertFFmpeg(Image<Rgba32> image)
    {
        var filterSpec = "negate";
        return ApplyFFmpegFilter(image, filterSpec);
    }

    /// <summary>
    /// Applies fancy rotation filter using FFmpeg rotate.
    /// </summary>
    /// <param name="image">The input image to process.</param>
    /// <returns>The processed image after applying the fancy rotation filter.</returns>
    private Image<Rgba32> ApplyFancyFFmpeg(Image<Rgba32> image)
    {
        // Random rotation between -15 and 15 degrees
        var angleDegrees = Random.Next(-15, 16);
        var angleRadians = angleDegrees * Math.PI / 180.0;

        // Use black background color for rotation
        var filterSpec = $"rotate={angleRadians:F4}:c=black";
        return ApplyFFmpegFilter(image, filterSpec);
    }

    /// <summary>
    /// Applies cross-processing effect using FFmpeg curves and color adjustments.
    /// </summary>
    /// <param name="image">The input image to process.</param>
    /// <returns>The processed image after applying the cross-processing effect.</returns>
    private Image<Rgba32> ApplyCrossProcessingFFmpeg(Image<Rgba32> image)
    {
        // Cross processing: shift colors, increase saturation and contrast
        var filterSpec = "hue=h=30,eq=saturation=1.2:contrast=1.1";
        return ApplyFFmpegFilter(image, filterSpec);
    }

    /// <summary>
    /// Applies film strip effect using FFmpeg drawbox.
    /// </summary>
    /// <param name="image">The input image to process.</param>
    /// <returns>The processed image after applying the film strip effect.</returns>
    private Image<Rgba32> ApplyStripFFmpeg(Image<Rgba32> image)
    {
        var sprocketWidth = image.Width / 20;
        var sprocketHeight = sprocketWidth;
        var sprocketSpacing = sprocketHeight * 2;
        var holeRadius = sprocketWidth / 3;
        var holeX = sprocketWidth / 2;

        // Build filter for film strip effect
        var filters = new List<string>
        {
            // Draw black borders on left and right
            $"drawbox=x=0:y=0:w={sprocketWidth}:h=ih:color=black:t=fill",
            $"drawbox=x=iw-{sprocketWidth}:y=0:w={sprocketWidth}:h=ih:color=black:t=fill"
        };

        // Draw sprocket holes (simplified - just draw white boxes)
        for (int y = sprocketHeight / 2; y < image.Height; y += sprocketSpacing)
        {
            // Left sprocket holes
            filters.Add($"drawbox=x={holeX - holeRadius}:y={y - holeRadius}:w={holeRadius * 2}:h={holeRadius * 2}:color=white:t=fill");

            // Right sprocket holes
            filters.Add($"drawbox=x=iw-{holeX + holeRadius}:y={y - holeRadius}:w={holeRadius * 2}:h={holeRadius * 2}:color=white:t=fill");
        }

        var filterSpec = string.Join(",", filters);
        return ApplyFFmpegFilter(image, filterSpec);
    }

    /// <summary>
    /// Applies an FFmpeg filter specification to an image.
    /// </summary>
    /// <param name="image">The input image to process.</param>
    /// <param name="filterSpec">The FFmpeg filter specification string.</param>
    /// <returns>The processed image after applying the filter.</returns>
    private Image<Rgba32> ApplyFFmpegFilter(Image<Rgba32> image, string filterSpec)
    {
        AVFilterGraph* filterGraph = null;
        AVFilterContext* bufferSrcCtx = null;
        AVFilterContext* bufferSinkCtx = null;
        AVFrame* inputFrame = null;
        AVFrame* outputFrame = null;

        try
        {
            // Convert ImageSharp to AVFrame
            inputFrame = ImageToAVFrame(image);

            // Create filter graph
            filterGraph = ffmpeg.avfilter_graph_alloc();
            if (filterGraph == null)
            {
                Console.WriteLine("Failed to allocate filter graph");
                return image;
            }

            // Build filter graph
            if (!CreateFilterGraph(filterGraph, &bufferSrcCtx, &bufferSinkCtx, image.Width, image.Height, filterSpec))
            {
                Console.WriteLine($"Failed to create filter graph for: {filterSpec}");
                return image;
            }

            // Push frame through filter
            ffmpeg.av_buffersrc_add_frame_flags(bufferSrcCtx, inputFrame, 0).ThrowExceptionIfError();

            // Pull filtered frame
            outputFrame = ffmpeg.av_frame_alloc();
            var ret = ffmpeg.av_buffersink_get_frame(bufferSinkCtx, outputFrame);

            if (ret < 0)
            {
                Console.WriteLine($"Failed to get filtered frame: {ret}");
                return image;
            }

            // Convert back to ImageSharp
            return AVFrameToImage(outputFrame);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error applying FFmpeg filter: {ex.Message}");
            return image;
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
    /// Creates and configures a filter graph.
    /// </summary>
    /// <param name="filterGraph">The filter graph to configure.</param>
    /// <param name="bufferSrcCtx">Pointer to the buffer source context.</param>
    /// <param name="bufferSinkCtx">Pointer to the buffer sink context.</param>
    /// <param name="width">Width of the input image.</param>
    /// <param name="height">Height of the input image.</param>
    /// <param name="filterSpec">The FFmpeg filter specification string.</param>
    /// <returns>True if the filter graph was created successfully; otherwise, false.</returns>
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
            var bufferSrc = ffmpeg.avfilter_get_by_name("buffer");
            var bufferSink = ffmpeg.avfilter_get_by_name("buffersink");

            if (bufferSrc == null || bufferSink == null)
            {
                return false;
            }

            // Create buffer source
            var args = $"video_size={width}x{height}:pix_fmt={((int)AVPixelFormat.AV_PIX_FMT_RGBA)}:time_base=1/1000";
            var ret = ffmpeg.avfilter_graph_create_filter(bufferSrcCtx, bufferSrc, "in", args, null, filterGraph);
            if (ret < 0) return false;

            // Create buffer sink
            ret = ffmpeg.avfilter_graph_create_filter(bufferSinkCtx, bufferSink, "out", null, null, filterGraph);
            if (ret < 0) return false;

            // Parse filter chain
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

            if (ret < 0) return false;

            // Configure graph
            ret = ffmpeg.avfilter_graph_config(filterGraph, null);
            return ret >= 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Converts ImageSharp Image to AVFrame.
    /// </summary>
    /// <param name="image">The input image to convert.</param>
    /// <returns>The allocated AVFrame containing the image data.</returns>
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

        var bufferSize = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_RGBA, image.Width, image.Height, 1);
        var buffer = (byte*)ffmpeg.av_malloc((ulong)bufferSize);

        var dstData = new byte_ptr4();
        var dstLinesize = new int4();
        ffmpeg.av_image_fill_arrays(ref dstData, ref dstLinesize, buffer, AVPixelFormat.AV_PIX_FMT_RGBA, image.Width, image.Height, 1);

        frame->data[0] = dstData[0];
        frame->data[1] = dstData[1];
        frame->data[2] = dstData[2];
        frame->data[3] = dstData[3];

        frame->linesize[0] = dstLinesize[0];
        frame->linesize[1] = dstLinesize[1];
        frame->linesize[2] = dstLinesize[2];
        frame->linesize[3] = dstLinesize[3];

        // Copy pixel data
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
    /// Converts AVFrame to ImageSharp Image.
    /// </summary>
    /// <param name="frame">The input AVFrame to convert.</param>
    /// <returns>The converted ImageSharp Image.</returns>
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
                    row[x] = new Rgba32(pixelPtr[0], pixelPtr[1], pixelPtr[2], pixelPtr[3]);
                }
            }
        });

        return image;
    }

    /// <summary>
    /// Disposes the service and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
