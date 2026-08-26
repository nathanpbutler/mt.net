namespace nathanbutlerDEV.mt.net.Utilities;

/// <summary>
/// A straight (non-premultiplied) 8-bit RGBA colour.
/// </summary>
/// <remarks>
/// Replaces SixLabors.ImageSharp.Color / Rgba32. Kept deliberately minimal - the FFmpeg
/// composition path only ever needs to fill a rectangle, blit, and emit a hex string for
/// the drawtext filter.
/// </remarks>
public readonly record struct RgbaColor(byte R, byte G, byte B, byte A = 255)
{
    public static readonly RgbaColor Black = new(0, 0, 0);
    public static readonly RgbaColor White = new(255, 255, 255);
    public static readonly RgbaColor Transparent = new(0, 0, 0, 0);

    /// <summary>
    /// Formats the colour as <c>0xRRGGBB</c> for use in FFmpeg filter arguments.
    /// </summary>
    public string ToFilterHex() => $"0x{R:X2}{G:X2}{B:X2}";
}
