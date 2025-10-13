using nathanbutlerDEV.mt.net.Commands;
using nathanbutlerDEV.mt.net.Configuration;
using Serilog;

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
    /// <remarks>
    /// This method initializes logging, builds configuration, and sets up command parsing.
    /// </remarks>
    static int Main(string[] args)
    {
        // Initialize logging
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateLogger();

        try
        {
            // Build configuration
            var configuration = ConfigurationBuilder.Build(args);

            // Create and run the root command
            var rootCommand = RootCommandBuilder.CreateRootCommand();

            // Parse and invoke
            var parseResult = rootCommand.Parse(args);
            return parseResult.Invoke();
        }
        catch (Exception ex)
        {
            // Handle exceptions, log errors and return a non-zero exit code
            Log.Fatal(ex, "Application terminated unexpectedly");
            return 1;
        }
        finally
        {
            // Ensure to flush and close the logger
            Log.CloseAndFlush();
        }
    }
}
