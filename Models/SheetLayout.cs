namespace nathanbutlerDEV.mt.net.Models;

/// <summary>
/// The geometry a contact sheet was actually composed with.
/// </summary>
/// <remarks>
/// Before v3 three places computed this independently and disagreed: the composer's
/// pre-render <c>CalculateHeaderHeight</c>, the composer's actual rendered header, and
/// <c>OutputService.GenerateWebVttAsync</c>'s own formula. The result was WebVTT sprite offsets
/// that missed the thumbnails by the difference (~15px on a default sheet), and a grid that
/// misaligned whenever <c>--v360</c> resized frames behind the layout's back.
///
/// The composer now returns this record built from the values it really used, and every consumer
/// reads it instead of recomputing. Deliberately free of any FFmpeg dependency so the geometry
/// can be unit tested on its own.
/// </remarks>
/// <param name="HeaderHeight">Rendered header height in pixels; 0 when the header is disabled.</param>
/// <param name="ThumbnailWidth">Width of each composed thumbnail, after scaling or v360.</param>
/// <param name="ThumbnailHeight">Height of each composed thumbnail, after scaling or v360.</param>
/// <param name="Columns">Number of grid columns.</param>
/// <param name="Rows">Number of grid rows.</param>
/// <param name="Padding">Gap between thumbnails, and the outer margin.</param>
public sealed record SheetLayout(
    int HeaderHeight,
    int ThumbnailWidth,
    int ThumbnailHeight,
    int Columns,
    int Rows,
    int Padding)
{
    /// <summary>Total canvas width, including the outer margins.</summary>
    public int ContentWidth => (Columns * ThumbnailWidth) + ((Columns + 1) * Padding);

    /// <summary>Total canvas height, header included.</summary>
    public int TotalHeight => HeaderHeight + (Rows * ThumbnailHeight) + ((Rows + 1) * Padding);

    /// <summary>Left edge of the thumbnail at <paramref name="index"/> in reading order.</summary>
    public int ThumbnailX(int index) =>
        Padding + (index % Columns) * (ThumbnailWidth + Padding);

    /// <summary>Top edge of the thumbnail at <paramref name="index"/> in reading order.</summary>
    public int ThumbnailY(int index) =>
        HeaderHeight + Padding + (index / Columns) * (ThumbnailHeight + Padding);
}
