using nathanbutlerDEV.mt.net.Models;

namespace nathanbutlerDEV.mt.net.Services;

/// <summary>
/// Quantitative read of a single frame. Every metric is computed on the same downscaled copy,
/// so one analysis serves the accept/reject decision, the ranking of rejects, and deduplication.
/// </summary>
/// <param name="Brightness">Mean luma, 0-255.</param>
/// <param name="Uniformity">Standard deviation of luma. Low means a flat, blank-looking frame.</param>
/// <param name="Sharpness">Variance of the Laplacian response. Low means blurry.</param>
/// <param name="SkinPercentage">Share of pixels matching the skin-tone rules, 0-100.</param>
/// <param name="Fingerprint">64-bit average hash, for near-duplicate comparison.</param>
public readonly record struct FrameAnalysis(
    double Brightness,
    double Uniformity,
    double Sharpness,
    double SkinPercentage,
    ulong Fingerprint);

/// <summary>Which metrics a caller actually needs, so unused ones are not computed.</summary>
[Flags]
public enum AnalysisNeeds
{
    None = 0,
    Uniformity = 1 << 0,
    Sharpness = 1 << 1,
    Skin = 1 << 2,
    Fingerprint = 1 << 3
}

public static class ContentDetectionService
{
    /// <summary>
    /// Longest edge, in pixels, of the copy every metric is computed on.
    /// </summary>
    /// <remarks>
    /// Detection used to run on the full decoded frame: on 4K that meant analysing 8.3 million
    /// pixels — and allocating a 63 MB <c>double[]</c> for the blur pass — to judge a thumbnail
    /// that ends up 400px wide. Working from a downscaled copy costs a fraction of that and
    /// judges the frame at roughly the size it will actually be seen. Frames already at or below
    /// this size are analysed as-is.
    /// </remarks>
    public const int DetectionMaxEdge = 480;

    /// <summary>Skin share above which <c>--sfw</c> rejects a frame.</summary>
    private const double SkinLimit = 40.0;

    /// <summary>
    /// Luma standard deviation treated as "blank" at <c>--blank-threshold 100</c>.
    /// </summary>
    /// <remarks>
    /// Busy content measures around 80; this sits just below 255*0.3 so the top of the dial is
    /// aggressive without being meaningless. The mapping is linear because luma spread is itself
    /// linear in the pixel values.
    /// </remarks>
    private const double MaxBlankCutoff = 76.5;

    /// <summary>
    /// Laplacian variance treated as "blurry" at <c>--blur-threshold 100</c>.
    /// </summary>
    /// <remarks>
    /// Sharpness spans three orders of magnitude — roughly 6 for heavy blur to 6500 for a crisp
    /// frame — so the dial is mapped logarithmically. A linear map (as in v2, which compared
    /// against <c>threshold * 2</c>) put the entire 0-100 range inside the bottom 3% of the
    /// scale: everything above about 30 behaved identically, and a merely soft frame could not
    /// be caught at any setting.
    /// </remarks>
    private const double MaxBlurCutoff = 3000.0;

    // Rec. 601 luma weights, used by the brightness and uniformity analysis.
    private const double LumaR601 = 0.299;
    private const double LumaG601 = 0.587;
    private const double LumaB601 = 0.114;

    // Rec. 709 luma weights, matching the greyscale conversion the blur detector
    // previously performed via ImageSharp's default Grayscale() mode.
    private const double LumaR709 = 0.2126;
    private const double LumaG709 = 0.7152;
    private const double LumaB709 = 0.0722;

    /// <summary>
    /// Converts <c>--blank-threshold</c> into the luma spread below which a frame counts as blank.
    /// </summary>
    /// <remarks>
    /// 0 disables the check; 100 is maximally aggressive. This is the opposite of v2, where a
    /// *lower* number was stricter and anything at or below 50 rejected every frame of ordinary
    /// video, failing the whole run. Both thresholds now read the same way round.
    /// </remarks>
    public static double BlankCutoff(int threshold) =>
        Math.Clamp(threshold, 0, 100) / 100.0 * MaxBlankCutoff;

    /// <summary>
    /// Converts <c>--blur-threshold</c> into the sharpness below which a frame counts as blurry.
    /// </summary>
    public static double BlurCutoff(int threshold)
    {
        var t = Math.Clamp(threshold, 0, 100);
        return t == 0 ? 0.0 : Math.Pow(MaxBlurCutoff, t / 100.0);
    }

    /// <summary>
    /// Converts a v2-era <c>--blank-threshold</c> into the equivalent
    /// <c>--blank-sensitivity</c>, for the message shown when the retired name is used.
    /// </summary>
    /// <remarks>
    /// v2 compared the luma spread against <c>255 * (100 - t) / 100</c>, so its scale ran
    /// backwards. Values at or below 50 produced a cutoff no real frame could clear and are
    /// reported as 100 here, since the nearest honest equivalent is "maximally aggressive".
    /// </remarks>
    public static int TranslateLegacyBlankThreshold(int legacy)
    {
        var cutoff = 255.0 * (100 - Math.Clamp(legacy, 0, 100)) / 100.0;
        return (int)Math.Round(Math.Clamp(cutoff / MaxBlankCutoff * 100.0, 0, 100));
    }

    /// <summary>
    /// Converts a v2-era <c>--blur-threshold</c> into the equivalent <c>--blur-sensitivity</c>.
    /// </summary>
    /// <remarks>v2 compared the Laplacian variance against <c>t * 2</c>.</remarks>
    public static int TranslateLegacyBlurThreshold(int legacy)
    {
        var cutoff = Math.Clamp(legacy, 0, 100) * 2.0;
        if (cutoff <= 1.0)
        {
            return 0;
        }

        return (int)Math.Round(Math.Clamp(100.0 * Math.Log(cutoff) / Math.Log(MaxBlurCutoff), 0, 100));
    }

    /// <summary>Works out which metrics the given options require.</summary>
    public static AnalysisNeeds NeedsFor(ThumbnailOptions options)
    {
        var needs = AnalysisNeeds.None;

        if (options.SkipBlank) needs |= AnalysisNeeds.Uniformity;
        if (options.SkipBlurry) needs |= AnalysisNeeds.Sharpness;
        if (options.Sfw) needs |= AnalysisNeeds.Skin;
        if (options.Dedupe || options.SceneDetect) needs |= AnalysisNeeds.Fingerprint;

        return needs;
    }

    /// <summary>
    /// Measures <paramref name="image"/>, computing only what <paramref name="needs"/> asks for.
    /// </summary>
    public static FrameAnalysis Analyse(RgbaImage image, AnalysisNeeds needs)
    {
        if (needs == AnalysisNeeds.None)
        {
            return default;
        }

        var scaled = Downscale(image, DetectionMaxEdge);

        try
        {
            var target = scaled ?? image;
            var luma = ExtractLuma(target, LumaR601, LumaG601, LumaB601);

            double brightness = 0, uniformity = 0;
            if (needs.HasFlag(AnalysisNeeds.Uniformity))
            {
                (brightness, uniformity) = MeanAndStdDev(luma);
            }

            var sharpness = needs.HasFlag(AnalysisNeeds.Sharpness)
                ? LaplacianVariance(ExtractLuma(target, LumaR709, LumaG709, LumaB709), target.Width, target.Height)
                : 0.0;

            var skin = needs.HasFlag(AnalysisNeeds.Skin) ? SkinPercentage(target) : 0.0;

            var fingerprint = needs.HasFlag(AnalysisNeeds.Fingerprint)
                ? AverageHash(luma, target.Width, target.Height)
                : 0UL;

            return new FrameAnalysis(brightness, uniformity, sharpness, skin, fingerprint);
        }
        finally
        {
            scaled?.Dispose();
        }
    }

    /// <summary>
    /// How much headroom a frame has against the enabled checks: 1.0 or more is acceptable,
    /// below 1.0 is a rejection, and larger is better.
    /// </summary>
    /// <remarks>
    /// A single comparable number for every check is what lets the retry loop fall back to the
    /// least-bad candidate instead of discarding the thumbnail entirely. Each check contributes
    /// the ratio of its metric to its cutoff and the worst one wins, so a frame is only as good
    /// as its weakest aspect.
    /// </remarks>
    public static double AcceptanceRatio(FrameAnalysis analysis, ThumbnailOptions options)
    {
        var worst = double.PositiveInfinity;

        if (options.SkipBlank)
        {
            var cutoff = BlankCutoff(options.BlankThreshold);
            worst = Math.Min(worst, cutoff <= 0 ? double.PositiveInfinity : analysis.Uniformity / cutoff);
        }

        if (options.SkipBlurry)
        {
            var cutoff = BlurCutoff(options.BlurThreshold);
            worst = Math.Min(worst, cutoff <= 0 ? double.PositiveInfinity : analysis.Sharpness / cutoff);
        }

        if (options.Sfw)
        {
            // Below the limit is fine; the ratio degrades as skin coverage climbs past it.
            worst = Math.Min(worst, analysis.SkinPercentage <= 0.0
                ? double.PositiveInfinity
                : SkinLimit / analysis.SkinPercentage);
        }

        return worst;
    }

    /// <summary>Names the check a frame failed, for verbose reporting.</summary>
    public static string DescribeRejection(FrameAnalysis analysis, ThumbnailOptions options)
    {
        var reasons = new List<string>();

        if (options.SkipBlank && analysis.Uniformity < BlankCutoff(options.BlankThreshold))
        {
            reasons.Add($"blank (spread {analysis.Uniformity:F1} < {BlankCutoff(options.BlankThreshold):F1})");
        }

        if (options.SkipBlurry && analysis.Sharpness < BlurCutoff(options.BlurThreshold))
        {
            reasons.Add($"blurry (sharpness {analysis.Sharpness:F0} < {BlurCutoff(options.BlurThreshold):F0})");
        }

        if (options.Sfw && analysis.SkinPercentage > SkinLimit)
        {
            reasons.Add($"not SFW (skin {analysis.SkinPercentage:F0}% > {SkinLimit:F0}%)");
        }

        return reasons.Count > 0 ? string.Join(", ", reasons) : "rejected";
    }

    /// <summary>Hamming distance between two fingerprints: 0 identical, 64 maximally different.</summary>
    public static int FingerprintDistance(ulong a, ulong b) =>
        System.Numerics.BitOperations.PopCount(a ^ b);

    /// <summary>
    /// Area-averaged downscale so the long edge fits <paramref name="maxEdge"/>, or null when
    /// the image is already small enough and can be analysed in place.
    /// </summary>
    /// <remarks>
    /// Averaging rather than sampling matters here: nearest-neighbour subsampling aliases high
    /// frequencies back in and would make blurry frames measure as sharp.
    /// </remarks>
    private static RgbaImage? Downscale(RgbaImage src, int maxEdge)
    {
        var longest = Math.Max(src.Width, src.Height);
        if (longest <= maxEdge)
        {
            return null;
        }

        var scale = maxEdge / (double)longest;
        var width = Math.Max(1, (int)(src.Width * scale));
        var height = Math.Max(1, (int)(src.Height * scale));

        var dst = new RgbaImage(width, height);

        for (int y = 0; y < height; y++)
        {
            var y0 = y * src.Height / height;
            var y1 = Math.Max(y0 + 1, (y + 1) * src.Height / height);
            var dstRow = dst.Row(y);

            for (int x = 0; x < width; x++)
            {
                var x0 = x * src.Width / width;
                var x1 = Math.Max(x0 + 1, (x + 1) * src.Width / width);

                int r = 0, g = 0, b = 0, count = 0;

                for (int sy = y0; sy < y1; sy++)
                {
                    var srcRow = src.Row(sy);
                    for (int sx = x0; sx < x1; sx++)
                    {
                        var i = sx * 4;
                        r += srcRow[i];
                        g += srcRow[i + 1];
                        b += srcRow[i + 2];
                        count++;
                    }
                }

                var o = x * 4;
                dstRow[o] = (byte)(r / count);
                dstRow[o + 1] = (byte)(g / count);
                dstRow[o + 2] = (byte)(b / count);
                dstRow[o + 3] = 255;
            }
        }

        return dst;
    }

    private static float[] ExtractLuma(RgbaImage image, double wr, double wg, double wb)
    {
        var luma = new float[image.Width * image.Height];

        for (int y = 0; y < image.Height; y++)
        {
            var row = image.Row(y);
            var offset = y * image.Width;

            for (int x = 0; x < image.Width; x++)
            {
                var i = x * 4;
                luma[offset + x] = (float)(row[i] * wr + row[i + 1] * wg + row[i + 2] * wb);
            }
        }

        return luma;
    }

    /// <summary>
    /// Mean and standard deviation in one pass.
    /// </summary>
    /// <remarks>
    /// v2 walked the whole frame twice — once for the mean, once for the variance — and did both
    /// accumulations in integer arithmetic, truncating the mean before squaring the differences.
    /// </remarks>
    private static (double Mean, double StdDev) MeanAndStdDev(float[] values)
    {
        if (values.Length == 0)
        {
            return (0, 0);
        }

        double sum = 0, sumSquares = 0;

        foreach (var v in values)
        {
            sum += v;
            sumSquares += (double)v * v;
        }

        var mean = sum / values.Length;
        var variance = Math.Max(0, (sumSquares / values.Length) - (mean * mean));

        return (mean, Math.Sqrt(variance));
    }

    /// <summary>
    /// Runs a Laplacian kernel over the luma and returns the variance of the response.
    /// </summary>
    private static double LaplacianVariance(float[] luma, int width, int height)
    {
        if (width < 3 || height < 3)
        {
            return 0;
        }

        var count = 0L;
        double sum = 0, sumSquares = 0;

        for (int y = 1; y < height - 1; y++)
        {
            var prev = (y - 1) * width;
            var curr = y * width;
            var next = (y + 1) * width;

            for (int x = 1; x < width - 1; x++)
            {
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

    private static double SkinPercentage(RgbaImage image)
    {
        var skinPixelCount = 0;
        var totalPixels = image.Width * image.Height;

        for (int y = 0; y < image.Height; y++)
        {
            var row = image.Row(y);

            for (int x = 0; x < image.Width; x++)
            {
                var i = x * 4;
                if (IsSkinTone(row[i], row[i + 1], row[i + 2]))
                {
                    skinPixelCount++;
                }
            }
        }

        return totalPixels == 0 ? 0 : skinPixelCount * 100.0 / totalPixels;
    }

    private static bool IsSkinTone(byte r, byte g, byte b)
    {
        // Simple RGB-rule skin tone detection. Crude, which is why --sfw is marked experimental.
        return r > 95 && g > 40 && b > 20 &&
               r > g && r > b &&
               Math.Abs(r - g) > 15 &&
               r - Math.Min(g, b) > 15;
    }

    /// <summary>
    /// 8x8 average hash: one bit per cell, set when that cell is brighter than the frame mean.
    /// </summary>
    /// <remarks>
    /// Robust to the compression and scaling differences that make exact pixel comparison
    /// useless, while staying cheap enough to run on every candidate frame.
    /// </remarks>
    private static ulong AverageHash(float[] luma, int width, int height)
    {
        const int Grid = 8;
        Span<double> cells = stackalloc double[Grid * Grid];
        Span<int> counts = stackalloc int[Grid * Grid];

        for (int y = 0; y < height; y++)
        {
            var cellY = Math.Min(Grid - 1, y * Grid / height);
            var offset = y * width;

            for (int x = 0; x < width; x++)
            {
                var cell = cellY * Grid + Math.Min(Grid - 1, x * Grid / width);
                cells[cell] += luma[offset + x];
                counts[cell]++;
            }
        }

        double total = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            if (counts[i] > 0)
            {
                cells[i] /= counts[i];
            }
            total += cells[i];
        }

        var mean = total / cells.Length;

        ulong hash = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] > mean)
            {
                hash |= 1UL << i;
            }
        }

        return hash;
    }
}
