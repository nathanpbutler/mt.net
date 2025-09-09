using Microsoft.Extensions.Configuration;

namespace MtNet.Configuration;

public static class ConfigurationBuilder
{
    public static IConfiguration Build(string[] args)
    {
        var builder = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("mt.json", optional: true, reloadOnChange: true)
            .AddJsonFile("mt.config.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables("MT_")
            .AddCommandLine(args);

        return builder.Build();
    }
}