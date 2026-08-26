using FFmpeg.AutoGen.Abstractions;
using nathanbutlerDEV.mt.net.Models;
using nathanbutlerDEV.mt.net.Utilities;

namespace nathanbutlerDEV.mt.net.Services;

/// <summary>
/// Encodes an <see cref="RgbaImage"/> to a still image file using FFmpeg's own encoders.
/// </summary>
/// <remarks>
/// Replaces ImageSharp's SaveAsPngAsync / SaveAsJpegAsync. PNG is written straight from
/// RGBA; JPEG goes through swscale to YUVJ420P first, since the MJPEG encoder does not
/// accept packed RGB.
/// </remarks>
public static unsafe class ImageEncoder
{
    /// <summary>AV_CODEC_FLAG_QSCALE - use a fixed quantiser rather than a bitrate target.</summary>
    private const int AvCodecFlagQScale = 1 << 1;

    /// <summary>FF_QP2LAMBDA - scales a quantiser index into global_quality units.</summary>
    private const int FfQp2Lambda = 118;

    /// <summary>PNG deflate level. 9 matches the previous PngCompressionLevel.BestCompression.</summary>
    private const int PngCompressionLevel = 9;

    public static void SavePng(RgbaImage image, string path)
    {
        Encode(image, path, AVCodecID.AV_CODEC_ID_PNG, AVPixelFormat.AV_PIX_FMT_RGBA, jpegQuality: null);
    }

    public static void SaveJpeg(RgbaImage image, string path, int quality = 90)
    {
        Encode(image, path, AVCodecID.AV_CODEC_ID_MJPEG, AVPixelFormat.AV_PIX_FMT_YUVJ420P, quality);
    }

    private static void Encode(RgbaImage image, string path, AVCodecID codecId, AVPixelFormat pixelFormat, int? jpegQuality)
    {
        AVCodecContext* codecContext = null;
        AVFrame* sourceFrame = null;
        AVFrame* encodeFrame = null;
        AVPacket* packet = null;
        SwsContext* swsContext = null;

        try
        {
            var codec = ffmpeg.avcodec_find_encoder(codecId);
            if (codec == null)
            {
                throw new InvalidOperationException(
                    $"This FFmpeg build has no {codecId} encoder, so '{Path.GetFileName(path)}' cannot be written.");
            }

            codecContext = ffmpeg.avcodec_alloc_context3(codec);
            if (codecContext == null)
            {
                throw new InvalidOperationException("Failed to allocate the encoder context.");
            }

            codecContext->width = image.Width;
            codecContext->height = image.Height;
            codecContext->pix_fmt = pixelFormat;

            // A single still, so any non-zero time base will do.
            codecContext->time_base = new AVRational { num = 1, den = 1 };

            if (jpegQuality.HasValue)
            {
                // MJPEG quantiser scale runs 1 (best) to 31 (worst).
                var qscale = Math.Clamp((int)Math.Round((100 - jpegQuality.Value) * 30.0 / 100.0) + 1, 1, 31);
                codecContext->flags |= AvCodecFlagQScale;
                codecContext->global_quality = qscale * FfQp2Lambda;
                codecContext->color_range = AVColorRange.AVCOL_RANGE_JPEG;
            }
            else
            {
                codecContext->compression_level = PngCompressionLevel;
            }

            ffmpeg.avcodec_open2(codecContext, codec, null).ThrowExceptionIfError();

            sourceFrame = AVFrameBridge.ToAVFrame(image);

            if (pixelFormat == AVPixelFormat.AV_PIX_FMT_RGBA)
            {
                // No conversion needed - encode the RGBA frame directly.
                encodeFrame = sourceFrame;
                sourceFrame = null;
            }
            else
            {
                encodeFrame = ffmpeg.av_frame_alloc();
                if (encodeFrame == null)
                {
                    throw new InvalidOperationException("Failed to allocate the encode frame.");
                }

                encodeFrame->width = image.Width;
                encodeFrame->height = image.Height;
                encodeFrame->format = (int)pixelFormat;
                ffmpeg.av_frame_get_buffer(encodeFrame, 32).ThrowExceptionIfError();

                swsContext = ffmpeg.sws_getContext(
                    image.Width, image.Height, AVPixelFormat.AV_PIX_FMT_RGBA,
                    image.Width, image.Height, pixelFormat,
                    (int)SwsFlags.SWS_BILINEAR, null, null, null);

                if (swsContext == null)
                {
                    throw new InvalidOperationException("Could not initialize the colour conversion context.");
                }

                ffmpeg.sws_scale(
                    swsContext,
                    sourceFrame->data,
                    sourceFrame->linesize,
                    0,
                    image.Height,
                    encodeFrame->data,
                    encodeFrame->linesize);
            }

            // With AV_CODEC_FLAG_QSCALE the MJPEG encoder reads the quantiser from the frame, not
            // from the context, so global_quality alone was silently ignored and every JPEG came
            // out at the encoder default regardless of the requested quality.
            if (jpegQuality.HasValue)
            {
                encodeFrame->quality = codecContext->global_quality;
            }

            ffmpeg.avcodec_send_frame(codecContext, encodeFrame).ThrowExceptionIfError();
            ffmpeg.avcodec_send_frame(codecContext, null).ThrowExceptionIfError();

            packet = ffmpeg.av_packet_alloc();
            if (packet == null)
            {
                throw new InvalidOperationException("Failed to allocate the output packet.");
            }

            using var output = File.Create(path);

            while (true)
            {
                var ret = ffmpeg.avcodec_receive_packet(codecContext, packet);
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                {
                    break;
                }

                ret.ThrowExceptionIfError();

                output.Write(new ReadOnlySpan<byte>(packet->data, packet->size));
                ffmpeg.av_packet_unref(packet);
            }
        }
        finally
        {
            if (packet != null)
            {
                var pPacket = packet;
                ffmpeg.av_packet_free(&pPacket);
            }

            if (swsContext != null)
            {
                ffmpeg.sws_freeContext(swsContext);
            }

            if (encodeFrame != null)
            {
                var pFrame = encodeFrame;
                ffmpeg.av_frame_free(&pFrame);
            }

            if (sourceFrame != null)
            {
                var pFrame = sourceFrame;
                ffmpeg.av_frame_free(&pFrame);
            }

            if (codecContext != null)
            {
                var pContext = codecContext;
                ffmpeg.avcodec_free_context(&pContext);
            }
        }
    }
}
