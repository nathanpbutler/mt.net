using FFmpeg.AutoGen.Abstractions;
using nathanbutlerDEV.mt.net.Models;
using nathanbutlerDEV.mt.net.Services;

namespace nathanbutlerDEV.mt.net.Utilities;

/// <summary>
/// Converts between <see cref="RgbaImage"/> and FFmpeg's <c>AVFrame</c>.
/// </summary>
/// <remarks>
/// This replaces five near-identical hand-rolled per-pixel converters that previously lived
/// in the decoder, the filter service and the composer. Both directions copy row-wise because
/// an AVFrame's linesize is padded for alignment and is usually wider than width * 4.
/// </remarks>
public static unsafe class AVFrameBridge
{
    /// <summary>Alignment passed to <c>av_frame_get_buffer</c>.</summary>
    private const int BufferAlignment = 32;

    /// <summary>
    /// Allocates an RGBA <c>AVFrame</c> holding a copy of <paramref name="image"/>.
    /// </summary>
    /// <remarks>
    /// The frame owns its buffer (allocated via <c>av_frame_get_buffer</c>), so a single
    /// <c>av_frame_free</c> releases everything. The previous implementations used
    /// <c>av_malloc</c> and stashed the raw pointer into <c>frame->data</c> without an
    /// AVBufferRef, which leaked one full frame buffer per call.
    /// </remarks>
    public static AVFrame* ToAVFrame(RgbaImage image)
    {
        var frame = ffmpeg.av_frame_alloc();
        if (frame == null)
        {
            throw new InvalidOperationException("Failed to allocate AVFrame");
        }

        frame->width = image.Width;
        frame->height = image.Height;
        frame->format = (int)AVPixelFormat.AV_PIX_FMT_RGBA;

        var ret = ffmpeg.av_frame_get_buffer(frame, BufferAlignment);
        if (ret < 0)
        {
            ffmpeg.av_frame_free(&frame);
            ret.ThrowExceptionIfError();
        }

        var dstStride = frame->linesize[0];
        var dst = frame->data[0];

        for (var y = 0; y < image.Height; y++)
        {
            image.Row(y).CopyTo(new Span<byte>(dst + (y * dstStride), image.Stride));
        }

        return frame;
    }

    /// <summary>
    /// Copies an RGBA <c>AVFrame</c> into a new <see cref="RgbaImage"/>.
    /// </summary>
    public static RgbaImage ToRgbaImage(AVFrame* frame)
    {
        var image = new RgbaImage(frame->width, frame->height);

        var srcStride = frame->linesize[0];
        var src = frame->data[0];

        for (var y = 0; y < image.Height; y++)
        {
            new ReadOnlySpan<byte>(src + (y * srcStride), image.Stride).CopyTo(image.Row(y));
        }

        return image;
    }
}
