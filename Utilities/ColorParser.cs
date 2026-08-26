namespace nathanbutlerDEV.mt.net.Utilities;

/// <summary>
/// Parses the "R,G,B" colour strings accepted by --bg-content, --bg-header and --fg-header.
/// </summary>
public static class ColorParser
{
    public static RgbaColor ParseRgb(string rgbString, RgbaColor defaultColor = default)
    {
        return ParseRgbString(rgbString, defaultColor);
    }

    public static RgbaColor ParseRgbString(string rgbString, RgbaColor defaultColor = default)
    {
        if (string.IsNullOrWhiteSpace(rgbString))
            return defaultColor;

        var parts = rgbString.Split(',');
        if (parts.Length != 3)
            return defaultColor;

        if (byte.TryParse(parts[0].Trim(), out var r) &&
            byte.TryParse(parts[1].Trim(), out var g) &&
            byte.TryParse(parts[2].Trim(), out var b))
        {
            return new RgbaColor(r, g, b);
        }

        return defaultColor;
    }
}
