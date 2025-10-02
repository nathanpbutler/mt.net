using SixLabors.ImageSharp;

namespace nathanbutlerDEV.mt.net.Utilities;

public static class ColorParser
{
    public static Color ParseRgb(string rgbString, Color defaultColor = default)
    {
        return ParseRgbString(rgbString, defaultColor);
    }

    public static Color ParseRgbString(string rgbString, Color defaultColor = default)
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
            return Color.FromRgb(r, g, b);
        }

        return defaultColor;
    }

    public static Color ParseColorName(string colorName)
    {
        return colorName.ToLowerInvariant() switch
        {
            "black" => Color.Black,
            "white" => Color.White,
            "red" => Color.Red,
            "green" => Color.Green,
            "blue" => Color.Blue,
            "yellow" => Color.Yellow,
            "cyan" => Color.Cyan,
            "magenta" => Color.Magenta,
            "gray" or "grey" => Color.Gray,
            "darkgray" or "darkgrey" => Color.DarkGray,
            "lightgray" or "lightgrey" => Color.LightGray,
            _ => Color.Black
        };
    }
}