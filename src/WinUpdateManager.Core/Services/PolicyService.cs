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

    public WindowsUpdateStatusInfo GetWindowsUpdateStatus()
    {
        var status = new WindowsUpdateStatusInfo();
        if (!OperatingSystem.IsWindows())
        {
            status.DisplayStatus = "Không khả dụng (Không phải Windows)";
            status.StatusDescription = "Hệ điều hành hiện tại không phải Windows.";
            status.StatusColor = "#6B7280";
            return status;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv");
            if (key != null)
            {
                var startVal = key.GetValue("Start") as int?;
                bool isProtected = key.GetValue("Security") != null;
                status.IsProtectedByWub = isProtected;

                if (startVal == 4)
                {
                    status.IsDisabled = true;
                    status.ServiceStartMode = "Disabled";
                    status.DisplayStatus = "Đã Khóa (Disabled)";
                    status.StatusDescription = isProtected
                        ? "Dịch vụ đang bị khóa và bảo vệ quyền truy cập (Chế độ WUB)."
                        : "Dịch vụ cập nhật hệ thống đang bị tắt (Disabled).";
                    status.StatusColor = "#DC2626"; // Red
                }
                else
                {
                    status.IsDisabled = false;
                    status.ServiceStartMode = startVal == 2 ? "Automatic" : "Manual";
                    status.DisplayStatus = "Đang Bật (Enabled)";
                    status.StatusDescription = "Dịch vụ cập nhật hệ thống đang mở và sẵn sàng hoạt động.";
                    status.StatusColor = "#059669"; // Green
                }
            }
            else
            {
                status.IsDisabled = true;
                status.DisplayStatus = "Không tìm thấy dịch vụ";
                status.StatusColor = "#DC2626";
            }
        }
        catch (Exception ex)
        {
            _logService.LogWarning("Không thể đọc trực tiếp trạng thái wuauserv: " + ex.Message);
            status.IsDisabled = true;
            status.DisplayStatus = "Đang Bị Khóa / Giới Hạn";
            status.StatusColor = "#DC2626";
        }

        return status;
    }

    public async Task<ProcessResult> UnlockAndEnableWindowsUpdateAsync(CancellationToken cancellationToken = default)
    {
        _logService.LogWarning("Bắt đầu mở khóa và bật lại Windows Update (Phá khóa WUB / Khôi phục toàn diện)...");

        // 1. Phá bỏ khóa quyền ACL Registry do WUB hoặc hệ thống thiết lập
        _logService.LogInfo("Đang chiếm quyền sở hữu (Take Ownership) và gỡ bỏ hạn chế quyền 'Access is denied'...");
        WindowsSecurityHelper.UnlockServiceRegistryKey("wuauserv");
        WindowsSecurityHelper.UnlockServiceRegistryKey("WaaSMedicSvc");
        WindowsSecurityHelper.UnlockServiceRegistryKey("UsoSvc");

        // 2. Khôi phục quyền SDDL chuẩn của Service Control Manager
        string defaultSddl = "D:(A;;CCLCSWRPLORC;;;AU)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;SY)S:(AU;FA;CCDCLCSWRPWPDTLOSDRCWDWO;;;WD)";
        await _processRunner.RunAsync("sc.exe", new[] { "sdset", "wuauserv", defaultSddl }, cancellationToken).ConfigureAwait(false);
        await _processRunner.RunAsync("sc.exe", new[] { "sdset", "WaaSMedicSvc", defaultSddl }, cancellationToken).ConfigureAwait(false);
        await _processRunner.RunAsync("sc.exe", new[] { "sdset", "UsoSvc", defaultSddl }, cancellationToken).ConfigureAwait(false);

        // 3. Nếu tìm thấy công cụ WUB trên máy, gọi lệnh /e của WUB
        string? wubExe = WindowsSecurityHelper.FindWubExecutable();
        if (!string.IsNullOrWhiteSpace(wubExe))
        {
            _logService.LogInfo($"Phát hiện phần mềm WUB tại '{wubExe}'. Đang gọi lệnh Wub.exe /e...");
            await _processRunner.RunAsync(wubExe, new[] { "/e" }, cancellationToken).ConfigureAwait(false);
        }

        // 4. Cấu hình kiểu khởi động (Demand / Manual)
        var scResult = await _processRunner.RunAsync("sc.exe", new[] { "config", "wuauserv", "start=demand" }, cancellationToken).ConfigureAwait(false);
        await _processRunner.RunAsync("sc.exe", new[] { "config", "WaaSMedicSvc", "start=demand" }, cancellationToken).ConfigureAwait(false);
        await _processRunner.RunAsync("sc.exe", new[] { "config", "UsoSvc", "start=delayed-auto" }, cancellationToken).ConfigureAwait(false);

        // Ghi trực tiếp Start=3 vào Registry làm phương án bảo đảm
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv", writable: true);
            key?.SetValue("Start", 3, RegistryValueKind.DWord);
        }
        catch { }

        // 5. Khởi động dịch vụ wuauserv
        await _processRunner.RunAsync("sc.exe", new[] { "start", "wuauserv" }, cancellationToken).ConfigureAwait(false);

        // 6. Xóa cờ NoAutoUpdate
        try
        {
            using var keyAu = Registry.LocalMachine.CreateSubKey(KeyWindowsUpdateAu);
            keyAu.SetValue("NoAutoUpdate", 0, RegistryValueKind.DWord);
        }
        catch { }

        _logService.LogSuccess("Mở khóa thành công! Dịch vụ Windows Update (wuauserv) đã hoạt động trở lại bình thường.");
        return scResult;
    }

    public async Task<ProcessResult> SetWuauservDisabledAsync(bool disable, bool protectSettings = true, CancellationToken cancellationToken = default)
    {
        if (!disable)
        {
            return await UnlockAndEnableWindowsUpdateAsync(cancellationToken).ConfigureAwait(false);
        }

        _logService.LogWarning("Đang thực hiện khóa triệt để Windows Update (Chế độ tương thích WUB)...");

        // Trước khi cấu hình, đảm bảo process có quyền ghi Registry
        WindowsSecurityHelper.UnlockServiceRegistryKey("wuauserv");
        WindowsSecurityHelper.UnlockServiceRegistryKey("WaaSMedicSvc");
        WindowsSecurityHelper.UnlockServiceRegistryKey("UsoSvc");

        // 1. Dừng các dịch vụ đang chạy
        await _processRunner.RunAsync("net.exe", new[] { "stop", "wuauserv" }, cancellationToken).ConfigureAwait(false);
        await _processRunner.RunAsync("net.exe", new[] { "stop", "WaaSMedicSvc" }, cancellationToken).ConfigureAwait(false);
        await _processRunner.RunAsync("net.exe", new[] { "stop", "UsoSvc" }, cancellationToken).ConfigureAwait(false);

        // 2. Chuyển cấu hình dịch vụ sang Disabled
        var result = await _processRunner.RunAsync("sc.exe", new[] { "config", "wuauserv", "start=disabled" }, cancellationToken).ConfigureAwait(false);
        await _processRunner.RunAsync("sc.exe", new[] { "config", "WaaSMedicSvc", "start=disabled" }, cancellationToken).ConfigureAwait(false);
        await _processRunner.RunAsync("sc.exe", new[] { "config", "UsoSvc", "start=disabled" }, cancellationToken).ConfigureAwait(false);

        // Ghi trực tiếp Start=4 vào Registry
        try
        {
            using var keyWu = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv", writable: true);
            keyWu?.SetValue("Start", 4, RegistryValueKind.DWord);

            using var keyMedic = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WaaSMedicSvc", writable: true);
            keyMedic?.SetValue("Start", 4, RegistryValueKind.DWord);

            using var keyUso = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\UsoSvc", writable: true);
            keyUso?.SetValue("Start", 4, RegistryValueKind.DWord);
        }
        catch { }

        // 3. Kích hoạt chính sách cấm tự động cập nhật
        try
        {
            using var keyAu = Registry.LocalMachine.CreateSubKey(KeyWindowsUpdateAu);
            keyAu.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
        }
        catch { }

        // 4. Nếu có WUB trên máy và người dùng chọn Protect, gọi Wub.exe /d /p
        if (protectSettings)
        {
            string? wubExe = WindowsSecurityHelper.FindWubExecutable();
            if (!string.IsNullOrWhiteSpace(wubExe))
            {
                _logService.LogInfo($"Phát hiện phần mềm WUB tại '{wubExe}'. Đang áp dụng bảo vệ qua Wub.exe /d /p...");
                await _processRunner.RunAsync(wubExe, new[] { "/d", "/p" }, cancellationToken).ConfigureAwait(false);
            }
        }

        _logService.LogSuccess("Đã khóa và dừng triệt để dịch vụ Windows Update (wuauserv, WaaSMedicSvc, UsoSvc).");
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
