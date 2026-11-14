using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public interface IConfigService
{
    AppConfig CurrentConfig { get; }
    Task<AppConfig> LoadConfigAsync(CancellationToken cancellationToken = default);
    Task SaveConfigAsync(AppConfig config, CancellationToken cancellationToken = default);
    string GetCacheDirectory();
}
