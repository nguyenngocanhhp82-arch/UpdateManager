using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public interface IWindowsUpdateService
{
    Task<IReadOnlyList<WindowsUpdateItem>> SearchUpdatesAsync(bool includeHidden = false, CancellationToken cancellationToken = default);
    Task<WindowsUpdateInstallResult> InstallUpdatesAsync(IEnumerable<WindowsUpdateItem> updates, CancellationToken cancellationToken = default);
    Task<bool> SetUpdateHiddenAsync(WindowsUpdateItem update, bool hide, CancellationToken cancellationToken = default);
    Task<bool> HideKbArticleAsync(string kbNumber, CancellationToken cancellationToken = default);
    Task RestartComputerAsync();
}
