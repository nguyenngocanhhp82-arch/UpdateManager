using System.Text.Json;
using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public class ConfigService : IConfigService
{
    private readonly string _configFilePath;
    private readonly object _lock = new();
    private AppConfig _currentConfig = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AppConfig CurrentConfig
    {
        get
        {
            lock (_lock)
            {
                return _currentConfig;
            }
        }
        private set
        {
            lock (_lock)
            {
                _currentConfig = value;
            }
        }
    }

    public ConfigService(string? customConfigPath = null)
    {
        _configFilePath = !string.IsNullOrWhiteSpace(customConfigPath)
            ? customConfigPath
            : DetermineConfigFilePath();
    }

    public async Task<AppConfig> LoadConfigAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                string json = await File.ReadAllTextAsync(_configFilePath, cancellationToken).ConfigureAwait(false);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config != null)
                {
                    CurrentConfig = config;
                    return config;
                }
            }
        }
        catch
        {
            // Fallback to default if reading fails
        }

        var defaultConfig = new AppConfig();
        CurrentConfig = defaultConfig;
        await SaveConfigAsync(defaultConfig, cancellationToken).ConfigureAwait(false);
        return defaultConfig;
    }

    public async Task SaveConfigAsync(AppConfig config, CancellationToken cancellationToken = default)
    {
        CurrentConfig = config;
        try
        {
            string? dir = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(config, JsonOptions);
            await File.WriteAllTextAsync(_configFilePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Non-critical persistence failure
        }
    }

    public string GetCacheDirectory()
    {
        if (!string.IsNullOrWhiteSpace(CurrentConfig.CacheDirectory) && Directory.Exists(CurrentConfig.CacheDirectory))
        {
            return CurrentConfig.CacheDirectory;
        }

        string defaultCache = Path.Combine(Path.GetDirectoryName(_configFilePath) ?? Path.GetTempPath(), "Cache");
        try
        {
            Directory.CreateDirectory(defaultCache);
        }
        catch { }
        return defaultCache;
    }

    private static string DetermineConfigFilePath()
    {
        try
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrWhiteSpace(programData))
            {
                string dir = Path.Combine(programData, "WinUpdateManager");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "config.json");
            }
        }
        catch { }

        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                string dir = Path.Combine(localAppData, "WinUpdateManager");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "config.json");
            }
        }
        catch { }

        string temp = Path.Combine(Path.GetTempPath(), "WinUpdateManager");
        Directory.CreateDirectory(temp);
        return Path.Combine(temp, "config.json");
    }
}
