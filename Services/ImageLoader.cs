using nathanbutlerDEV.mt.net.Models;
using nathanbutlerDEV.mt.net.Utilities;

namespace nathanbutlerDEV.mt.net.Services;

/// <summary>
/// Loads a still image file (PNG, JPEG, ...) into an <see cref="RgbaImage"/>.
/// </summary>
/// <remarks>
/// Replaces ImageSharp's Image.Load. Still images open as an AVFormatContext with a
/// single-frame video stream, so the existing decoder handles them without any
/// format-specific code.
/// </remarks>
public static class ImageLoader
{
    public static RgbaImage? Load(string path)
    {
        try
        {
            using var decoder = new FFmpegAutoGenVideoDecoder(path);
            return decoder.DecodeFirstFrame();
        }
        catch (Exception ex)
        {
            ConsoleOutput.Error($"Error loading image '{path}': {ex.Message}");
            return null;
        }
    }
}
