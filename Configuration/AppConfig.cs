using Microsoft.Extensions.Configuration;
using nathanbutlerDEV.mt.net.Models;

namespace nathanbutlerDEV.mt.net.Configuration;

public class AppConfig(IConfiguration configuration)
{
    private readonly IConfiguration _configuration = configuration;

    public ThumbnailOptions GetThumbnailOptions()
    {
        var options = new ThumbnailOptions();
        _configuration.Bind(options);
        return options;
    }

    public T GetValue<T>(string key, T defaultValue = default!)
    {
        return _configuration.GetValue(key, defaultValue) ?? defaultValue;
    }

    public string GetConnectionString(string name)
    {
        return _configuration.GetConnectionString(name) ?? string.Empty;
    }
}