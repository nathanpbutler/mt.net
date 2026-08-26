namespace nathanbutlerDEV.mt.net.Utilities;

/// <summary>How much human-readable output the pipeline should emit.</summary>
public enum OutputLevel
{
    /// <summary>Errors only.</summary>
    Quiet,

    /// <summary>Progress and milestones. The default.</summary>
    Normal,

    /// <summary>Everything, including per-frame and FFmpeg detail.</summary>
    Verbose
}

/// <summary>
/// Single funnel for all console output so <c>--quiet</c>, <c>--verbose</c> and <c>--json</c>
/// can route it.
/// </summary>
/// <remarks>
/// Before v3 the pipeline wrote to <see cref="Console"/> directly from a dozen places, which is
/// why <c>--verbose</c> had nothing to control. Everything human-readable goes through here now;
/// errors always reach stderr regardless of level so <c>--quiet</c> stays safe in scripts, and
/// <c>--json</c> keeps stdout clean for the machine-readable document.
/// </remarks>
public static class ConsoleOutput
{
    private static int _progressLineLength;

    /// <summary>Current verbosity. Set once from the parsed options.</summary>
    public static OutputLevel Level { get; set; } = OutputLevel.Normal;

    /// <summary>When true, all human-readable output is suppressed so stdout carries only JSON.</summary>
    public static bool JsonMode { get; set; }

    /// <summary>Writes a milestone message. Suppressed by <c>--quiet</c> and <c>--json</c>.</summary>
    public static void Info(string message)
    {
        if (JsonMode || Level == OutputLevel.Quiet)
        {
            return;
        }

        ClearProgress();
        Console.WriteLine(message);
    }

    /// <summary>Writes detail only wanted under <c>--verbose</c>.</summary>
    public static void Verbose(string message)
    {
        if (JsonMode || Level != OutputLevel.Verbose)
        {
            return;
        }

        ClearProgress();
        Console.WriteLine(message);
    }

    /// <summary>
    /// Writes a transient carriage-return progress line. Only at <see cref="OutputLevel.Normal"/>:
    /// quiet has nothing to show and verbose output would shred a rewriting line.
    /// </summary>
    public static void Progress(string message)
    {
        if (JsonMode || Level != OutputLevel.Normal)
        {
            return;
        }

        Console.Write($"\r{message}");
        _progressLineLength = message.Length;
    }

    /// <summary>Erases any in-flight progress line so later output starts clean.</summary>
    public static void ClearProgress()
    {
        if (_progressLineLength == 0)
        {
            return;
        }

        Console.Write($"\r{new string(' ', _progressLineLength)}\r");
        _progressLineLength = 0;
    }

    /// <summary>Writes to stderr. Never suppressed.</summary>
    public static void Error(string message)
    {
        ClearProgress();
        Console.Error.WriteLine(message);
    }
}
