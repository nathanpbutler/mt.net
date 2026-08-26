using nathanbutlerDEV.mt.net.Models;
using nathanbutlerDEV.mt.net.Utilities;

namespace nathanbutlerDEV.mt.net.Services;

/// <summary>
/// Blends a watermark image over a raster.
/// </summary>
/// <remarks>
/// Relocated from the removed ImageComposer, which was shared by both composers. The
/// 0.7 blend factor and the centre-of-canvas placement are carried over unchanged.
/// </remarks>
public static class WatermarkService
{
    /// <summary>Opacity applied to the watermark, matching the previous behaviour.</summary>
    private const float WatermarkOpacity = 0.7f;

    public static void ApplyWatermark(RgbaImage image, string watermarkPath, bool center = true)
    {
        if (string.IsNullOrEmpty(watermarkPath) || !File.Exists(watermarkPath))
        {
            return;
        }

        try
        {
            using var watermark = ImageLoader.Load(watermarkPath);
            if (watermark is null)
            {
                ConsoleOutput.Error($"Error applying watermark: could not decode '{watermarkPath}'.");
                return;
            }

            var x = center ? (image.Width - watermark.Width) / 2 : 0;
            var y = center ? (image.Height - watermark.Height) / 2 : 0;

            image.DrawImage(watermark, x, y, WatermarkOpacity);
        }
        catch (Exception ex)
        {
            ConsoleOutput.Error($"Error applying watermark: {ex.Message}");
        }
    }
}
