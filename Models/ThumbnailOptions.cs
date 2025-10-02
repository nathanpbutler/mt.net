using System.Drawing;

namespace nathanbutlerDEV.mt.net.Models;

public class ThumbnailOptions
{
    public int NumCaps { get; set; } = 4;
    public int Columns { get; set; } = 2;
    public int Padding { get; set; } = 10;
    public int Width { get; set; } = 400;
    public int Height { get; set; } = 0;
    public string FontPath { get; set; } = "DroidSans.ttf";
    public int FontSize { get; set; } = 12;
    public bool DisableTimestamps { get; set; } = false;
    public double TimestampOpacity { get; set; } = 1.0;
    public string Filename { get; set; } = "{{.Path}}{{.Name}}.jpg";
    public bool Verbose { get; set; } = false;
    public string BgContent { get; set; } = "0,0,0";
    public string BgHeader { get; set; } = "0,0,0";
    public string FgHeader { get; set; } = "255,255,255";
    public int Border { get; set; } = 0;
    public string From { get; set; } = "00:00:00";
    public string End { get; set; } = "00:00:00";
    public bool SingleImages { get; set; } = false;
    public bool Header { get; set; } = true;
    public string HeaderImage { get; set; } = "";
    public bool HeaderMeta { get; set; } = false;
    public string Watermark { get; set; } = "";
    public string WatermarkAll { get; set; } = "";
    public string Comment { get; set; } = "contactsheet created with mt.net";
    public string Filter { get; set; } = "none";
    public bool SkipBlank { get; set; } = false;
    public bool SkipBlurry { get; set; } = false;
    public bool SkipExisting { get; set; } = false;
    public bool Overwrite { get; set; } = false;
    public bool Sfw { get; set; } = false;
    public bool Fast { get; set; } = false;
    public bool ShowConfig { get; set; } = false;
    public bool Vtt { get; set; } = false;
    public bool WebVtt { get; set; } = false;
    public int BlurThreshold { get; set; } = 62;
    public int BlankThreshold { get; set; } = 85;
    public bool Upload { get; set; } = false;
    public string UploadUrl { get; set; } = "http://example.com/upload";
    public bool SkipCredits { get; set; } = false;
    public int Interval { get; set; } = 0;
}