using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public interface IPolicyService
{
    UpdatePolicyModel GetCurrentPolicies();
    void ApplyPolicies(UpdatePolicyModel model);
    void ResetToDefaults();

    bool IsWuauservDisabled();
    Task<ProcessResult> SetWuauservDisabledAsync(bool disable, CancellationToken cancellationToken = default);
}
