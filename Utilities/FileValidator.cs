namespace MtNet.Utilities;

public static class FileValidator
{
    private static readonly string[] SupportedVideoExtensions = {
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", 
        ".mpg", ".mpeg", ".3gp", ".ogv", ".asf", ".rm", ".rmvb"
    };

    public static bool IsVideoFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedVideoExtensions.Contains(extension);
    }

    public static bool FileExists(string filePath)
    {
        return File.Exists(filePath);
    }

    public static void ValidateVideoFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be empty");

        if (!FileExists(filePath))
            throw new FileNotFoundException($"File does not exist: {filePath}");

        if (!IsVideoFile(filePath))
            throw new ArgumentException($"File is not a supported video format: {filePath}");
    }

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