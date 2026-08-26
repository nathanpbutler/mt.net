using System.CommandLine;
using System.CommandLine.Completions;
using System.CommandLine.Help;
using System.Text.Json;
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

        // Input files. ZeroOrMore rather than ExactlyOne so that --filters works on its own
        // and so a run can name several files, a directory, or a glob.
        var filesArgument = new Argument<string[]>("files")
        {
            Description = "Video files, directories, or globs to process",
            Arity = ArgumentArity.ZeroOrMore
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

        // Time Options
        var intervalOption = new Option<int>("--interval", ["-i"])
        {
            Description = "Interval between captures in seconds (overrides numcaps)",
            DefaultValueFactory = _ => 0
        };

        var fromOption = new Option<string>("--from")
        {
            Description = "Start time for captures (HH:MM:SS)",
            DefaultValueFactory = _ => "00:00:00"
        };

        var toOption = new Option<string>("--to", ["--end"])
        {
            Description = "End time for captures (HH:MM:SS)",
            DefaultValueFactory = _ => "00:00:00"
        };

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

        var fontOption = new Option<string>("--font", ["-f"])
        {
            Description = "Font to use for timestamps and header",
            DefaultValueFactory = _ => "DroidSans"
        };

        var fontSizeOption = new Option<int>("--font-size")
        {
            Description = "Font size in pixels",
            DefaultValueFactory = _ => 12
        };

        var disableTimestampsOption = new Option<bool>("--disable-timestamps", ["-d"])
        {
            Description = "Disable timestamp overlay on images"
        };

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
            Description = "Image to display on the right of the header"
        };

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
            Description = "Watermark image for the center thumbnail"
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

        // v360 (VR) Options
        var v360Option = new Option<bool>("--v360")
        {
            Description = "Convert 360-degree VR footage to a flat projection"
        };

        var v360InputOption = new Option<string>("--v360-input")
        {
            Description = "v360 input projection (e.g. hequirect, equirect, dfisheye)",
            DefaultValueFactory = _ => "hequirect"
        };

        var v360OutputOption = new Option<string>("--v360-output")
        {
            Description = "v360 output projection (e.g. flat, equirect)",
            DefaultValueFactory = _ => "flat"
        };

        var v360StereoOption = new Option<string>("--v360-stereo")
        {
            Description = "v360 input stereo layout (sbs, tb, 2d)",
            DefaultValueFactory = _ => "sbs"
        };

        var v360FovOption = new Option<int>("--v360-fov")
        {
            Description = "v360 diagonal field of view in degrees",
            DefaultValueFactory = _ => 125
        };

        var v360PitchOption = new Option<int>("--v360-pitch")
        {
            Description = "v360 pitch adjustment in degrees",
            DefaultValueFactory = _ => -25
        };

        // Processing Options
        var skipBlankOption = new Option<bool>("--skip-blank", ["-b"])
        {
            Description = "Skip blank frames (up to 3 retries)"
        };

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
            Description = "Blur detection aggressiveness: 0 never skips, 100 skips most",
            DefaultValueFactory = _ => 60
        };

        var blankThresholdOption = new Option<int>("--blank-threshold")
        {
            Description = "Blank detection aggressiveness: 0 never skips, 100 skips most",
            DefaultValueFactory = _ => 50
        };

        var retriesOption = new Option<int>("--retries")
        {
            Description = "Attempts to find an acceptable frame before keeping the best candidate",
            DefaultValueFactory = _ => 3
        };

        var retryStepOption = new Option<double>("--retry-step")
        {
            Description = "Seconds to advance between retry attempts",
            DefaultValueFactory = _ => 1.0
        };

        var dedupeOption = new Option<bool>("--dedupe")
        {
            Description = "Skip frames that look like ones already chosen"
        };

        var dedupeThresholdOption = new Option<int>("--dedupe-threshold")
        {
            Description = "How alike two frames must be to count as duplicates (0-64, lower is stricter)",
            DefaultValueFactory = _ => 6
        };

        var sceneDetectOption = new Option<bool>("--scene-detect")
        {
            Description = "Prefer a frame just after a scene change near each timestamp"
        };

        var sceneWindowOption = new Option<double>("--scene-window")
        {
            Description = "Seconds to search forward for a scene change",
            DefaultValueFactory = _ => 5.0
        };

        var outputOption = new Option<string>("--output", ["-o"])
        {
            Description = "Output filename pattern",
            DefaultValueFactory = _ => "{{.Path}}{{.Name}}.jpg"
        };

        outputOption.CompletionSources.Add(ctx =>
        {
            var files = ctx.ParseResult.GetValue(filesArgument);
            var first = files?.FirstOrDefault();
            if (!string.IsNullOrEmpty(first))
            {
                var directory = Path.GetDirectoryName(first) ?? ".";
                return [new CompletionItem(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(first)}.jpg"))];
            }
            return [];
        });

        // Output Options
        var formatOption = new Option<OutputFormat>("--format")
        {
            Description = "Output image format: auto (from extension), jpg, or png",
            DefaultValueFactory = _ => OutputFormat.Auto
        };

        var qualityOption = new Option<int>("--quality", ["-q"])
        {
            Description = "JPEG quality (1-100); ignored for PNG",
            DefaultValueFactory = _ => 90
        };

        var singleImagesOption = new Option<bool>("--single-images", ["-s"])
        {
            Description = "Save individual images instead of contact sheet"
        };

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

        var noMtimeOption = new Option<bool>("--no-mtime")
        {
            Description = "Do not apply input file's modified date to output files"
        };

        // Input Options
        var recursiveOption = new Option<bool>("--recursive", ["-r"])
        {
            Description = "Recurse into subdirectories when an input is a directory or glob"
        };

        // Global Options
        var verboseOption = new Option<bool>("--verbose", ["-v"])
        {
            Description = "Enable verbose logging"
        };

        var quietOption = new Option<bool>("--quiet")
        {
            Description = "Suppress all output except errors"
        };

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Emit a JSON summary to stdout instead of human-readable progress"
        };

        var filtersOption = new Option<bool>("--filters")
        {
            Description = "List all available image filters"
        };

        // Add argument and all options to root command
        rootCommand.Arguments.Add(filesArgument);

        // Basic Options
        rootCommand.Options.Add(numCapsOption);
        rootCommand.Options.Add(columnsOption);
        rootCommand.Options.Add(widthOption);
        rootCommand.Options.Add(heightOption);
        rootCommand.Options.Add(paddingOption);

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

        // v360 Options
        rootCommand.Options.Add(v360Option);
        rootCommand.Options.Add(v360InputOption);
        rootCommand.Options.Add(v360OutputOption);
        rootCommand.Options.Add(v360StereoOption);
        rootCommand.Options.Add(v360FovOption);
        rootCommand.Options.Add(v360PitchOption);

        // Processing Options
        rootCommand.Options.Add(skipBlankOption);
        rootCommand.Options.Add(skipBlurryOption);
        rootCommand.Options.Add(fastOption);
        rootCommand.Options.Add(sfwOption);
        rootCommand.Options.Add(blurThresholdOption);
        rootCommand.Options.Add(blankThresholdOption);
        rootCommand.Options.Add(retriesOption);
        rootCommand.Options.Add(retryStepOption);
        rootCommand.Options.Add(dedupeOption);
        rootCommand.Options.Add(dedupeThresholdOption);
        rootCommand.Options.Add(sceneDetectOption);
        rootCommand.Options.Add(sceneWindowOption);

        // Output Options
        rootCommand.Options.Add(outputOption);
        rootCommand.Options.Add(formatOption);
        rootCommand.Options.Add(qualityOption);
        rootCommand.Options.Add(singleImagesOption);
        rootCommand.Options.Add(overwriteOption);
        rootCommand.Options.Add(skipExistingOption);
        rootCommand.Options.Add(vttOption);
        rootCommand.Options.Add(webVttOption);
        rootCommand.Options.Add(noMtimeOption);

        // Input Options
        rootCommand.Options.Add(recursiveOption);

        // Global Options
        rootCommand.Options.Add(verboseOption);
        rootCommand.Options.Add(quietOption);
        rootCommand.Options.Add(jsonOption);
        rootCommand.Options.Add(filtersOption);

        rootCommand.SetAction(async parseResult =>
        {
            // --filters is informational and takes no input, so answer it before anything else.
            if (parseResult.GetValue(filtersOption))
            {
                ShowAvailableFilters();
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

                // v360 Options
                V360 = parseResult.GetValue(v360Option),
                V360Input = parseResult.GetValue(v360InputOption)!,
                V360Output = parseResult.GetValue(v360OutputOption)!,
                V360Stereo = parseResult.GetValue(v360StereoOption)!,
                V360Fov = parseResult.GetValue(v360FovOption),
                V360Pitch = parseResult.GetValue(v360PitchOption),

                // Processing Options
                SkipBlank = parseResult.GetValue(skipBlankOption),
                SkipBlurry = parseResult.GetValue(skipBlurryOption),
                Fast = parseResult.GetValue(fastOption),
                Sfw = parseResult.GetValue(sfwOption),
                BlurThreshold = parseResult.GetValue(blurThresholdOption),
                BlankThreshold = parseResult.GetValue(blankThresholdOption),
                Retries = parseResult.GetValue(retriesOption),
                RetryStep = parseResult.GetValue(retryStepOption),
                Dedupe = parseResult.GetValue(dedupeOption),
                DedupeThreshold = parseResult.GetValue(dedupeThresholdOption),
                SceneDetect = parseResult.GetValue(sceneDetectOption),
                SceneWindow = parseResult.GetValue(sceneWindowOption),

                // Output Options
                Filename = parseResult.GetValue(outputOption)!,
                Format = parseResult.GetValue(formatOption),
                Quality = parseResult.GetValue(qualityOption),
                SingleImages = parseResult.GetValue(singleImagesOption),
                Overwrite = parseResult.GetValue(overwriteOption),
                SkipExisting = parseResult.GetValue(skipExistingOption),
                Vtt = parseResult.GetValue(vttOption),
                WebVtt = parseResult.GetValue(webVttOption),
                NoMtime = parseResult.GetValue(noMtimeOption),

                // Input Options
                Recursive = parseResult.GetValue(recursiveOption),

                // Global Options
                Verbose = parseResult.GetValue(verboseOption),
                Quiet = parseResult.GetValue(quietOption),
                Json = parseResult.GetValue(jsonOption)
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

            ConsoleOutput.JsonMode = options.Json;
            ConsoleOutput.Level = options.Quiet ? OutputLevel.Quiet
                : options.Verbose ? OutputLevel.Verbose
                : OutputLevel.Normal;

            // Catch incoherent combinations before decoding anything.
            var validationError = options.Validate();
            if (validationError != null)
            {
                ConsoleOutput.Error($"Error: {validationError}");
                return 1;
            }

            var inputs = parseResult.GetValue(filesArgument) ?? [];
            if (inputs.Length == 0)
            {
                ConsoleOutput.Error("Error: no input files specified. Pass one or more video files, directories, or globs.");
                return 1;
            }

            var resolved = InputResolver.Resolve(inputs, options.Recursive);
            foreach (var problem in resolved.Problems)
            {
                ConsoleOutput.Error($"Warning: {problem}");
            }

            if (resolved.Files.Count == 0)
            {
                ConsoleOutput.Error("Error: no video files to process.");
                return 1;
            }

            // A glob that matches nothing is a warning; a file the user named that isn't there
            // is a mistake worth a non-zero exit, even though the other inputs still run.
            var missingInputExitCode = resolved.HasMissingInput ? 1 : 0;

            try
            {
                FFmpegHelper.Initialize(options.Verbose);
            }
            catch (Exception ex)
            {
                ConsoleOutput.Error(ex.Message);
                return 1;
            }

            var exitCode = await ProcessAllAsync(resolved.Files, options);
            return exitCode != 0 ? exitCode : missingInputExitCode;
        });

        return rootCommand;
    }

    /// <summary>
    /// Processes every resolved input, continuing past per-file failures.
    /// </summary>
    /// <returns>0 when every file succeeded, 1 if any failed.</returns>
    private static async Task<int> ProcessAllAsync(IReadOnlyList<string> files, ThumbnailOptions options)
    {
        var results = new List<JsonFileResult>();
        var failures = 0;

        // drawtext is absent from FFmpeg builds compiled without libfreetype (notably Homebrew's
        // stock formula). Warn once for the whole run rather than once per file.
        if (options.Header || !options.DisableTimestamps)
        {
            FFmpegHelper.WarnIfDrawTextMissing();
        }

        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];

            if (files.Count > 1)
            {
                ConsoleOutput.Info($"[{i + 1}/{files.Count}] {file}");
            }

            try
            {
                results.Add(await ProcessVideoAsync(file, options));
            }
            catch (Exception ex)
            {
                failures++;
                ConsoleOutput.Error($"Error processing {file}: {ex.Message}");

                if (options.Verbose)
                {
                    ConsoleOutput.Error(ex.StackTrace ?? "");
                }

                results.Add(new JsonFileResult(file, false, [], null, 0, null, null, ex.Message));
            }
        }

        if (options.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new JsonReport(results), JsonReportContext.Default.JsonReport));
        }
        else
        {
            ConsoleOutput.Info("Processing complete!");
        }

        return failures > 0 ? 1 : 0;
    }

    private static async Task<JsonFileResult> ProcessVideoAsync(string videoPath, ThumbnailOptions options)
    {
        ConsoleOutput.Info($"Processing video: {videoPath}");

        // Step 1: Extract video metadata
        ConsoleOutput.Info("Extracting video metadata...");
        var headerInfo = await VideoProcessor.GetVideoMetadataAsync(videoPath);

        // Step 2: Calculate timestamps
        ConsoleOutput.Info("Calculating timestamps...");
        var timestamps = VideoProcessor.CalculateTimestamps(headerInfo.Duration, options);
        ConsoleOutput.Info($"Will extract {timestamps.Count} frames");

        var timestampsCountLength = timestamps.Count.ToString().Length;

        // Step 3: Extract frames with content detection.
        // One decoder for the whole file: it seeks and flushes per call, and reopening the
        // container per frame (as v2 did) was pure overhead.
        ConsoleOutput.Info("Extracting frames...");
        var frames = new List<(RgbaImage, TimeSpan)>();

        var needs = ContentDetectionService.NeedsFor(options);
        var chosenFingerprints = new List<ulong>();
        var fallbacks = 0;

        using (var decoder = new FFmpegAutoGenVideoDecoder(videoPath))
        {
            for (int i = 0; i < timestamps.Count; i++)
            {
                var timestamp = timestamps[i];
                // Pad frame number depending on total count (01 if <100, 001 if <1000, etc.)
                var paddedFrameNumber = (i + 1).ToString().PadLeft(timestampsCountLength, '0');
                ConsoleOutput.Progress(
                    $"Extracting frame {paddedFrameNumber}/{timestamps.Count} at {timestamp:hh\\:mm\\:ss}...");

                var selection = await VideoProcessor.SelectFrameAsync(
                    decoder, timestamp, options, needs, chosenFingerprints);

                if (selection == null)
                {
                    continue;
                }

                if (selection.FellBack)
                {
                    fallbacks++;
                }

                if (needs.HasFlag(AnalysisNeeds.Fingerprint))
                {
                    chosenFingerprints.Add(selection.Analysis.Fingerprint);
                }

                // Keep the timestamp the frame actually came from: scene detection and retries
                // both move it, and the overlay should say where the picture is really from.
                frames.Add((selection.Image, selection.Timestamp));
            }
        }

        ConsoleOutput.ClearProgress();
        ConsoleOutput.Info($"Extracted {frames.Count} frames");

        if (fallbacks > 0)
        {
            ConsoleOutput.Info(
                $"  {fallbacks} frame(s) kept as best available after no candidate passed the content checks");
        }

        try
        {
            if (frames.Count == 0)
            {
                throw new InvalidOperationException("No valid frames could be extracted from the video.");
            }

            return options.SingleImages
                ? await SaveSingleImagesAsync(frames, videoPath, headerInfo, options)
                : await SaveContactSheetAsync(frames, videoPath, headerInfo, options);
        }
        finally
        {
            foreach (var (frame, _) in frames)
            {
                frame.Dispose();
            }
        }
    }

    /// <summary>
    /// Renders each frame and writes it as its own image.
    /// </summary>
    /// <remarks>
    /// v2 handed the raw decoded frames straight to the encoder, so <c>--single-images</c> ignored
    /// every rendering option. Running <see cref="FFmpegFilterGraphComposer.ProcessFrames"/> here
    /// gives this path the same scaling, filters, timestamps, border and watermarks as the sheet.
    /// </remarks>
    private static async Task<JsonFileResult> SaveSingleImagesAsync(
        List<(RgbaImage Image, TimeSpan Timestamp)> frames,
        string videoPath,
        HeaderInfo headerInfo,
        ThumbnailOptions options)
    {
        ConsoleOutput.Info("Rendering frames...");

        List<(RgbaImage Image, TimeSpan Timestamp)> processed;
        using (var composer = new FFmpegFilterGraphComposer())
        {
            processed = composer.ProcessFrames(frames, options);
        }

        try
        {
            ConsoleOutput.Info("Saving individual images...");
            var paths = await OutputService.SaveIndividualImagesAsync(processed, videoPath, options);

            return new JsonFileResult(
                videoPath, true, paths, null, processed.Count,
                JsonVideoMetadata.From(headerInfo), null, null);
        }
        finally
        {
            foreach (var (frame, _) in processed)
            {
                frame.Dispose();
            }
        }
    }

    private static async Task<JsonFileResult> SaveContactSheetAsync(
        List<(RgbaImage Image, TimeSpan Timestamp)> frames,
        string videoPath,
        HeaderInfo headerInfo,
        ThumbnailOptions options)
    {
        ConsoleOutput.Info("Creating contact sheet...");

        RgbaImage contactSheet;
        SheetLayout layout;
        using (var composer = new FFmpegFilterGraphComposer())
        {
            (contactSheet, layout) = composer.CreateContactSheet(frames, headerInfo, options);
        }

        using (contactSheet)
        {
            ConsoleOutput.Info("Saving contact sheet...");
            var outputPath = await OutputService.SaveContactSheetAsync(contactSheet, videoPath, options);

            string? vttPath = null;
            if (options.Vtt)
            {
                ConsoleOutput.Info("Generating WebVTT file...");

                // VTT cues span the whole video, unlike the extraction timestamps which stop
                // short of the end so the last frame stays decodable (matching Go mt.go:396).
                var vttTimestamps = new List<TimeSpan> { TimeSpan.Zero };
                var vttStep = headerInfo.Duration.TotalSeconds / frames.Count;
                for (int i = 1; i <= frames.Count; i++)
                {
                    vttTimestamps.Add(TimeSpan.FromSeconds(vttStep * i));
                }

                vttPath = await OutputService.GenerateWebVttAsync(
                    frames.Count, outputPath, videoPath, options, layout, vttTimestamps);
            }

            return new JsonFileResult(
                videoPath, true, [outputPath], vttPath, frames.Count,
                JsonVideoMetadata.From(headerInfo), JsonSheetLayout.From(layout), null);
        }
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
