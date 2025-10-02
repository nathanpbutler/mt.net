using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace nathanbutlerDEV.mt.net.Services;

public class FilterService
{
    private static readonly Random Random = new Random();

    public void ApplyFilters(Image<Rgba32> image, string filterString)
    {
        if (string.IsNullOrEmpty(filterString) || filterString.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var filters = filterString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var filter in filters)
        {
            ApplyFilter(image, filter);
        }
    }

    private void ApplyFilter(Image<Rgba32> image, string filterName)
    {
        switch (filterName.ToLowerInvariant())
        {
            case "greyscale":
            case "grayscale":
                ApplyGreyscale(image);
                break;

            case "sepia":
                ApplySepia(image);
                break;

            case "invert":
                ApplyInvert(image);
                break;

            case "fancy":
                ApplyFancy(image);
                break;

            case "cross":
                ApplyCrossProcessing(image);
                break;

            case "strip":
                ApplyStrip(image);
                break;

            default:
                Console.WriteLine($"Warning: Unknown filter '{filterName}' - skipping");
                break;
        }
    }

    private void ApplyGreyscale(Image<Rgba32> image)
    {
        image.Mutate(ctx => ctx.Grayscale());
    }

    private void ApplySepia(Image<Rgba32> image)
    {
        image.Mutate(ctx => ctx.Sepia());
    }

    private void ApplyInvert(Image<Rgba32> image)
    {
        image.Mutate(ctx => ctx.Invert());
    }

    private void ApplyFancy(Image<Rgba32> image)
    {
        // Randomly rotate the image
        var rotationAngle = Random.Next(-15, 16); // Random angle between -15 and 15 degrees
        image.Mutate(ctx => ctx.Rotate(rotationAngle));
    }

    private void ApplyCrossProcessing(Image<Rgba32> image)
    {
        // Cross processing effect - shift colors and adjust curves
        image.Mutate(ctx =>
        {
            ctx.Hue(30); // Shift hue
            ctx.Saturate(1.2f); // Increase saturation
            ctx.Contrast(1.1f); // Increase contrast slightly
        });
    }

    private void ApplyStrip(Image<Rgba32> image)
    {
        // Film strip effect - add sprocket holes on sides
        var sprocketWidth = image.Width / 20;
        var sprocketHeight = sprocketWidth;
        var sprocketSpacing = sprocketHeight * 2;

        image.Mutate(ctx =>
        {
            // Draw black borders on left and right
            ctx.Fill(Color.Black, new RectangleF(0, 0, sprocketWidth, image.Height));
            ctx.Fill(Color.Black, new RectangleF(image.Width - sprocketWidth, 0, sprocketWidth, image.Height));

            // Draw sprocket holes
            var holeRadius = sprocketWidth / 3;
            var holeX = sprocketWidth / 2;

            for (int y = sprocketHeight / 2; y < image.Height; y += sprocketSpacing)
            {
                // Left sprocket holes
                ctx.FillPolygon(Color.White, new PointF[] {
                    new PointF(holeX, y - holeRadius),
                    new PointF(holeX + holeRadius, y),
                    new PointF(holeX, y + holeRadius),
                    new PointF(holeX - holeRadius, y)
                });

                // Right sprocket holes
                ctx.FillPolygon(Color.White, new PointF[] {
                    new PointF(image.Width - holeX, y - holeRadius),
                    new PointF(image.Width - holeX + holeRadius, y),
                    new PointF(image.Width - holeX, y + holeRadius),
                    new PointF(image.Width - holeX - holeRadius, y)
                });
            }
        });
    }
}
