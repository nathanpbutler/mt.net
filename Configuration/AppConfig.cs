using Microsoft.Extensions.Configuration;
using MtNet.Models;

namespace MtNet.Configuration;

public class AppConfig
{
    private readonly IConfiguration _configuration;

    public AppConfig(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public ThumbnailOptions GetThumbnailOptions()
    {
        var options = new ThumbnailOptions();
        _configuration.Bind(options);
        return options;
    }

    public T GetValue<T>(string key, T defaultValue = default!)
    {
        return _configuration.GetValue<T>(key, defaultValue);
    }

    public string GetConnectionString(string name)
    {
        return _configuration.GetConnectionString(name) ?? string.Empty;
    }
}