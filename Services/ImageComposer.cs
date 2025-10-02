using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;
using nathanbutlerDEV.mt.net.Models;
using nathanbutlerDEV.mt.net.Utilities;

namespace nathanbutlerDEV.mt.net.Services;

public class ImageComposer
{
    public Image<Rgba32> CreateContactSheet(
        List<(Image<Rgba32> Image, TimeSpan Timestamp)> frames,
        HeaderInfo headerInfo,
        ThumbnailOptions options)
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("No frames provided for contact sheet");
        }

        // Calculate dimensions
        var thumbnailWidth = options.Width;
        var thumbnailHeight = options.Height > 0
            ? options.Height
            : (int)(frames[0].Image.Height * (thumbnailWidth / (double)frames[0].Image.Width));

        var columns = options.Columns;
        var rows = (int)Math.Ceiling(frames.Count / (double)columns);

        var padding = options.Padding;
        var border = options.Border;

        // Calculate canvas dimensions
        var contentWidth = (columns * thumbnailWidth) + ((columns + 1) * padding);
        var contentHeight = (rows * thumbnailHeight) + ((rows + 1) * padding);

        // Header dimensions
        var headerHeight = options.Header ? 100 : 0;
        var totalHeight = headerHeight + contentHeight;

        // Create canvas
        var bgContent = ColorParser.ParseRgb(options.BgContent);
        var canvas = new Image<Rgba32>(contentWidth, totalHeight);
        canvas.Mutate(ctx => ctx.Fill(bgContent));

        // Draw header if enabled
        if (options.Header)
        {
            DrawHeader(canvas, headerInfo, options, contentWidth, headerHeight);
        }

        // Draw thumbnails
        for (int i = 0; i < frames.Count; i++)
        {
            var (frame, timestamp) = frames[i];
            var row = i / columns;
            var col = i % columns;

            var x = padding + (col * (thumbnailWidth + padding));
            var y = headerHeight + padding + (row * (thumbnailHeight + padding));

            // Resize thumbnail
            var resizedFrame = frame.Clone(ctx => ctx.Resize(thumbnailWidth, thumbnailHeight));

            // Add timestamp if enabled
            if (!options.DisableTimestamps)
            {
                AddTimestamp(resizedFrame, timestamp, options);
            }

            // Draw border if specified
            if (border > 0)
            {
                resizedFrame.Mutate(ctx => ctx.Draw(
                    Color.White,
                    border,
                    new RectangleF(0, 0, resizedFrame.Width, resizedFrame.Height)
                ));
            }

            // Composite onto canvas
            canvas.Mutate(ctx => ctx.DrawImage(resizedFrame, new Point(x, y), 1f));

            resizedFrame.Dispose();
        }

        return canvas;
    }

    private void DrawHeader(
        Image<Rgba32> canvas,
        HeaderInfo headerInfo,
        ThumbnailOptions options,
        int width,
        int height)
    {
        var bgHeader = ColorParser.ParseRgb(options.BgHeader);
        var fgHeader = ColorParser.ParseRgb(options.FgHeader);

        // Fill header background
        canvas.Mutate(ctx => ctx.Fill(bgHeader, new RectangleF(0, 0, width, height)));

        // Prepare header text
        var filename = headerInfo.Filename;
        var dimensions = $"{headerInfo.Width}x{headerInfo.Height}";
        var durationStr = FormatDuration(headerInfo.Duration);
        var fileSize = FormatFileSize(headerInfo.FileSize);

        var headerText = $"{filename}\n{dimensions} | {durationStr} | {fileSize}";

        // Add metadata if requested
        if (options.HeaderMeta)
        {
            var codec = headerInfo.VideoCodec;
            var fps = $"{headerInfo.FrameRate:F2} fps";
            var bitrate = $"{(headerInfo.BitRate / 1000)} kbps";
            headerText += $"\n{codec} | {fps} | {bitrate}";
        }

        // Add comment if provided
        if (!string.IsNullOrEmpty(options.Comment))
        {
            headerText += $"\n{options.Comment}";
        }

        // Draw text
        try
        {
            var font = SystemFonts.CreateFont("Arial", options.FontSize);
            var textOptions = new RichTextOptions(font)
            {
                Origin = new PointF(10, 10),
                WrappingLength = width - 20,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            canvas.Mutate(ctx => ctx.DrawText(textOptions, headerText, fgHeader));
        }
        catch
        {
            // Fallback if font not available
            Console.WriteLine("Warning: Could not load font for header text");
        }
    }

    private void AddTimestamp(
        Image<Rgba32> image,
        TimeSpan timestamp,
        ThumbnailOptions options)
    {
        var timestampText = FormatTimestamp(timestamp);
        var opacity = (float)options.TimestampOpacity;

        try
        {
            var font = SystemFonts.CreateFont("Arial", options.FontSize);
            var textOptions = new RichTextOptions(font)
            {
                Origin = new PointF(5, image.Height - options.FontSize - 5),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            // Draw timestamp with semi-transparent background
            var backgroundColor = new Rgba32(0, 0, 0, (byte)(180 * opacity));
            var textColor = new Rgba32(255, 255, 255, (byte)(255 * opacity));

            image.Mutate(ctx =>
            {
                // Measure text to draw background
                var textBounds = TextMeasurer.MeasureSize(timestampText, textOptions);
                var bgRect = new RectangleF(
                    textOptions.Origin.X - 2,
                    textOptions.Origin.Y - 2,
                    textBounds.Width + 4,
                    textBounds.Height + 4
                );

                ctx.Fill(backgroundColor, bgRect);
                ctx.DrawText(textOptions, timestampText, textColor);
            });
        }
        catch
        {
            // Fallback if font not available
            Console.WriteLine("Warning: Could not load font for timestamp");
        }
    }

    public void ApplyWatermark(
        Image<Rgba32> image,
        string watermarkPath,
        bool center = true)
    {
        if (string.IsNullOrEmpty(watermarkPath) || !File.Exists(watermarkPath))
        {
            return;
        }

        try
        {
            using var watermark = Image.Load<Rgba32>(watermarkPath);

            var position = center
                ? new Point(
                    (image.Width - watermark.Width) / 2,
                    (image.Height - watermark.Height) / 2)
                : new Point(0, 0);

            image.Mutate(ctx => ctx.DrawImage(watermark, position, 0.7f));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error applying watermark: {ex.Message}");
        }
    }

    private string FormatDuration(TimeSpan duration)
    {
        return duration.Hours > 0
            ? $"{duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes:D2}:{duration.Seconds:D2}";
    }

    private string FormatTimestamp(TimeSpan timestamp)
    {
        return timestamp.Hours > 0
            ? $"{timestamp.Hours:D2}:{timestamp.Minutes:D2}:{timestamp.Seconds:D2}"
            : $"{timestamp.Minutes:D2}:{timestamp.Seconds:D2}";
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}
