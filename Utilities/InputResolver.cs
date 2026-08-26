namespace nathanbutlerDEV.mt.net.Utilities;

/// <summary>
/// Expands the command line's input arguments into a concrete list of video files.
/// </summary>
/// <remarks>
/// v3 accepts several inputs where v2 took exactly one file. Each argument may be a file, a
/// directory, or a glob; directories and globs are filtered to video files via
/// <see cref="FileValidator.IsVideoFile"/> so that pointing mt at a folder of mixed content does
/// something sensible. An explicitly named file is always accepted, extension notwithstanding —
/// if the user names it, they mean it, and FFmpeg gets the final say.
///
/// Hand-rolled against <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/>
/// rather than pulling in Microsoft.Extensions.FileSystemGlobbing, since this release removes
/// seven packages and should not quietly add one back.
/// </remarks>
public static class InputResolver
{
    /// <summary>The outcome of expanding the input arguments.</summary>
    /// <param name="Files">Distinct video files in stable order.</param>
    /// <param name="Problems">Human-readable notes about arguments that matched nothing.</param>
    /// <param name="HasMissingInput">
    /// True when an argument naming a specific path did not exist. A glob or directory that
    /// matches nothing is only a warning, but a file the user named by hand is an error.
    /// </param>
    public sealed record Result(
        IReadOnlyList<string> Files,
        IReadOnlyList<string> Problems,
        bool HasMissingInput);

    public static Result Resolve(IEnumerable<string> inputs, bool recursive)
    {
        var files = new List<string>();
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasMissingInput = false;

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            var problemsBefore = problems.Count;
            var isPattern = IsPattern(input);

            if (!isPattern && !File.Exists(input) && !Directory.Exists(input))
            {
                hasMissingInput = true;
            }

            var matchedAny = false;
            foreach (var match in Expand(input, isPattern, searchOption, problems))
            {
                matchedAny = true;

                var full = Path.GetFullPath(match);
                if (seen.Add(full))
                {
                    files.Add(full);
                }
            }

            // Only report "matched nothing" when Expand hasn't already said something more
            // specific, so a missing file doesn't produce two lines saying the same thing.
            if (!matchedAny && problems.Count == problemsBefore)
            {
                problems.Add($"No video files matched: {input}");
            }
        }

        return new Result(files, problems, hasMissingInput);
    }

    /// <summary>True when the argument should be treated as a glob rather than a literal path.</summary>
    private static bool IsPattern(string input) => input.Contains('*') || input.Contains('?');

    private static IEnumerable<string> Expand(
        string input, bool isPattern, SearchOption searchOption, List<string> problems)
    {
        if (isPattern)
        {
            var directory = Path.GetDirectoryName(input);
            var pattern = Path.GetFileName(input);

            if (string.IsNullOrEmpty(directory))
            {
                directory = ".";
            }

            if (!Directory.Exists(directory))
            {
                problems.Add($"Directory not found: {directory}");
                return [];
            }

            return SafeEnumerate(directory, pattern, searchOption, problems).Where(FileValidator.IsVideoFile).Order();
        }

        if (Directory.Exists(input))
        {
            return SafeEnumerate(input, "*", searchOption, problems).Where(FileValidator.IsVideoFile).Order();
        }

        if (File.Exists(input))
        {
            // Named explicitly, so honour it whatever the extension says.
            return [input];
        }

        problems.Add($"Not found: {input}");
        return [];
    }

    private static IEnumerable<string> SafeEnumerate(
        string directory, string pattern, SearchOption searchOption, List<string> problems)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern, searchOption).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            problems.Add($"Could not read {directory}: {ex.Message}");
            return [];
        }
    }
}
