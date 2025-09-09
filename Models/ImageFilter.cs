namespace MtNet.Models;

public enum ImageFilter
{
    None,
    Greyscale,
    Invert,
    Fancy,
    Cross,
    Strip,
    Sepia
}

public static class ImageFilterExtensions
{
    public static ImageFilter ParseFilter(string filterName)
    {
        return filterName.ToLowerInvariant() switch
        {
            "greyscale" or "grayscale" => ImageFilter.Greyscale,
            "invert" => ImageFilter.Invert,
            "fancy" => ImageFilter.Fancy,
            "cross" => ImageFilter.Cross,
            "strip" => ImageFilter.Strip,
            "sepia" => ImageFilter.Sepia,
            "none" or "" => ImageFilter.None,
            _ => throw new ArgumentException($"Unknown filter: {filterName}")
        };
    }
}