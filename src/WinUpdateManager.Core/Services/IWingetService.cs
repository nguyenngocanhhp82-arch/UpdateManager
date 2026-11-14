using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public interface IWingetService
{
    Task<bool> IsWingetInstalledAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WingetPackage>> GetUpgradesAsync(bool includeUnknown = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WingetPackage>> GetInstalledAppsAsync(string? filter = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAvailableVersionsAsync(string packageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetPinnedPackageIdsAsync(CancellationToken cancellationToken = default);

    Task<ProcessResult> UpgradePackageAsync(string packageId, CancellationToken cancellationToken = default);
    Task<ProcessResult> UpgradeAllPackagesAsync(CancellationToken cancellationToken = default);
    Task<ProcessResult> UninstallPackageAsync(string packageId, CancellationToken cancellationToken = default);
    Task<ProcessResult> InstallVersionAsync(string packageId, string version, bool force = true, CancellationToken cancellationToken = default);
    Task<ProcessResult> PinPackageAsync(string packageId, CancellationToken cancellationToken = default);
    Task<ProcessResult> UnpinPackageAsync(string packageId, CancellationToken cancellationToken = default);

    // WhatsApp Fast / Offline Features
    Task<WhatsAppStatus> CheckWhatsAppStatusAsync(CancellationToken cancellationToken = default);
    Task<ProcessResult> FastUpdateWhatsAppAsync(string? customPackagePath = null, CancellationToken cancellationToken = default);
    Task<ProcessResult> InstallAppxPackageDirectAsync(string packagePath, CancellationToken cancellationToken = default);
    Task<ProcessResult> DownloadWhatsAppPackageAsync(CancellationToken cancellationToken = default);
}
