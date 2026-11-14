using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public interface IHotfixService
{
    Task<IReadOnlyList<InstalledHotfix>> GetInstalledHotfixesAsync(CancellationToken cancellationToken = default);
    Task<ProcessResult> UninstallHotfixAsync(string kbNumber, bool createRestorePoint = true, bool blockReinstall = true, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DismPackage>> GetDismPackagesAsync(CancellationToken cancellationToken = default);
    Task<ProcessResult> RemoveDismPackageAsync(string packageName, CancellationToken cancellationToken = default);
    Task<bool> CreateRestorePointAsync(string description, CancellationToken cancellationToken = default);
}
