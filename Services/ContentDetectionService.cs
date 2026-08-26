using nathanbutlerDEV.mt.net.Models;

namespace nathanbutlerDEV.mt.net.Services;

public class ContentDetectionService
{
    // Rec. 601 luma weights, used by the blank-frame brightness analysis.
    private const double LumaR601 = 0.299;
    private const double LumaG601 = 0.587;
    private const double LumaB601 = 0.114;

    // Rec. 709 luma weights, matching the greyscale conversion the blur detector
    // previously performed via ImageSharp's default Grayscale() mode.
    private const double LumaR709 = 0.2126;
    private const double LumaG709 = 0.7152;
    private const double LumaB709 = 0.0722;

    public static bool IsBlankFrame(RgbaImage image, int threshold = 85)
    {
        // Use histogram analysis to detect blank frames
        // Calculate average brightness and check if it's too uniform

        long totalBrightness = 0;
        long pixelCount = (long)image.Width * image.Height;

        for (int y = 0; y < image.Height; y++)
        {
            var row = image.Row(y);

            for (int x = 0; x < image.Width; x++)
            {
                var i = x * 4;
                // Calculate perceived brightness
                var brightness = row[i] * LumaR601 + row[i + 1] * LumaG601 + row[i + 2] * LumaB601;
                totalBrightness += (long)brightness;
            }
        }

        var averageBrightness = totalBrightness / pixelCount;

        // Calculate variance to detect uniformity
        long variance = 0;

        for (int y = 0; y < image.Height; y++)
        {
            var row = image.Row(y);

            for (int x = 0; x < image.Width; x++)
            {
                var i = x * 4;
                var brightness = (long)(row[i] * LumaR601 + row[i + 1] * LumaG601 + row[i + 2] * LumaB601);
                var diff = brightness - averageBrightness;
                variance += diff * diff;
            }
        }

        var standardDeviation = Math.Sqrt(variance / pixelCount);

        // If standard deviation is very low, the frame is likely blank/uniform
        // Threshold of 85 means less than 15% variation is considered blank
        var uniformityThreshold = (255 * (100 - threshold)) / 100.0;

        return standardDeviation < uniformityThreshold;
    }

    public static bool IsBlurryFrame(RgbaImage image, int threshold = 62)
    {
        // Use Laplacian variance to detect blur
        // Lower variance indicates more blur

        double laplacianVariance = CalculateLaplacianVariance(image);

        // Normalize threshold - lower threshold means stricter blur detection
        // Threshold of 62 is a middle ground (100 = very strict, 0 = very lenient)
        var normalizedThreshold = threshold * 2.0; // Scale to reasonable variance range

        return laplacianVariance < normalizedThreshold;
    }

    /// <summary>
    /// Runs a Laplacian kernel over the image's luma and returns the variance of the response.
    /// </summary>
    /// <remarks>
    /// Luma is computed on the fly rather than materialising a greyscale copy first.
    /// </remarks>
    private static double CalculateLaplacianVariance(RgbaImage image)
    {
        var width = image.Width;
        var height = image.Height;

        if (width < 3 || height < 3)
        {
            return 0;
        }

        // Precompute luma once; the kernel reads each pixel up to nine times.
        var luma = new double[width * height];
        for (int y = 0; y < height; y++)
        {
            var row = image.Row(y);
            var offset = y * width;

            for (int x = 0; x < width; x++)
            {
                var i = x * 4;
                luma[offset + x] = row[i] * LumaR709 + row[i + 1] * LumaG709 + row[i + 2] * LumaB709;
            }
        }

        var count = 0L;
        var sum = 0.0;
        var sumSquares = 0.0;

        for (int y = 1; y < height - 1; y++)
        {
            var prev = (y - 1) * width;
            var curr = y * width;
            var next = (y + 1) * width;

            for (int x = 1; x < width - 1; x++)
            {
                // Laplacian kernel
                var laplacian = Math.Abs(
                    -luma[prev + x - 1] - luma[prev + x] - luma[prev + x + 1]
                    - luma[curr + x - 1] + 8 * luma[curr + x] - luma[curr + x + 1]
                    - luma[next + x - 1] - luma[next + x] - luma[next + x + 1]);

                count++;
                sum += laplacian;
                sumSquares += laplacian * laplacian;
            }
        }

        if (count == 0)
        {
            return 0;
        }

        var mean = sum / count;
        return (sumSquares / count) - (mean * mean);
    }

    public static bool IsSafeForWork(RgbaImage image)
    {
        // Basic skin tone detection as a simple SFW filter
        // This is experimental and not very accurate

        int skinPixelCount = 0;
        int totalPixels = image.Width * image.Height;

        for (int y = 0; y < image.Height; y++)
        {
            var row = image.Row(y);

            for (int x = 0; x < image.Width; x++)
            {
                var i = x * 4;

                // Simple skin tone detection (YCbCr color space approximation)
                if (IsSkinTone(row[i], row[i + 1], row[i + 2]))
                {
                    skinPixelCount++;
                }
            }
        }

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

    public static RgbaImage? FindBestFrame(
        List<RgbaImage> candidates,
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
