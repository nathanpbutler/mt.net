using System.CommandLine;
using System.CommandLine.Completions;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using nathanbutlerDEV.mt.net.Models;
using nathanbutlerDEV.mt.net.Services;
using nathanbutlerDEV.mt.net.Utilities;

namespace nathanbutlerDEV.mt.net.Commands;

public static class RootCommandBuilder
{
    /// <summary>
    /// Creates the root command with all options and arguments.
    /// </summary>
    /// <returns>The configured RootCommand instance.</returns>
    public static RootCommand CreateRootCommand()
    {
        // Create root command
        var rootCommand = new RootCommand("mt.net - Media Thumbnailing Tool");

        // Remove "-h" alias from help to avoid conflict with height option
        for (int i = 0; i < rootCommand.Options.Count; i++)
        {
            if (rootCommand.Options[i] is HelpOption helpOption)
            {
                helpOption.Aliases.Remove("-h");
                break;
            }
        }

        // File argument (required) - the video file to process
        var fileArgument = new Argument<FileInfo>("file")
        {
            Description = "Video file to process",
            Arity = ArgumentArity.ExactlyOne
        };

        // Basic Options
        var numCapsOption = new Option<int>("--numcaps", ["-n"])
        {
            Description = "Number of captures to make",
            DefaultValueFactory = _ => 4
        };

        var columnsOption = new Option<int>("--columns", ["-c"])
        {
            Description = "Number of columns in output",
            DefaultValueFactory = _ => 2
        };

        var widthOption = new Option<int>("--width", ["-w"])
        {
            Description = "Width of individual thumbnails in pixels",
            DefaultValueFactory = _ => 400
        };

        var heightOption = new Option<int>("--height", ["-h"])
        {
            Description = "Height of individual thumbnails in pixels (0 = auto)",
            DefaultValueFactory = _ => 0
        };

        var paddingOption = new Option<int>("--padding", ["-p"])
        {
            Description = "Padding between images in pixels",
            DefaultValueFactory = _ => 10
        };

        var outputOption = new Option<string>("--output", ["-o"])
        {
            Description = "Output filename pattern",
            DefaultValueFactory = _ => "{{.Path}}{{.Name}}.jpg"
        };

        outputOption.CompletionSources.Add(ctx =>
        {
            var file = ctx.ParseResult.GetValue(fileArgument); // Interesting...
            if (file != null)
            {
                var directory = file.DirectoryName ?? ".";
                return [new CompletionItem(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(file.Name)}.jpg"))];
            }
            return [];
        });

        // Time Options
        var intervalOption = new Option<int>("--interval")
        {
            Description = "Interval between captures in seconds (overrides numcaps)",
            DefaultValueFactory = _ => 0
        };
        intervalOption.Aliases.Add("-i");

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

        var skipCreditsOption = new Option<bool>("--skip-credits")
        {
            Description = "Skip end credits by cutting off last 2 minutes or 10%"
        };

        // Visual Options
        var filterOption = new Option<string>("--filter")
        {
            Description = "Image filters to apply (comma-separated): none, greyscale, invert, sepia, fancy, cross, strip",
            DefaultValueFactory = _ => "none"
        };

        var fontOption = new Option<string>("--font")
        {
            Description = "Font to use for timestamps and header",
            DefaultValueFactory = _ => "DroidSans"
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

        var commentOption = new Option<string>("--comment")
        {
            Description = "Comment to add to header",
            DefaultValueFactory = _ => "contactsheet created with mt.net (https://github.com/nathanpbutler/mt.net)"
        };

        // Processing Options
        var skipBlankOption = new Option<bool>("--skip-blank")
        {
            Description = "Skip blank frames (up to 3 retries)"
        };
        skipBlankOption.Aliases.Add("-b");

        var skipBlurryOption = new Option<bool>("--skip-blurry")
        {
            Description = "Skip blurry frames (up to 3 retries)"
        };

        var fastOption = new Option<bool>("--fast")
        {
            Description = "Use fast but less accurate seeking"
        };

        var sfwOption = new Option<bool>("--sfw")
        {
            Description = "Use content filtering for safe-for-work output (experimental)"
        };

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

        // Output Options
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

        var vttOption = new Option<bool>("--vtt")
        {
            Description = "Generate WebVTT file for HTML5 video players"
        };

        var webVttOption = new Option<bool>("--webvtt")
        {
            Description = "Generate WebVTT with disabled headers, padding, and timestamps"
        };

        // Upload Options
        var uploadOption = new Option<bool>("--upload")
        {
            Description = "Upload generated files via HTTP"
        };

        var uploadUrlOption = new Option<string>("--upload-url")
        {
            Description = "URL for file upload",
            DefaultValueFactory = _ => "http://example.com/upload"
        };

        // Configuration Options
        var configOption = new Option<FileInfo>("--config")
        {
            Description = "Configuration file path",
            Arity = ArgumentArity.ExactlyOne
        };
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

        // Global Options
        var composerOption = new Option<string>("--composer")
        {
            Description = "Choose image composer: ffmpeg, imagesharp",
            // CompletionSources = { "ffmpeg", "imagesharp" },
            DefaultValueFactory = _ => "ffmpeg" // FFMpeg.AutoGen is now default
        };
        composerOption.CompletionSources.Add(ctx =>
        {
            return [new CompletionItem("ffmpeg"), new CompletionItem("imagesharp")];
        });

        var verboseOption = new Option<bool>("--verbose", ["-v"])
        {
            Description = "Enable verbose logging",
            Arity = ArgumentArity.ExactlyOne
        };

        var filtersOption = new Option<bool>("--filters")
        {
            Description = "List all available image filters"
        };

        // Add argument and all options to root command
        rootCommand.Arguments.Add(fileArgument);

        // Basic Options
        rootCommand.Options.Add(numCapsOption);
        rootCommand.Options.Add(columnsOption);
        rootCommand.Options.Add(widthOption);
        rootCommand.Options.Add(heightOption);
        rootCommand.Options.Add(paddingOption);
        rootCommand.Options.Add(outputOption);

        // Time Options
        rootCommand.Options.Add(intervalOption);
        rootCommand.Options.Add(fromOption);
        rootCommand.Options.Add(toOption);
        rootCommand.Options.Add(skipCreditsOption);

        // Visual Options
        rootCommand.Options.Add(filterOption);
        rootCommand.Options.Add(fontOption);
        rootCommand.Options.Add(fontSizeOption);
        rootCommand.Options.Add(disableTimestampsOption);
        rootCommand.Options.Add(timestampOpacityOption);
        rootCommand.Options.Add(headerOption);
        rootCommand.Options.Add(headerMetaOption);
        rootCommand.Options.Add(headerImageOption);
        rootCommand.Options.Add(bgContentOption);
        rootCommand.Options.Add(bgHeaderOption);
        rootCommand.Options.Add(fgHeaderOption);
        rootCommand.Options.Add(borderOption);
        rootCommand.Options.Add(watermarkOption);
        rootCommand.Options.Add(watermarkAllOption);
        rootCommand.Options.Add(commentOption);

        // Processing Options
        rootCommand.Options.Add(skipBlankOption);
        rootCommand.Options.Add(skipBlurryOption);
        rootCommand.Options.Add(fastOption);
        rootCommand.Options.Add(sfwOption);
        rootCommand.Options.Add(blurThresholdOption);
        rootCommand.Options.Add(blankThresholdOption);

        // Output Options
        rootCommand.Options.Add(singleImagesOption);
        rootCommand.Options.Add(overwriteOption);
        rootCommand.Options.Add(skipExistingOption);
        rootCommand.Options.Add(vttOption);
        rootCommand.Options.Add(webVttOption);

        // Upload Options
        rootCommand.Options.Add(uploadOption);
        rootCommand.Options.Add(uploadUrlOption);

        // Configuration Options
        rootCommand.Options.Add(configOption);
        rootCommand.Options.Add(saveConfigOption);
        rootCommand.Options.Add(configFileOption);
        rootCommand.Options.Add(showConfigOption);

        // Global Options
        rootCommand.Options.Add(composerOption);
        rootCommand.Options.Add(verboseOption);
        rootCommand.Options.Add(filtersOption);

        // Set the action for the root command (same logic as generate command)
        rootCommand.SetAction(async parseResult =>
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
                // Basic Options
                NumCaps = parseResult.GetValue(numCapsOption),
                Columns = parseResult.GetValue(columnsOption),
                Width = parseResult.GetValue(widthOption),
                Height = parseResult.GetValue(heightOption),
                Padding = parseResult.GetValue(paddingOption),
                Filename = parseResult.GetValue(outputOption)!,

                // Time Options
                Interval = parseResult.GetValue(intervalOption),
                From = parseResult.GetValue(fromOption)!,
                End = parseResult.GetValue(toOption)!,
                SkipCredits = parseResult.GetValue(skipCreditsOption),

                // Visual Options
                Filter = parseResult.GetValue(filterOption)!,
                FontPath = parseResult.GetValue(fontOption)!,
                FontSize = parseResult.GetValue(fontSizeOption),
                DisableTimestamps = parseResult.GetValue(disableTimestampsOption),
                TimestampOpacity = parseResult.GetValue(timestampOpacityOption),
                Header = parseResult.GetValue(headerOption),
                HeaderMeta = parseResult.GetValue(headerMetaOption),
                HeaderImage = parseResult.GetValue(headerImageOption) ?? "",
                BgContent = parseResult.GetValue(bgContentOption)!,
                BgHeader = parseResult.GetValue(bgHeaderOption)!,
                FgHeader = parseResult.GetValue(fgHeaderOption)!,
                Border = parseResult.GetValue(borderOption),
                Watermark = parseResult.GetValue(watermarkOption) ?? "",
                WatermarkAll = parseResult.GetValue(watermarkAllOption) ?? "",
                Comment = parseResult.GetValue(commentOption)!,

                // Processing Options
                SkipBlank = parseResult.GetValue(skipBlankOption),
                SkipBlurry = parseResult.GetValue(skipBlurryOption),
                Fast = parseResult.GetValue(fastOption),
                Sfw = parseResult.GetValue(sfwOption),
                BlurThreshold = parseResult.GetValue(blurThresholdOption),
                BlankThreshold = parseResult.GetValue(blankThresholdOption),

                // Output Options
                SingleImages = parseResult.GetValue(singleImagesOption),
                Overwrite = parseResult.GetValue(overwriteOption),
                SkipExisting = parseResult.GetValue(skipExistingOption),
                Vtt = parseResult.GetValue(vttOption),
                WebVtt = parseResult.GetValue(webVttOption),

                // Upload Options
                Upload = parseResult.GetValue(uploadOption),
                UploadUrl = parseResult.GetValue(uploadUrlOption)!,

                // Global Options
                Composer = parseResult.GetValue(composerOption)!
            };

            // Handle WebVTT special mode (mimics Go behavior at mt.go:441-447)
            if (options.WebVtt)
            {
                options.Vtt = true;                    // Enable VTT generation
                options.Header = false;                 // Disable header
                options.HeaderMeta = false;            // Disable header meta
                options.DisableTimestamps = true;      // Disable timestamps
                options.Padding = 0;                   // No padding
            }

            // Handle save config
            var saveConfigPath = parseResult.GetValue(saveConfigOption);
            if (!string.IsNullOrEmpty(saveConfigPath))
            {
                // TODO: Save configuration to file
                Console.WriteLine($"Saving configuration to: {saveConfigPath}");
            }

            if (file == null || !file.Exists)
            {
                Console.Error.WriteLine("Error: Video file not found or not specified.");
                return 1;
            }

            try
            {
                // Initialize FFmpeg libraries
                FFmpegHelper.Initialize();

                await ProcessVideoAsync(file.FullName, options);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing video: {ex.Message}");
                if (options.Verbose)
                {
                    Console.Error.WriteLine(ex.StackTrace);
                }
                return 1;
            }
        });

        return rootCommand;
    }

    private static async Task ProcessVideoAsync(string videoPath, ThumbnailOptions options)
    {
        Console.WriteLine($"Processing video: {videoPath}");

        // Initialize services
        var videoProcessor = new VideoProcessor();
        var contentDetection = new ContentDetectionService();
        var filterService = new FilterService();
        var outputService = new OutputService();

        if (options.Verbose)
        {
            Console.WriteLine($"Using {(options.Composer == "ffmpeg" ? "FFmpeg.AutoGen" : "ImageSharp")} composer");
        }

        // Step 1: Extract video metadata
        Console.WriteLine("Extracting video metadata...");
        var headerInfo = await VideoProcessor.GetVideoMetadataAsync(videoPath);

        // Step 2: Calculate timestamps
        Console.WriteLine("Calculating timestamps...");
        var timestamps = VideoProcessor.CalculateTimestamps(headerInfo.Duration, options);
        Console.WriteLine($"Will extract {timestamps.Count} frames");

        var timestampsCountLength = timestamps.Count.ToString().Length;

        // Step 3: Extract frames with content detection
        Console.WriteLine("Extracting frames...");
        var frames = new List<(SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>, TimeSpan)>();

        for (int i = 0; i < timestamps.Count; i++)
        {
            var timestamp = timestamps[i];
            // Pad frame number depending on total count (01 if <100, 001 if <1000, etc.)
            var paddedFrameNumber = (i + 1).ToString().PadLeft(timestampsCountLength, '0');
            Console.Write($"\rExtracting frame {paddedFrameNumber}/{timestamps.Count} at {timestamp:hh\\:mm\\:ss}...");

            var frame = await VideoProcessor.ExtractFrameWithRetriesAsync(
                videoPath,
                timestamp,
                options,
                skipCondition: img =>
                {
                    if (options.SkipBlank && ContentDetectionService.IsBlankFrame(img, options.BlankThreshold))
                        return true;
                    if (options.SkipBlurry && ContentDetectionService.IsBlurryFrame(img, options.BlurThreshold))
                        return true;
                    if (options.Sfw && !ContentDetectionService.IsSafeForWork(img))
                        return true;
                    return false;
                },
                maxRetries: 3
            );

            if (frame != null)
            {
                frames.Add((frame, timestamp));
            }
        }

        Console.WriteLine($"\nExtracted {frames.Count} frames");

        if (frames.Count == 0)
        {
            Console.Error.WriteLine("Error: No valid frames extracted from video");
            return;
        }

        // Step 4: Apply image filters (only for ImageSharp composer)
        if (options.Composer != "ffmpeg" && !string.IsNullOrEmpty(options.Filter) && options.Filter != "none")
        {
            Console.WriteLine($"Applying filters: {options.Filter}");
            foreach (var (frame, _) in frames)
            {
                FilterService.ApplyFilters(frame, options.Filter);
            }
        }

        // Step 5: Create contact sheet or save individual images
        if (options.SingleImages)
        {
            Console.WriteLine("Saving individual images...");
            await OutputService.SaveIndividualImagesAsync(frames, videoPath, options);
        }
        else
        {
            Console.WriteLine("Creating contact sheet...");

            // Choose composer implementation based on --composer option
            SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> contactSheet;
            if (options.Composer == "ffmpeg")
            {
                using var ffmpegComposer = new FFmpegFilterGraphComposer();
                contactSheet = ffmpegComposer.CreateContactSheet(frames, headerInfo, options);
            }
            else
            {
                var imageComposer = new ImageComposer();
                contactSheet = ImageComposer.CreateContactSheet(frames, headerInfo, options);
            }

            using (contactSheet)
            {
                // Apply watermarks if specified
                if (!string.IsNullOrEmpty(options.Watermark))
                {
                    ImageComposer.ApplyWatermark(contactSheet, options.Watermark, center: true);
                }

                if (!string.IsNullOrEmpty(options.WatermarkAll))
                {
                    foreach (var (frame, _) in frames)
                    {
                        ImageComposer.ApplyWatermark(frame, options.WatermarkAll, center: false);
                    }
                }

                Console.WriteLine("Saving contact sheet...");
                var outputPath = await OutputService.SaveContactSheetAsync(contactSheet, videoPath, options);

                // Step 6: Generate WebVTT if requested
                if (options.Vtt || options.WebVtt)
                {
                    Console.WriteLine("Generating WebVTT file...");

                    // Build VTT timestamps array with evenly-spaced intervals spanning full video
                    // Unlike frame extraction timestamps (which use numCaps+1 to avoid the exact end),
                    // VTT timestamps should span from 00:00:00 to video duration (matching Go mt.go:396)
                    var vttTimestamps = new List<TimeSpan> { TimeSpan.Zero };
                    var vttStep = headerInfo.Duration.TotalSeconds / frames.Count;
                    for (int i = 1; i <= frames.Count; i++)
                    {
                        vttTimestamps.Add(TimeSpan.FromSeconds(vttStep * i));
                    }

                    await OutputService.GenerateWebVttAsync(frames, outputPath, videoPath, options, vttTimestamps);
                }
            }
        }

        // Cleanup
        foreach (var (frame, _) in frames)
        {
            frame.Dispose();
        }

        Console.WriteLine("Processing complete!");
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