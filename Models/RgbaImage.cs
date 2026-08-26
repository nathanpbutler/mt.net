using System.Buffers;
using nathanbutlerDEV.mt.net.Utilities;

namespace nathanbutlerDEV.mt.net.Models;

/// <summary>
/// A tightly-packed, top-down 8-bit RGBA raster.
/// </summary>
/// <remarks>
/// This is the pixel currency passed across every service boundary, replacing
/// <c>Image&lt;Rgba32&gt;</c>. The backing array is rented from <see cref="ArrayPool{T}"/>,
/// so it is almost always LARGER than the image - never derive length from
/// <c>Buffer.Length</c>, always use <see cref="SizeInBytes"/>.
/// </remarks>
public sealed class RgbaImage : IDisposable
{
    private byte[]? _buffer;

    public int Width { get; }
    public int Height { get; }

    /// <summary>Bytes per row. The raster is tightly packed, so this is always Width * 4.</summary>
    public int Stride => Width * 4;

    /// <summary>The number of meaningful bytes in <see cref="Buffer"/>.</summary>
    public int SizeInBytes => Width * Height * 4;

    public byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(RgbaImage));

    /// <summary>The pixel data, trimmed to <see cref="SizeInBytes"/>.</summary>
    public Span<byte> Pixels => Buffer.AsSpan(0, SizeInBytes);

    public RgbaImage(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");

        Width = width;
        Height = height;
        _buffer = ArrayPool<byte>.Shared.Rent(SizeInBytes);

        // Pooled arrays come back dirty.
        Pixels.Clear();
    }

    /// <summary>Returns the pixel span for a single row.</summary>
    public Span<byte> Row(int y)
    {
        if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
        return Buffer.AsSpan(y * Stride, Stride);
    }

    public RgbaImage Clone()
    {
        var copy = new RgbaImage(Width, Height);
        Pixels.CopyTo(copy.Pixels);
        return copy;
    }

    /// <summary>Fills the entire image with a solid colour.</summary>
    public void Fill(RgbaColor color)
    {
        var row = Row(0);
        for (var x = 0; x < Width; x++)
        {
            var i = x * 4;
            row[i] = color.R;
            row[i + 1] = color.G;
            row[i + 2] = color.B;
            row[i + 3] = color.A;
        }

        // Replicate the first row down the image.
        for (var y = 1; y < Height; y++)
        {
            row.CopyTo(Row(y));
        }
    }

    /// <summary>Copies <paramref name="src"/> over this image at (x, y), ignoring alpha.</summary>
    public void DrawImage(RgbaImage src, int x, int y)
    {
        var (sx, sy, dx, dy, w, h) = ClipBlit(src, x, y);
        if (w <= 0 || h <= 0) return;

        var bytes = w * 4;
        for (var row = 0; row < h; row++)
        {
            src.Row(sy + row).Slice(sx * 4, bytes).CopyTo(Row(dy + row).Slice(dx * 4, bytes));
        }
    }

    /// <summary>
    /// Alpha-blends <paramref name="src"/> over this image at (x, y), scaling the source
    /// alpha by <paramref name="opacity"/>.
    /// </summary>
    public void DrawImage(RgbaImage src, int x, int y, float opacity)
    {
        var (sx, sy, dx, dy, w, h) = ClipBlit(src, x, y);
        if (w <= 0 || h <= 0) return;

        var scale = Math.Clamp(opacity, 0f, 1f);

        for (var row = 0; row < h; row++)
        {
            var srcRow = src.Row(sy + row);
            var dstRow = Row(dy + row);

            for (var col = 0; col < w; col++)
            {
                var s = (sx + col) * 4;
                var d = (dx + col) * 4;

                var alpha = srcRow[s + 3] / 255f * scale;
                if (alpha <= 0f) continue;

                if (alpha >= 1f)
                {
                    dstRow[d] = srcRow[s];
                    dstRow[d + 1] = srcRow[s + 1];
                    dstRow[d + 2] = srcRow[s + 2];
                    dstRow[d + 3] = 255;
                    continue;
                }

                var inv = 1f - alpha;
                dstRow[d] = (byte)(srcRow[s] * alpha + dstRow[d] * inv);
                dstRow[d + 1] = (byte)(srcRow[s + 1] * alpha + dstRow[d + 1] * inv);
                dstRow[d + 2] = (byte)(srcRow[s + 2] * alpha + dstRow[d + 2] * inv);
                dstRow[d + 3] = (byte)Math.Min(255f, srcRow[s + 3] * scale + dstRow[d + 3] * inv);
            }
        }
    }

    /// <summary>
    /// Clips a blit of <paramref name="src"/> at (x, y) to this image's bounds.
    /// </summary>
    private (int SrcX, int SrcY, int DstX, int DstY, int Width, int Height) ClipBlit(RgbaImage src, int x, int y)
    {
        var srcX = x < 0 ? -x : 0;
        var srcY = y < 0 ? -y : 0;
        var dstX = Math.Max(x, 0);
        var dstY = Math.Max(y, 0);

        var w = Math.Min(src.Width - srcX, Width - dstX);
        var h = Math.Min(src.Height - srcY, Height - dstY);

        return (srcX, srcY, dstX, dstY, w, h);
    }

    public void Dispose()
    {
        var buffer = _buffer;
        if (buffer is null) return;

        _buffer = null;
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
