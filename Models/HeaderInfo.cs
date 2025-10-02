namespace nathanbutlerDEV.mt.net.Models;

public class HeaderInfo
{
    public string Filename { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public TimeSpan Duration { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string VideoCodec { get; set; } = string.Empty;
    public string AudioCodec { get; set; } = string.Empty;
    public double FrameRate { get; set; }
    public long BitRate { get; set; }
    public string Format { get; set; } = string.Empty;
    public string? Comment { get; set; }
}