using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace nathanbutlerDEV.mt.net.Services;

/// <summary>
/// Video decoder using FFmpeg.AutoGen for direct control over frame seeking.
/// Provides both fast (keyframe-based) and accurate (frame-exact) seeking modes.
/// </summary>
public sealed unsafe class FFmpegAutoGenVideoDecoder : IDisposable
{
    private readonly AVCodecContext* _pCodecContext;
    private readonly AVFormatContext* _pFormatContext;
    private readonly AVFrame* _pFrame;
    private readonly AVPacket* _pPacket;
    private readonly int _streamIndex;
    private readonly SwsContext* _pSwsContext;
    private readonly IntPtr _convertedFrameBufferPtr;
    private readonly byte_ptr4 _dstData;
    private readonly int4 _dstLinesize;

    public string CodecName { get; }
    public int Width { get; }
    public int Height { get; }
    public AVPixelFormat PixelFormat { get; }
    public TimeSpan Duration { get; }
    public double FrameRate { get; }

    private bool _disposed = false;

    public FFmpegAutoGenVideoDecoder(string videoPath)
    {
        _pFormatContext = ffmpeg.avformat_alloc_context();

        var pFormatContext = _pFormatContext;
        ffmpeg.avformat_open_input(&pFormatContext, videoPath, null, null).ThrowExceptionIfError();
        ffmpeg.avformat_find_stream_info(_pFormatContext, null).ThrowExceptionIfError();

        // Find video stream
        AVCodec* codec = null;
        _streamIndex = ffmpeg
            .av_find_best_stream(_pFormatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0)
            .ThrowExceptionIfError();

        var stream = _pFormatContext->streams[_streamIndex];

        // Create codec context
        _pCodecContext = ffmpeg.avcodec_alloc_context3(codec);
        ffmpeg.avcodec_parameters_to_context(_pCodecContext, stream->codecpar)
            .ThrowExceptionIfError();
        ffmpeg.avcodec_open2(_pCodecContext, codec, null).ThrowExceptionIfError();

        // Store metadata
        CodecName = Marshal.PtrToStringAnsi((IntPtr)codec->name) ?? "unknown";
        Width = _pCodecContext->width;
        Height = _pCodecContext->height;
        PixelFormat = _pCodecContext->pix_fmt;

        // Calculate duration
        if (stream->duration != ffmpeg.AV_NOPTS_VALUE)
        {
            Duration = TimeSpan.FromSeconds(stream->duration * ffmpeg.av_q2d(stream->time_base));
        }
        else if (_pFormatContext->duration != ffmpeg.AV_NOPTS_VALUE)
        {
            Duration = TimeSpan.FromSeconds(_pFormatContext->duration / (double)ffmpeg.AV_TIME_BASE);
        }

        // Calculate frame rate
        var avgFrameRate = stream->avg_frame_rate;
        if (avgFrameRate.num != 0 && avgFrameRate.den != 0)
        {
            FrameRate = ffmpeg.av_q2d(avgFrameRate);
        }
        else
        {
            FrameRate = 25.0; // Default fallback
        }

        // Allocate frames and packet
        _pPacket = ffmpeg.av_packet_alloc();
        _pFrame = ffmpeg.av_frame_alloc();

        // Setup conversion context for RGBA conversion
        _pSwsContext = ffmpeg.sws_getContext(
            Width, Height, PixelFormat,
            Width, Height, AVPixelFormat.AV_PIX_FMT_RGB24,
            ffmpeg.SWS_BILINEAR, null, null, null);

        if (_pSwsContext == null)
        {
            throw new ApplicationException("Could not initialize the conversion context.");
        }

        // Allocate buffer for RGB frame
        var bufferSize = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_RGB24, Width, Height, 1);
        _convertedFrameBufferPtr = Marshal.AllocHGlobal(bufferSize);
        _dstData = new byte_ptr4();
        _dstLinesize = new int4();

        ffmpeg.av_image_fill_arrays(
            ref _dstData,
            ref _dstLinesize,
            (byte*)_convertedFrameBufferPtr,
            AVPixelFormat.AV_PIX_FMT_RGB24,
            Width,
            Height,
            1);
    }

    /// <summary>
    /// Seeks to a timestamp and extracts a frame.
    /// </summary>
    /// <param name="timestamp">Target timestamp</param>
    /// <param name="fast">If true, uses keyframe-based seeking (faster but less accurate).
    /// If false, decodes until exact frame is reached (slower but frame-accurate).</param>
    /// <returns>Frame as ImageSharp Image, or null if extraction fails</returns>
    public Image<Rgba32>? SeekAndExtractFrame(TimeSpan timestamp, bool fast = false)
    {
        try
        {
            var stream = _pFormatContext->streams[_streamIndex];
            var seekTarget = (long)(timestamp.TotalSeconds / ffmpeg.av_q2d(stream->time_base));

            // Seek flags: BACKWARD seeks to nearest keyframe before timestamp
            // In fast mode, we accept the keyframe. In accurate mode, we decode forward to exact frame.
            var seekFlags = ffmpeg.AVSEEK_FLAG_BACKWARD;

            ffmpeg.av_seek_frame(_pFormatContext, _streamIndex, seekTarget, seekFlags)
                .ThrowExceptionIfError();

            // Flush codec buffers after seek
            ffmpeg.avcodec_flush_buffers(_pCodecContext);

            AVFrame? targetFrame = null;
            var targetPts = (long)(timestamp.TotalSeconds / ffmpeg.av_q2d(stream->time_base));

            // Decode frames until we get the right one
            while (true)
            {
                ffmpeg.av_frame_unref(_pFrame);
                ffmpeg.av_packet_unref(_pPacket);

                // Read next packet
                var error = ffmpeg.av_read_frame(_pFormatContext, _pPacket);
                if (error == ffmpeg.AVERROR_EOF)
                {
                    break;
                }
                error.ThrowExceptionIfError();

                // Skip non-video packets
                if (_pPacket->stream_index != _streamIndex)
                {
                    continue;
                }

                // Send packet to decoder
                error = ffmpeg.avcodec_send_packet(_pCodecContext, _pPacket);
                if (error < 0 && error != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    error.ThrowExceptionIfError();
                }

                // Receive decoded frame
                error = ffmpeg.avcodec_receive_frame(_pCodecContext, _pFrame);
                if (error == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    continue;
                }
                if (error == ffmpeg.AVERROR_EOF)
                {
                    break;
                }
                error.ThrowExceptionIfError();

                var framePts = _pFrame->pts;

                if (fast)
                {
                    // Fast mode: Accept the first decoded frame after seek
                    // This will be the nearest keyframe, which is faster but less accurate
                    targetFrame = *_pFrame;
                    break;
                }
                else
                {
                    // Accurate mode: Continue decoding until we reach or pass the target timestamp
                    if (framePts >= targetPts)
                    {
                        targetFrame = *_pFrame;
                        break;
                    }
                }
            }

            if (targetFrame == null)
            {
                return null;
            }

            // Convert frame to RGBA and create ImageSharp Image
            return ConvertFrameToImage(targetFrame.Value);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error extracting frame at {timestamp}: {ex.Message}");
            return null;
        }
    }

    private Image<Rgba32> ConvertFrameToImage(AVFrame frame)
    {
        // Convert YUV to RGBA using sws_scale
        ffmpeg.sws_scale(
            _pSwsContext,
            frame.data,
            frame.linesize,
            0,
            Height,
            _dstData,
            _dstLinesize);

        // Create ImageSharp image from RGBA buffer
        var image = new Image<Rgba32>(Width, Height);

        var stride = _dstLinesize[0];
        var pSrc = (byte*)_convertedFrameBufferPtr;

        // Copy pixels to ImageSharp image
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var rowPtr = pSrc + (y * stride);

                for (int x = 0; x < Width; x++)
                {
                    var pixelPtr = rowPtr + (x * 3);
                    row[x] = new Rgba32(
                        pixelPtr[0],  // R
                        pixelPtr[1],  // G
                        pixelPtr[2]  // B
                    );
                }
            }
        });

        return image;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_convertedFrameBufferPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_convertedFrameBufferPtr);
        }

        if (_pSwsContext != null)
        {
            ffmpeg.sws_freeContext(_pSwsContext);
        }

        var pFrame = _pFrame;
        ffmpeg.av_frame_free(&pFrame);

        var pPacket = _pPacket;
        ffmpeg.av_packet_free(&pPacket);

        if (_pCodecContext != null)
        {
            var pCodecContext = _pCodecContext;
            ffmpeg.avcodec_free_context(&pCodecContext);
        }

        if (_pFormatContext != null)
        {
            var pFormatContext = _pFormatContext;
            ffmpeg.avformat_close_input(&pFormatContext);
        }
    }
}

/// <summary>
/// Extension methods for FFmpeg error handling
/// </summary>
internal static class FFmpegExtensions
{
    public static int ThrowExceptionIfError(this int error)
    {
        if (error < 0)
        {
            unsafe
            {
                var bufferSize = 1024;
                var buffer = stackalloc byte[bufferSize];
                ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
                var message = Marshal.PtrToStringAnsi((IntPtr)buffer);
                throw new ApplicationException($"FFmpeg error: {message} (code: {error})");
            }
        }
        return error;
    }
}
