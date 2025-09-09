namespace MtNet.Models;

public class HeaderInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FileSize { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public string Fps { get; set; } = string.Empty;
    public string Bitrate { get; set; } = string.Empty;
    public string VideoCodec { get; set; } = string.Empty;
    public string AudioCodec { get; set; } = string.Empty;
    public string? Comment { get; set; }
}