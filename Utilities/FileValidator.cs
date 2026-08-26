namespace nathanbutlerDEV.mt.net.Utilities;

/// <summary>
/// The single authority on which files mt.net will pick up when expanding a directory or glob.
/// </summary>
/// <remarks>
/// Until v3 this list and <c>VideoProcessor.SupportedExtensions</c> both existed and disagreed
/// (15 entries versus 10). <c>VideoProcessor</c>'s copy was never read; this one is now the only
/// list, and it is deliberately the more generous of the two.
/// </remarks>
public static class FileValidator
{
    private static readonly HashSet<string> SupportedVideoExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v",
            ".mpg", ".mpeg", ".3gp", ".ogv", ".asf", ".rm", ".rmvb", ".ts", ".m2ts"
        };

    /// <summary>True when the path's extension is one mt.net will pick up during expansion.</summary>
    public static bool IsVideoFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        return SupportedVideoExtensions.Contains(Path.GetExtension(filePath));
    }

    /// <summary>Creates the parent directory of <paramref name="filePath"/> if it is missing.</summary>
    public static string EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return filePath;
    }
}
