using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace nathanbutlerDEV.mt.net.Services;

public class ContentDetectionService
{
    public static bool IsBlankFrame(Image<Rgba32> image, int threshold = 85)
    {
        // Use histogram analysis to detect blank frames
        // Calculate average brightness and check if it's too uniform

        long totalBrightness = 0;
        long pixelCount = image.Width * image.Height;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var pixelRow = accessor.GetRowSpan(y);

                for (int x = 0; x < pixelRow.Length; x++)
                {
                    var pixel = pixelRow[x];
                    // Calculate perceived brightness
                    var brightness = pixel.R * 0.299 + pixel.G * 0.587 + pixel.B * 0.114;
                    totalBrightness += (long)brightness;
                }
            }
        });

        var averageBrightness = totalBrightness / pixelCount;

        // Calculate variance to detect uniformity
        long variance = 0;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var pixelRow = accessor.GetRowSpan(y);

                for (int x = 0; x < pixelRow.Length; x++)
                {
                    var pixel = pixelRow[x];
                    var brightness = (long)(pixel.R * 0.299 + pixel.G * 0.587 + pixel.B * 0.114);
                    var diff = brightness - averageBrightness;
                    variance += diff * diff;
                }
            }
        });

        var standardDeviation = Math.Sqrt(variance / pixelCount);

        // If standard deviation is very low, the frame is likely blank/uniform
        // Threshold of 85 means less than 15% variation is considered blank
        var uniformityThreshold = (255 * (100 - threshold)) / 100.0;

        return standardDeviation < uniformityThreshold;
    }

    public static bool IsBlurryFrame(Image<Rgba32> image, int threshold = 62)
    {
        // Use Laplacian variance to detect blur
        // Lower variance indicates more blur

        var grayscale = image.Clone(ctx => ctx.Grayscale());

        double laplacianVariance = CalculateLaplacianVariance(grayscale);

        grayscale.Dispose();

        // Normalize threshold - lower threshold means stricter blur detection
        // Threshold of 62 is a middle ground (100 = very strict, 0 = very lenient)
        var normalizedThreshold = threshold * 2.0; // Scale to reasonable variance range

        return laplacianVariance < normalizedThreshold;
    }

    private static double CalculateLaplacianVariance(Image<Rgba32> grayscaleImage)
    {
        // Simplified Laplacian operator for blur detection
        var width = grayscaleImage.Width;
        var height = grayscaleImage.Height;
        var values = new List<double>();

        grayscaleImage.ProcessPixelRows(accessor =>
        {
            for (int y = 1; y < height - 1; y++)
            {
                var prevRow = accessor.GetRowSpan(y - 1);
                var currRow = accessor.GetRowSpan(y);
                var nextRow = accessor.GetRowSpan(y + 1);

                for (int x = 1; x < width - 1; x++)
                {
                    // Laplacian kernel
                    var laplacian =
                        Math.Abs(
                            -prevRow[x - 1].R - prevRow[x].R - prevRow[x + 1].R
                            - currRow[x - 1].R + 8 * currRow[x].R - currRow[x + 1].R
                            - nextRow[x - 1].R - nextRow[x].R - nextRow[x + 1].R
                        );

                    values.Add(laplacian);
                }
            }
        });

        if (values.Count == 0)
        {
            return 0;
        }

        // Calculate variance
        var mean = values.Average();
        var variance = values.Sum(v => Math.Pow(v - mean, 2)) / values.Count;

        return variance;
    }

    public static bool IsSafeForWork(Image<Rgba32> image)
    {
        // Basic skin tone detection as a simple SFW filter
        // This is experimental and not very accurate

        int skinPixelCount = 0;
        int totalPixels = image.Width * image.Height;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var pixelRow = accessor.GetRowSpan(y);

                for (int x = 0; x < pixelRow.Length; x++)
                {
                    var pixel = pixelRow[x];

                    // Simple skin tone detection (YCbCr color space approximation)
                    if (IsSkinTone(pixel.R, pixel.G, pixel.B))
                    {
                        skinPixelCount++;
                    }
                }
            }
        });

        var skinPercentage = (skinPixelCount * 100.0) / totalPixels;

        // If more than 40% of the image is skin tone, flag it
        return skinPercentage <= 40;
    }

    private static bool IsSkinTone(byte r, byte g, byte b)
    {
        // Simple skin tone detection using RGB values
        // This is a very basic implementation
        return r > 95 && g > 40 && b > 20 &&
               r > g && r > b &&
               Math.Abs(r - g) > 15 &&
               r - Math.Min(g, b) > 15;
    }

    public static Image<Rgba32>? FindBestFrame(
        List<Image<Rgba32>> candidates,
        bool skipBlank,
        bool skipBlurry,
        int blankThreshold = 85,
        int blurThreshold = 62)
    {
        foreach (var candidate in candidates)
        {
            if (skipBlank && IsBlankFrame(candidate, blankThreshold))
            {
                continue;
            }

            if (skipBlurry && IsBlurryFrame(candidate, blurThreshold))
            {
                continue;
            }

            return candidate;
        }

        // If all frames are rejected, return the first one
        return candidates.FirstOrDefault();
    }
}
