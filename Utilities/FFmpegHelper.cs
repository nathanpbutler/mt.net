using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;

namespace nathanbutlerDEV.mt.net.Utilities;

/// <summary>
/// Helper class for initializing FFmpeg libraries
/// </summary>
public static class FFmpegHelper
{
    private static bool _initialized = false;
    private static readonly object _lock = new object();

    /// <summary>
    /// Initializes FFmpeg libraries and sets the library path
    /// </summary>
    public static void Initialize(bool verbose = false)
    {
        lock (_lock)
        {
            if (_initialized)
                return;

            try
            {
                // Set FFmpeg library path based on platform
                var libraryPath = SetFFmpegLibraryPath(verbose);

                if (verbose && !string.IsNullOrEmpty(libraryPath))
                {
                    Console.WriteLine($"FFmpeg library path set to: {libraryPath}");
                }

                // Initialize dynamically loaded bindings
                DynamicallyLoadedBindings.Initialize();

                // Set log level to suppress informational warnings (like swscaler colorspace messages)
                // while still showing actual errors
                unsafe
                {
                    ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
                }

                _initialized = true;

                // Log FFmpeg version
                unsafe
                {
                    var version = ffmpeg.av_version_info();
                    Console.WriteLine($"FFmpeg initialized successfully: {version}");

                    if (verbose)
                    {
                        Console.WriteLine($"  libavcodec version: {ffmpeg.avcodec_version()}");
                        Console.WriteLine($"  libavformat version: {ffmpeg.avformat_version()}");
                        Console.WriteLine($"  libavutil version: {ffmpeg.avutil_version()}");
                    }
                }
            }
            catch (Exception ex)
            {
                var errorMsg = "Failed to initialize FFmpeg.\n\n";

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    errorMsg += "On macOS, ensure FFmpeg 7.x is installed:\n" +
                               "  brew install ffmpeg@7\n\n" +
                               "Check your installation:\n" +
                               "  brew list ffmpeg@7\n" +
                               "  brew info ffmpeg@7\n\n" +
                               "NOTE: FFmpeg.AutoGen 7.1.1 requires FFmpeg 7.x libraries.\n" +
                               "If you have FFmpeg 6.x or older, you need to install FFmpeg 7:\n" +
                               "  brew install ffmpeg@7\n";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    errorMsg += "On Linux, ensure FFmpeg 7.x is installed:\n" +
                               "  apt-get install ffmpeg (or equivalent)\n";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    errorMsg += "On Windows, download FFmpeg 7.x from:\n" +
                               "  https://www.gyan.dev/ffmpeg/builds/\n";
                }

                errorMsg += $"\nError details: {ex.Message}";

                if (ex.InnerException != null)
                {
                    errorMsg += $"\nInner error: {ex.InnerException.Message}";
                }

                throw new InvalidOperationException(errorMsg, ex);
            }
        }
    }

    private static string? SetFFmpegLibraryPath(bool verbose = false)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Build list of paths to check
            var searchPaths = new List<string>();

            // 1. Check for ffmpeg@7 Cellar (versioned keg-only installation)
            var cellarBase = "/opt/homebrew/Cellar/ffmpeg@7";  // Apple Silicon
            if (Directory.Exists(cellarBase))
            {
                // Find all version subdirectories and get their lib paths
                var versionDirs = Directory.GetDirectories(cellarBase)
                    .OrderByDescending(d => d) // Newest version first
                    .Select(d => Path.Combine(d, "lib"));
                searchPaths.AddRange(versionDirs);
            }

            cellarBase = "/usr/local/Cellar/ffmpeg@7";  // Intel Mac
            if (Directory.Exists(cellarBase))
            {
                var versionDirs = Directory.GetDirectories(cellarBase)
                    .OrderByDescending(d => d)
                    .Select(d => Path.Combine(d, "lib"));
                searchPaths.AddRange(versionDirs);
            }

            // 2. Check opt link (keg-only)
            searchPaths.Add("/opt/homebrew/opt/ffmpeg@7/lib");
            searchPaths.Add("/usr/local/opt/ffmpeg@7/lib");

            // 3. Check regular ffmpeg Cellar (if not keg-only)
            cellarBase = "/opt/homebrew/Cellar/ffmpeg";
            if (Directory.Exists(cellarBase))
            {
                var versionDirs = Directory.GetDirectories(cellarBase)
                    .Where(d => Path.GetFileName(d).StartsWith("7."))  // Only FFmpeg 7.x
                    .OrderByDescending(d => d)
                    .Select(d => Path.Combine(d, "lib"));
                searchPaths.AddRange(versionDirs);
            }

            // 4. Check default lib directories (linked installations)
            searchPaths.Add("/opt/homebrew/lib");
            searchPaths.Add("/usr/local/lib");

            // Try each path
            foreach (var path in searchPaths)
            {
                if (!Directory.Exists(path))
                {
                    if (verbose)
                        Console.WriteLine($"  Skipping (not found): {path}");
                    continue;
                }

                // Check if libavcodec actually exists in this path
                var testLibs = new[]
                {
                    Path.Combine(path, "libavcodec.dylib"),
                    Path.Combine(path, "libavcodec.61.dylib"),  // FFmpeg 7.x
                    Path.Combine(path, "libavcodec.60.dylib"),  // FFmpeg 6.x (for reference)
                };

                var foundLib = testLibs.FirstOrDefault(File.Exists);
                if (foundLib != null)
                {
                    Console.WriteLine($"Found FFmpeg library: {foundLib}");
                    DynamicallyLoadedBindings.LibrariesPath = path;
                    return path;
                }
                else if (verbose)
                {
                    Console.WriteLine($"  Path exists but no FFmpeg libraries: {path}");
                }
            }

            // If we get here, we didn't find FFmpeg
            Console.WriteLine("WARNING: No FFmpeg 7.x libraries found in standard Homebrew locations.");
            Console.WriteLine("Attempting to use system default paths (may fail)...");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Try common Linux paths
            var linuxPaths = new[]
            {
                "/usr/lib/x86_64-linux-gnu",
                "/usr/lib64",
                "/usr/lib",
            };

            foreach (var path in linuxPaths)
            {
                if (Directory.Exists(path))
                {
                    if (verbose)
                        Console.WriteLine($"  Using library path: {path}");
                    DynamicallyLoadedBindings.LibrariesPath = path;
                    return path;
                }
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Windows, FFmpeg binaries should be in the application directory
            // or in PATH. DynamicallyLoaded will search these locations.
            if (verbose)
                Console.WriteLine("Windows: Using default library search paths.");
        }

        return null;
    }
}
