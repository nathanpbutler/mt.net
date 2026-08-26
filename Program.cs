using nathanbutlerDEV.mt.net.Commands;
using nathanbutlerDEV.mt.net.Utilities;

namespace nathanbutlerDEV.mt.net;

/// <summary>
/// Main program class.
/// </summary>
/// <remarks>
/// This class contains the entry point for the application.
/// </remarks>
class Program
{
    /// <summary>
    /// Main method - application entry point.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Exit code.</returns>
    static int Main(string[] args)
    {
        try
        {
            var rootCommand = RootCommandBuilder.CreateRootCommand();
            return rootCommand.Parse(args).Invoke();
        }
        catch (Exception ex)
        {
            // Last-resort handler. Per-file failures are caught in the command action;
            // reaching here means parsing or setup itself failed.
            ConsoleOutput.Error($"Fatal: {ex.Message}");
            return 1;
        }
    }
}
