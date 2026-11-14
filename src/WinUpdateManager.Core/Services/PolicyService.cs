using Microsoft.Win32;
using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public class PolicyService : IPolicyService
{
    private readonly ILogService _logService;
    private readonly IProcessRunner _processRunner;

    private const string KeyWindowsUpdate = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string KeyWindowsUpdateAu = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU";
    private const string KeyWindowsStore = @"SOFTWARE\Policies\Microsoft\WindowsStore";

    public PolicyService(ILogService logService, IProcessRunner processRunner)
    {
        _logService = logService;
        _processRunner = processRunner;
    }

    public UpdatePolicyModel GetCurrentPolicies()
    {
        var model = new UpdatePolicyModel();
        model.IsWuauservDisabled = IsWuauservDisabled();

        if (!OperatingSystem.IsWindows())
        {
            return model;
        }

        try
        {
            // 1. Read WindowsUpdate\AU
            using (var keyAu = Registry.LocalMachine.OpenSubKey(KeyWindowsUpdateAu))
            {
                if (keyAu != null)
                {
                    int? noAutoUpdate = keyAu.GetValue("NoAutoUpdate") as int?;
                    int? auOptions = keyAu.GetValue("AUOptions") as int?;

                    if (noAutoUpdate == 1)
                    {
                        model.AuMode = AuOptionMode.CompletelyDisabled;
                    }
                    else if (auOptions.HasValue)
                    {
                        model.AuMode = auOptions.Value switch
                        {
                            2 => AuOptionMode.NotifyBeforeDownload,
                            3 => AuOptionMode.AutoDownloadNotifyInstall,
                            4 => AuOptionMode.AutoDownloadAndInstall,
                            _ => AuOptionMode.Default
                        };
                    }
                    else
                    {
                        model.AuMode = AuOptionMode.Default;
                    }

                    int? noReboot = keyAu.GetValue("NoAutoRebootWithLoggedOnUsers") as int?;
                    model.NoAutoRebootWithLoggedOnUsers = noReboot == 1;
                }
                else
                {
                    model.AuMode = AuOptionMode.Default;
                }
            }

            // 2. Read WindowsUpdate
            using (var keyWu = Registry.LocalMachine.OpenSubKey(KeyWindowsUpdate))
            {
                if (keyWu != null)
                {
                    model.DeferQualityUpdates = (keyWu.GetValue("DeferQualityUpdates") as int?) == 1;
                    model.DeferQualityDays = (keyWu.GetValue("DeferQualityUpdatesPeriodInDays") as int?) ?? 7;

                    model.DeferFeatureUpdates = (keyWu.GetValue("DeferFeatureUpdates") as int?) == 1;
                    model.DeferFeatureDays = (keyWu.GetValue("DeferFeatureUpdatesPeriodInDays") as int?) ?? 30;

                    model.TargetReleaseVersionEnabled = (keyWu.GetValue("TargetReleaseVersion") as int?) == 1;
                    model.ProductVersion = (keyWu.GetValue("ProductVersion") as string) ?? DetectDefaultProductVersion();
                    model.TargetReleaseVersionInfo = (keyWu.GetValue("TargetReleaseVersionInfo") as string) ?? "23H2";

                    model.ExcludeDriversInQualityUpdate = (keyWu.GetValue("ExcludeWUDriversInQualityUpdate") as int?) == 1;

                    model.ActiveHoursEnabled = (keyWu.GetValue("SetActiveHours") as int?) == 1;
                    model.ActiveHoursStart = (keyWu.GetValue("ActiveHoursStart") as int?) ?? 8;
                    model.ActiveHoursEnd = (keyWu.GetValue("ActiveHoursEnd") as int?) ?? 18;
                }
                else
                {
                    model.ProductVersion = DetectDefaultProductVersion();
                }
            }

            // 3. Read WindowsStore
            using (var keyStore = Registry.LocalMachine.OpenSubKey(KeyWindowsStore))
            {
                if (keyStore != null)
                {
                    int? autoDownload = keyStore.GetValue("AutoDownload") as int?;
                    model.DisableStoreAutoDownload = autoDownload == 2;
                }
            }
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi đọc cấu hình chính sách từ Registry: " + ex.Message, ex);
        }

        return model;
    }

    public void ApplyPolicies(UpdatePolicyModel model)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logService.LogInfo("Thao tác ghi Registry chính sách chỉ khả dụng trên Windows.");
            return;
        }

        try
        {
            _logService.LogInfo("Đang lưu các chính sách cập nhật vào Windows Registry (HKLM)...");

            // 1. Ghi WindowsUpdate\AU
            using (var keyAu = Registry.LocalMachine.CreateSubKey(KeyWindowsUpdateAu))
            {
                switch (model.AuMode)
                {
                    case AuOptionMode.CompletelyDisabled:
                        keyAu.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
                        keyAu.DeleteValue("AUOptions", false);
                        break;
                    case AuOptionMode.NotifyBeforeDownload:
                        keyAu.SetValue("NoAutoUpdate", 0, RegistryValueKind.DWord);
                        keyAu.SetValue("AUOptions", 2, RegistryValueKind.DWord);
                        break;
                    case AuOptionMode.AutoDownloadNotifyInstall:
                        keyAu.SetValue("NoAutoUpdate", 0, RegistryValueKind.DWord);
                        keyAu.SetValue("AUOptions", 3, RegistryValueKind.DWord);
                        break;
                    case AuOptionMode.AutoDownloadAndInstall:
                        keyAu.SetValue("NoAutoUpdate", 0, RegistryValueKind.DWord);
                        keyAu.SetValue("AUOptions", 4, RegistryValueKind.DWord);
                        break;
                    case AuOptionMode.Default:
                    default:
                        keyAu.DeleteValue("NoAutoUpdate", false);
                        keyAu.DeleteValue("AUOptions", false);
                        break;
                }

                if (model.NoAutoRebootWithLoggedOnUsers)
                {
                    keyAu.SetValue("NoAutoRebootWithLoggedOnUsers", 1, RegistryValueKind.DWord);
                }
                else
                {
                    keyAu.DeleteValue("NoAutoRebootWithLoggedOnUsers", false);
                }
            }

            // 2. Ghi WindowsUpdate
            using (var keyWu = Registry.LocalMachine.CreateSubKey(KeyWindowsUpdate))
            {
                // Defer Quality Updates
                if (model.DeferQualityUpdates)
                {
                    keyWu.SetValue("DeferQualityUpdates", 1, RegistryValueKind.DWord);
                    keyWu.SetValue("DeferQualityUpdatesPeriodInDays", Math.Clamp(model.DeferQualityDays, 0, 30), RegistryValueKind.DWord);
                }
                else
                {
                    keyWu.DeleteValue("DeferQualityUpdates", false);
                    keyWu.DeleteValue("DeferQualityUpdatesPeriodInDays", false);
                }

                // Defer Feature Updates
                if (model.DeferFeatureUpdates)
                {
                    keyWu.SetValue("DeferFeatureUpdates", 1, RegistryValueKind.DWord);
                    keyWu.SetValue("DeferFeatureUpdatesPeriodInDays", Math.Clamp(model.DeferFeatureDays, 0, 365), RegistryValueKind.DWord);
                }
                else
                {
                    keyWu.DeleteValue("DeferFeatureUpdates", false);
                    keyWu.DeleteValue("DeferFeatureUpdatesPeriodInDays", false);
                }

                // Target Release Version Lock
                if (model.TargetReleaseVersionEnabled)
                {
                    keyWu.SetValue("TargetReleaseVersion", 1, RegistryValueKind.DWord);
                    keyWu.SetValue("ProductVersion", model.ProductVersion, RegistryValueKind.String);
                    keyWu.SetValue("TargetReleaseVersionInfo", model.TargetReleaseVersionInfo, RegistryValueKind.String);
                }
                else
                {
                    keyWu.DeleteValue("TargetReleaseVersion", false);
                    keyWu.DeleteValue("ProductVersion", false);
                    keyWu.DeleteValue("TargetReleaseVersionInfo", false);
                }

                // Exclude WU Drivers
                if (model.ExcludeDriversInQualityUpdate)
                {
                    keyWu.SetValue("ExcludeWUDriversInQualityUpdate", 1, RegistryValueKind.DWord);
                }
                else
                {
                    keyWu.DeleteValue("ExcludeWUDriversInQualityUpdate", false);
                }

                // Active Hours
                if (model.ActiveHoursEnabled)
                {
                    keyWu.SetValue("SetActiveHours", 1, RegistryValueKind.DWord);
                    keyWu.SetValue("ActiveHoursStart", Math.Clamp(model.ActiveHoursStart, 0, 23), RegistryValueKind.DWord);
                    keyWu.SetValue("ActiveHoursEnd", Math.Clamp(model.ActiveHoursEnd, 0, 23), RegistryValueKind.DWord);
                }
                else
                {
                    keyWu.DeleteValue("SetActiveHours", false);
                    keyWu.DeleteValue("ActiveHoursStart", false);
                    keyWu.DeleteValue("ActiveHoursEnd", false);
                }
            }

            // 3. Ghi WindowsStore
            using (var keyStore = Registry.LocalMachine.CreateSubKey(KeyWindowsStore))
            {
                if (model.DisableStoreAutoDownload)
                {
                    keyStore.SetValue("AutoDownload", 2, RegistryValueKind.DWord);
                }
                else
                {
                    keyStore.DeleteValue("AutoDownload", false);
                }
            }

            _logService.LogSuccess("Đã lưu và áp dụng thành công toàn bộ chính sách Windows Update vào hệ thống.");
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi ghi chính sách Registry: " + ex.Message, ex);
        }
    }

    public void ResetToDefaults()
    {
        if (!OperatingSystem.IsWindows())
        {
            _logService.LogInfo("Thao tác khôi phục mặc định Registry chỉ khả dụng trên Windows.");
            return;
        }

        try
        {
            _logService.LogWarning("Bắt đầu khôi phục mặc định: Đang xóa sạch các khóa chính sách Windows Update đã thiết lập...");

            // Delete values from AU
            using (var keyAu = Registry.LocalMachine.OpenSubKey(KeyWindowsUpdateAu, writable: true))
            {
                if (keyAu != null)
                {
                    keyAu.DeleteValue("NoAutoUpdate", false);
                    keyAu.DeleteValue("AUOptions", false);
                    keyAu.DeleteValue("NoAutoRebootWithLoggedOnUsers", false);
                }
            }

            // Delete values from WindowsUpdate
            using (var keyWu = Registry.LocalMachine.OpenSubKey(KeyWindowsUpdate, writable: true))
            {
                if (keyWu != null)
                {
                    keyWu.DeleteValue("DeferQualityUpdates", false);
                    keyWu.DeleteValue("DeferQualityUpdatesPeriodInDays", false);
                    keyWu.DeleteValue("DeferFeatureUpdates", false);
                    keyWu.DeleteValue("DeferFeatureUpdatesPeriodInDays", false);
                    keyWu.DeleteValue("TargetReleaseVersion", false);
                    keyWu.DeleteValue("ProductVersion", false);
                    keyWu.DeleteValue("TargetReleaseVersionInfo", false);
                    keyWu.DeleteValue("ExcludeWUDriversInQualityUpdate", false);
                    keyWu.DeleteValue("SetActiveHours", false);
                    keyWu.DeleteValue("ActiveHoursStart", false);
                    keyWu.DeleteValue("ActiveHoursEnd", false);
                }
            }

            // Delete values from WindowsStore
            using (var keyStore = Registry.LocalMachine.OpenSubKey(KeyWindowsStore, writable: true))
            {
                if (keyStore != null)
                {
                    keyStore.DeleteValue("AutoDownload", false);
                }
            }

            _logService.LogSuccess("Khôi phục mặc định thành công! Toàn bộ chính sách can thiệp đã được gỡ bỏ.");
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi khôi phục chính sách mặc định: " + ex.Message, ex);
        }
    }

    public bool IsWuauservDisabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv");
            if (key != null)
            {
                var val = key.GetValue("Start") as int?;
                return val == 4; // 4 = Disabled
            }
        }
        catch { }
        return false;
    }

    public async Task<ProcessResult> SetWuauservDisabledAsync(bool disable, CancellationToken cancellationToken = default)
    {
        string mode = disable ? "disabled" : "demand";
        string actionDesc = disable ? "Khóa/Tắt dịch vụ Windows Update (wuauserv)" : "Bật lại dịch vụ Windows Update (wuauserv - Manual)";

        _logService.LogWarning($"Đang thực hiện: {actionDesc}...");

        var result = await _processRunner.RunAsync("sc.exe", new[] { "config", "wuauserv", $"start={mode}" }, cancellationToken).ConfigureAwait(false);

        if (disable)
        {
            // Also stop running service
            await _processRunner.RunAsync("net.exe", new[] { "stop", "wuauserv" }, cancellationToken).ConfigureAwait(false);
            _logService.LogSuccess("Dịch vụ wuauserv đã được khóa (Disabled) và dừng hoạt động.");
        }
        else
        {
            _logService.LogSuccess("Dịch vụ wuauserv đã được chuyển sang chế độ Thủ công (Manual). Hãy nhớ cấu hình lại sau khi xong.");
        }

        return result;
    }

    private static string DetectDefaultProductVersion()
    {
        try
        {
            if (Environment.OSVersion.Version.Build >= 22000)
            {
                return "Windows 11";
            }
        }
        catch { }
        return "Windows 10";
    }
}
