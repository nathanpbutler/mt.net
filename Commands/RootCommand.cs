using System.CommandLine;
using MtNet.Models;
using MtNet.Utilities;

namespace MtNet.Commands;

public static class RootCommandBuilder
{
    public static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("mt.net - Media Thumbnailing Tool");

        // File argument (required) - the video file to process
        var fileArgument = new Argument<FileInfo>("file")
        {
            Description = "Video file to process"
        };

        // Global/Config options
        var configOption = new Option<FileInfo?>("--config")
        {
            Description = "Configuration file path"
        };
        configOption.Aliases.Add("-c");

        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Enable verbose logging"
        };
        verboseOption.Aliases.Add("-v");

        var versionOption = new Option<bool>("--version")
        {
            Description = "Show version information"
        };

        // Basic thumbnail options
        var numCapsOption = new Option<int>("--numcaps")
        {
            Description = "Number of captures to make",
            DefaultValueFactory = _ => 4
        };
        numCapsOption.Aliases.Add("-n");

        var columnsOption = new Option<int>("--columns")
        {
            Description = "Number of columns in output",
            DefaultValueFactory = _ => 2
        };
        columnsOption.Aliases.Add("-c");

        var widthOption = new Option<int>("--width")
        {
            Description = "Width of individual thumbnails in pixels",
            DefaultValueFactory = _ => 400
        };
        widthOption.Aliases.Add("-w");

        var heightOption = new Option<int>("--height")
        {
            Description = "Height of individual thumbnails in pixels (0 = auto)",
            DefaultValueFactory = _ => 0
        };

        var paddingOption = new Option<int>("--padding")
        {
            Description = "Padding between images in pixels",
            DefaultValueFactory = _ => 10
        };
        paddingOption.Aliases.Add("-p");

        var outputOption = new Option<string>("--output")
        {
            Description = "Output filename pattern",
            DefaultValueFactory = _ => "{{.Path}}{{.Name}}.jpg"
        };
        outputOption.Aliases.Add("-o");

        // Time options
        var fromOption = new Option<string>("--from")
        {
            Description = "Start time for captures (HH:MM:SS)",
            DefaultValueFactory = _ => "00:00:00"
        };

        var toOption = new Option<string>("--to")
        {
            Description = "End time for captures (HH:MM:SS)",
            DefaultValueFactory = _ => "00:00:00"
        };
        toOption.Aliases.Add("--end");

        var intervalOption = new Option<int>("--interval")
        {
            Description = "Interval between captures in seconds (overrides numcaps)",
            DefaultValueFactory = _ => 0
        };
        intervalOption.Aliases.Add("-i");

        // Output control options
        var singleImagesOption = new Option<bool>("--single-images")
        {
            Description = "Save individual images instead of contact sheet"
        };
        singleImagesOption.Aliases.Add("-s");

        var overwriteOption = new Option<bool>("--overwrite")
        {
            Description = "Overwrite existing files"
        };

        var skipExistingOption = new Option<bool>("--skip-existing")
        {
            Description = "Skip processing if output already exists"
        };

        // Visual options
        var fontOption = new Option<string>("--font")
        {
            Description = "Font to use for timestamps and header",
            DefaultValueFactory = _ => "DroidSans.ttf"
        };
        fontOption.Aliases.Add("-f");

        var fontSizeOption = new Option<int>("--font-size")
        {
            Description = "Font size in pixels",
            DefaultValueFactory = _ => 12
        };

        var disableTimestampsOption = new Option<bool>("--disable-timestamps")
        {
            Description = "Disable timestamp overlay on images"
        };
        disableTimestampsOption.Aliases.Add("-d");

        var timestampOpacityOption = new Option<double>("--timestamp-opacity")
        {
            Description = "Opacity of timestamp text (0.0-1.0)",
            DefaultValueFactory = _ => 1.0
        };

        var headerOption = new Option<bool>("--header")
        {
            Description = "Include header with file information",
            DefaultValueFactory = _ => true
        };

        var headerMetaOption = new Option<bool>("--header-meta")
        {
            Description = "Include codec, FPS, and bitrate in header"
        };

        var headerImageOption = new Option<string>("--header-image")
        {
            Description = "Image to display in header"
        };

        // Color options
        var bgContentOption = new Option<string>("--bg-content")
        {
            Description = "Background color for content area (R,G,B)",
            DefaultValueFactory = _ => "0,0,0"
        };

        var bgHeaderOption = new Option<string>("--bg-header")
        {
            Description = "Background color for header (R,G,B)",
            DefaultValueFactory = _ => "0,0,0"
        };

        var fgHeaderOption = new Option<string>("--fg-header")
        {
            Description = "Text color for header (R,G,B)",
            DefaultValueFactory = _ => "255,255,255"
        };

        var borderOption = new Option<int>("--border")
        {
            Description = "Border width around thumbnails",
            DefaultValueFactory = _ => 0
        };

        // Watermark options
        var watermarkOption = new Option<string>("--watermark")
        {
            Description = "Watermark image for center thumbnail"
        };

        var watermarkAllOption = new Option<string>("--watermark-all")
        {
            Description = "Watermark image for all thumbnails"
        };

        // Filter options
        var filterOption = new Option<string>("--filter")
        {
            Description = "Image filters to apply (comma-separated): none, greyscale, invert, sepia, fancy, cross, strip",
            DefaultValueFactory = _ => "none"
        };

        var filtersOption = new Option<bool>("--filters")
        {
            Description = "List all available image filters"
        };

        // Processing options
        var skipBlankOption = new Option<bool>("--skip-blank")
        {
            Description = "Skip blank frames (up to 3 retries)"
        };
        skipBlankOption.Aliases.Add("-b");

        var skipBlurryOption = new Option<bool>("--skip-blurry")
        {
            Description = "Skip blurry frames (up to 3 retries)"
        };

        var skipCreditsOption = new Option<bool>("--skip-credits")
        {
            Description = "Skip end credits by cutting off last 2 minutes or 10%"
        };

        var fastOption = new Option<bool>("--fast")
        {
            Description = "Use fast but less accurate seeking"
        };

        var sfwOption = new Option<bool>("--sfw")
        {
            Description = "Use content filtering for safe-for-work output (experimental)"
        };

        // WebVTT options
        var vttOption = new Option<bool>("--vtt")
        {
            Description = "Generate WebVTT file for HTML5 video players"
        };

        var webVttOption = new Option<bool>("--webvtt")
        {
            Description = "Generate WebVTT with disabled headers, padding, and timestamps"
        };

        // Upload options
        var uploadOption = new Option<bool>("--upload")
        {
            Description = "Upload generated files via HTTP"
        };

        var uploadUrlOption = new Option<string>("--upload-url")
        {
            Description = "URL for file upload",
            DefaultValueFactory = _ => "http://example.com/upload"
        };

        // Threshold options
        var blurThresholdOption = new Option<int>("--blur-threshold")
        {
            Description = "Threshold for blur detection (0-100)",
            DefaultValueFactory = _ => 62
        };

        var blankThresholdOption = new Option<int>("--blank-threshold")
        {
            Description = "Threshold for blank frame detection (0-100)",
            DefaultValueFactory = _ => 85
        };

        // Comment option
        var commentOption = new Option<string>("--comment")
        {
            Description = "Comment to add to header",
            DefaultValueFactory = _ => "contactsheet created with mt.net"
        };

        // Configuration options
        var saveConfigOption = new Option<string>("--save-config")
        {
            Description = "Save current settings to configuration file"
        };

        var configFileOption = new Option<string>("--config-file")
        {
            Description = "Use specific configuration file"
        };

        var showConfigOption = new Option<bool>("--show-config")
        {
            Description = "Show configuration file path and values, then exit"
        };

        // Add argument and all options to root command
        rootCommand.Arguments.Add(fileArgument);
        
        // Global options
        rootCommand.Options.Add(configOption);
        rootCommand.Options.Add(verboseOption);
        rootCommand.Options.Add(versionOption);
        
        // Basic options
        rootCommand.Options.Add(numCapsOption);
        rootCommand.Options.Add(columnsOption);
        rootCommand.Options.Add(widthOption);
        rootCommand.Options.Add(heightOption);
        rootCommand.Options.Add(paddingOption);
        rootCommand.Options.Add(outputOption);
        
        // Time options
        rootCommand.Options.Add(fromOption);
        rootCommand.Options.Add(toOption);
        rootCommand.Options.Add(intervalOption);
        
        // Output control
        rootCommand.Options.Add(singleImagesOption);
        rootCommand.Options.Add(overwriteOption);
        rootCommand.Options.Add(skipExistingOption);
        
        // Visual customization
        rootCommand.Options.Add(fontOption);
        rootCommand.Options.Add(fontSizeOption);
        rootCommand.Options.Add(disableTimestampsOption);
        rootCommand.Options.Add(timestampOpacityOption);
        rootCommand.Options.Add(headerOption);
        rootCommand.Options.Add(headerMetaOption);
        rootCommand.Options.Add(headerImageOption);
        
        // Colors and styling
        rootCommand.Options.Add(bgContentOption);
        rootCommand.Options.Add(bgHeaderOption);
        rootCommand.Options.Add(fgHeaderOption);
        rootCommand.Options.Add(borderOption);
        
        // Watermarks
        rootCommand.Options.Add(watermarkOption);
        rootCommand.Options.Add(watermarkAllOption);
        
        // Filters
        rootCommand.Options.Add(filterOption);
        rootCommand.Options.Add(filtersOption);
        
        // Processing
        rootCommand.Options.Add(skipBlankOption);
        rootCommand.Options.Add(skipBlurryOption);
        rootCommand.Options.Add(skipCreditsOption);
        rootCommand.Options.Add(fastOption);
        rootCommand.Options.Add(sfwOption);
        
        // WebVTT
        rootCommand.Options.Add(vttOption);
        rootCommand.Options.Add(webVttOption);
        
        // Upload
        rootCommand.Options.Add(uploadOption);
        rootCommand.Options.Add(uploadUrlOption);
        
        // Thresholds
        rootCommand.Options.Add(blurThresholdOption);
        rootCommand.Options.Add(blankThresholdOption);
        
        // Misc
        rootCommand.Options.Add(commentOption);
        rootCommand.Options.Add(saveConfigOption);
        rootCommand.Options.Add(configFileOption);
        rootCommand.Options.Add(showConfigOption);

        // Set the action for the root command (same logic as generate command)
        rootCommand.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArgument);
            
            // Handle special actions first
            if (parseResult.GetValue(filtersOption))
            {
                ShowAvailableFilters();
                return 0;
            }

            if (parseResult.GetValue(showConfigOption))
            {
                // TODO: Show configuration info
                Console.WriteLine("Configuration display not yet implemented.");
                return 0;
            }

            // Build comprehensive options object
            var options = new ThumbnailOptions
            {
                // Basic options
                NumCaps = parseResult.GetValue(numCapsOption),
                Columns = parseResult.GetValue(columnsOption),
                Width = parseResult.GetValue(widthOption),
                Height = parseResult.GetValue(heightOption),
                Padding = parseResult.GetValue(paddingOption),
                Filename = parseResult.GetValue(outputOption)!,
                
                // Time options
                From = parseResult.GetValue(fromOption)!,
                End = parseResult.GetValue(toOption)!,
                Interval = parseResult.GetValue(intervalOption),
                
                // Output control
                SingleImages = parseResult.GetValue(singleImagesOption),
                Overwrite = parseResult.GetValue(overwriteOption),
                SkipExisting = parseResult.GetValue(skipExistingOption),
                
                // Visual customization
                FontPath = parseResult.GetValue(fontOption)!,
                FontSize = parseResult.GetValue(fontSizeOption),
                DisableTimestamps = parseResult.GetValue(disableTimestampsOption),
                TimestampOpacity = parseResult.GetValue(timestampOpacityOption),
                Header = parseResult.GetValue(headerOption),
                HeaderMeta = parseResult.GetValue(headerMetaOption),
                HeaderImage = parseResult.GetValue(headerImageOption) ?? "",
                
                // Colors and styling  
                BgContent = parseResult.GetValue(bgContentOption)!,
                BgHeader = parseResult.GetValue(bgHeaderOption)!,
                FgHeader = parseResult.GetValue(fgHeaderOption)!,
                Border = parseResult.GetValue(borderOption),
                
                // Watermarks
                Watermark = parseResult.GetValue(watermarkOption) ?? "",
                WatermarkAll = parseResult.GetValue(watermarkAllOption) ?? "",
                
                // Filters
                Filter = parseResult.GetValue(filterOption)!,
                
                // Processing
                SkipBlank = parseResult.GetValue(skipBlankOption),
                SkipBlurry = parseResult.GetValue(skipBlurryOption),
                SkipCredits = parseResult.GetValue(skipCreditsOption),
                Fast = parseResult.GetValue(fastOption),
                Sfw = parseResult.GetValue(sfwOption),
                
                // WebVTT
                Vtt = parseResult.GetValue(vttOption),
                WebVtt = parseResult.GetValue(webVttOption),
                
                // Upload
                Upload = parseResult.GetValue(uploadOption),
                UploadUrl = parseResult.GetValue(uploadUrlOption)!,
                
                // Thresholds
                BlurThreshold = parseResult.GetValue(blurThresholdOption),
                BlankThreshold = parseResult.GetValue(blankThresholdOption),
                
                // Misc
                Comment = parseResult.GetValue(commentOption)!
            };

            // Handle save config
            var saveConfigPath = parseResult.GetValue(saveConfigOption);
            if (!string.IsNullOrEmpty(saveConfigPath))
            {
                // TODO: Save configuration to file
                Console.WriteLine($"Saving configuration to: {saveConfigPath}");
            }

            Console.WriteLine($"Processing video file: {file?.FullName}");
            Console.WriteLine($"Options configured: NumCaps={options.NumCaps}, Columns={options.Columns}, Width={options.Width}");
            Console.WriteLine($"Time range: {options.From} to {options.End}, Interval: {options.Interval}s");
            Console.WriteLine($"Output: {options.Filename}, Single images: {options.SingleImages}");
            Console.WriteLine($"Filters: {options.Filter}, Skip blank: {options.SkipBlank}, Skip blurry: {options.SkipBlurry}");
            Console.WriteLine("Video processing not yet implemented.");

            return 0;
        });

        return rootCommand;
    }

    private static void ShowAvailableFilters()
    {
        var filtersHelp = @"Available image filters:

| NAME      | DESCRIPTION                     |
| --------- | --------------------------------|
| none      | No filter applied               |
| invert    | Invert colors                   |
| greyscale | Convert to greyscale image      |
| sepia     | Convert to sepia image          |
| fancy     | Randomly rotates every image    |
| cross     | Simulated cross processing      |
| strip     | Simulate an old 35mm Film strip |

You can stack multiple filters by separating them with a comma
Example:
    --filter=cross,fancy

NOTE: fancy has best results if it is applied as last filter!";

        Console.WriteLine(filtersHelp);
    }
}