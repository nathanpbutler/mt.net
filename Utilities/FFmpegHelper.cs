using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;

namespace nathanbutlerDEV.mt.net.Utilities;

/// <summary>
/// Locates the native FFmpeg 9.x shared libraries and initializes the bindings.
/// </summary>
public static class FFmpegHelper
{
    /// <summary>
    /// libavcodec SONAME major for FFmpeg 9.x. FFmpeg 9.0 bumped every library major
    /// (libavcodec 62 -> 63), so an 8.x install is ABI-incompatible and must not be bound.
    /// </summary>
    private const int AvCodecMajor = 63;

    /// <summary>Environment variable that overrides library discovery entirely.</summary>
    private const string PathOverrideVariable = "MT_FFMPEG_PATH";

    private static bool _initialized;
    private static bool _drawTextWarningShown;
    private static readonly object _lock = new();

    /// <summary>
    /// Initializes FFmpeg libraries and sets the native library search path.
    /// </summary>
    public static void Initialize(bool verbose = false)
    {
        lock (_lock)
        {
            if (_initialized)
                return;

            try
            {
                var libraryPath = SetFFmpegLibraryPath(verbose);

                if (verbose && !string.IsNullOrEmpty(libraryPath))
                {
                    ConsoleOutput.Verbose($"FFmpeg library path set to: {libraryPath}");
                }

                DynamicallyLoadedBindings.Initialize();

                // Suppress informational chatter (e.g. swscaler colorspace notes) but keep errors.
                unsafe
                {
                    ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
                }

                _initialized = true;

                if (verbose)
                {
                    unsafe
                    {
                        ConsoleOutput.Verbose($"FFmpeg initialized successfully: {ffmpeg.av_version_info()}");
                        ConsoleOutput.Verbose($"  libavcodec version:  {ffmpeg.avcodec_version()}");
                        ConsoleOutput.Verbose($"  libavformat version: {ffmpeg.avformat_version()}");
                        ConsoleOutput.Verbose($"  libavutil version:   {ffmpeg.avutil_version()}");
                    }

                    ConsoleOutput.Verbose($"  drawtext filter:     {(HasDrawText ? "available" : "MISSING")}");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(BuildInitFailureMessage(ex), ex);
            }
        }
    }

    /// <summary>
    /// Whether the loaded FFmpeg build includes the <c>drawtext</c> filter.
    /// </summary>
    /// <remarks>
    /// Homebrew's stock ffmpeg formula is built without libfreetype, which drops drawtext,
    /// subtitles and ass. avfilter_get_by_name returns null for filters that were not
    /// compiled in, so this is a direct probe rather than a guess.
    /// </remarks>
    public static bool HasDrawText
    {
        get
        {
            unsafe
            {
                return ffmpeg.avfilter_get_by_name("drawtext") != null;
            }
        }
    }

    /// <summary>
    /// Prints a one-time warning to stderr when text rendering was requested but the loaded
    /// FFmpeg build cannot do it. Non-fatal: a contact sheet without a header is still useful.
    /// </summary>
    public static void WarnIfDrawTextMissing()
    {
        if (_drawTextWarningShown || HasDrawText)
            return;

        _drawTextWarningShown = true;

        ConsoleOutput.Error(string.Empty);
        ConsoleOutput.Error("WARNING: this FFmpeg build has no drawtext filter, so the header and");
        ConsoleOutput.Error("         timestamps cannot be rendered. The contact sheet will be created");
        ConsoleOutput.Error("         without any text.");
        ConsoleOutput.Error(string.Empty);
        ConsoleOutput.Error("         drawtext requires FFmpeg to be built with libfreetype.");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            ConsoleOutput.Error("         Homebrew's stock ffmpeg formula is built without it. Install the");
            ConsoleOutput.Error("         batteries-included build instead:");
            ConsoleOutput.Error(string.Empty);
            ConsoleOutput.Error("           brew install ffmpeg-full");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ConsoleOutput.Error("         Install a distribution build with libfreetype enabled, or set");
            ConsoleOutput.Error($"         {PathOverrideVariable} to a directory containing one.");
        }
        else
        {
            ConsoleOutput.Error("         Use a full build such as the gyan.dev full or essentials release.");
        }

        ConsoleOutput.Error(string.Empty);
    }

    private static string? SetFFmpegLibraryPath(bool verbose)
    {
        // An explicit override always wins.
        var overridePath = Environment.GetEnvironmentVariable(PathOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (Directory.Exists(overridePath))
            {
                if (verbose)
                    ConsoleOutput.Verbose($"  Using {PathOverrideVariable}: {overridePath}");

                DynamicallyLoadedBindings.LibrariesPath = overridePath;
                return overridePath;
            }

            ConsoleOutput.Error($"WARNING: {PathOverrideVariable} is set to '{overridePath}', which does not exist. Ignoring.");
        }

        foreach (var path in GetSearchPaths())
        {
            if (!Directory.Exists(path))
            {
                if (verbose)
                    ConsoleOutput.Verbose($"  Skipping (not found): {path}");
                continue;
            }

            var foundLib = GetProbeNames().Select(name => Path.Combine(path, name)).FirstOrDefault(File.Exists);
            if (foundLib is not null)
            {
                if (verbose)
                    ConsoleOutput.Verbose($"  Found FFmpeg library: {foundLib}");

                DynamicallyLoadedBindings.LibrariesPath = path;
                return path;
            }

            if (verbose)
                ConsoleOutput.Verbose($"  Path exists but has no FFmpeg {AvCodecMajor}.x libraries: {path}");
        }

        if (verbose)
            ConsoleOutput.Verbose("  No FFmpeg libraries found in the standard locations; falling back to the default loader.");

        return null;
    }

    /// <summary>
    /// Candidate filenames for libavcodec on the current platform, most specific first.
    /// </summary>
    private static IEnumerable<string> GetProbeNames()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return $"libavcodec.{AvCodecMajor}.dylib";
            yield return "libavcodec.dylib";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            yield return $"libavcodec.so.{AvCodecMajor}";
            yield return "libavcodec.so";
        }
        else
        {
            yield return $"avcodec-{AvCodecMajor}.dll";
        }
    }

    private static IEnumerable<string> GetSearchPaths()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return GetMacSearchPaths();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return ["/usr/lib/x86_64-linux-gnu", "/usr/lib/aarch64-linux-gnu", "/usr/lib64", "/usr/local/lib", "/usr/lib"];

        return GetWindowsSearchPaths();
    }

    /// <summary>
    /// Homebrew search order. ffmpeg-full comes first because it is the only brew keg built
    /// with libfreetype, and therefore the only one where drawtext works. It is keg-only, so
    /// it is never symlinked into /opt/homebrew/lib and has to be named explicitly.
    /// </summary>
    private static IEnumerable<string> GetMacSearchPaths()
    {
        foreach (var prefix in new[] { "/opt/homebrew", "/usr/local" })
        {
            yield return $"{prefix}/opt/ffmpeg-full/lib";

            foreach (var dir in EnumerateCellarVersions($"{prefix}/Cellar/ffmpeg-full"))
                yield return dir;
        }

        foreach (var prefix in new[] { "/opt/homebrew", "/usr/local" })
        {
            yield return $"{prefix}/opt/ffmpeg/lib";

            foreach (var dir in EnumerateCellarVersions($"{prefix}/Cellar/ffmpeg"))
                yield return dir;
        }

        yield return "/opt/homebrew/lib";
        yield return "/usr/local/lib";
    }

    /// <summary>Enumerates cellar version/lib directories, newest first.</summary>
    private static IEnumerable<string> EnumerateCellarVersions(string cellarBase)
    {
        if (!Directory.Exists(cellarBase))
            return [];

        try
        {
            return Directory.GetDirectories(cellarBase)
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .Select(d => Path.Combine(d, "lib"))
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> GetWindowsSearchPaths()
    {
        yield return AppContext.BaseDirectory;

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var entry in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return entry;
        }

        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var root = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(root))
                continue;

            yield return Path.Combine(root, "ffmpeg", "bin");
        }
    }

    private static string BuildInitFailureMessage(Exception ex)
    {
        var message = "Failed to initialize FFmpeg.\n\n" +
                      "mt.net requires the FFmpeg 9.x shared libraries (libavcodec 63).\n\n";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            message += "On macOS, install FFmpeg via Homebrew:\n\n" +
                       "  brew install ffmpeg-full\n\n" +
                       "ffmpeg-full is recommended over the stock ffmpeg formula because the stock\n" +
                       "formula is built without libfreetype, which removes the drawtext filter that\n" +
                       "mt.net uses to render the header and timestamps.\n\n" +
                       "Verify the install:\n" +
                       "  brew info ffmpeg-full\n";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            message += "On Linux, install FFmpeg 9.x from your distribution:\n" +
                       "  apt-get install ffmpeg   (or the equivalent for your distro)\n";
        }
        else
        {
            message += "On Windows, download an FFmpeg 9.x shared build from:\n" +
                       "  https://www.gyan.dev/ffmpeg/builds/\n\n" +
                       "Then add its bin directory to PATH, or place the DLLs next to mt.exe.\n";
        }

        message += $"\nYou can also point mt.net at a specific directory with {PathOverrideVariable}.\n";
        message += $"\nError details: {ex.Message}";

        if (ex.InnerException is not null)
            message += $"\nInner error: {ex.InnerException.Message}";

        return message;
    }
}
