using System.CommandLine;
using MtNet.Commands;
using MtNet.Configuration;
using Serilog;

namespace MtNet;

class Program
{
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
            Log.Fatal(ex, "Application terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
