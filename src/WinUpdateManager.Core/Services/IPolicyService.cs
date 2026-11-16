using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public interface IPolicyService
{
    UpdatePolicyModel GetCurrentPolicies();
    void ApplyPolicies(UpdatePolicyModel model);
    void ResetToDefaults();

    bool IsWuauservDisabled();
    WindowsUpdateStatusInfo GetWindowsUpdateStatus();
    Task<ProcessResult> SetWuauservDisabledAsync(bool disable, bool protectSettings = true, CancellationToken cancellationToken = default);
    Task<ProcessResult> UnlockAndEnableWindowsUpdateAsync(CancellationToken cancellationToken = default);
}
