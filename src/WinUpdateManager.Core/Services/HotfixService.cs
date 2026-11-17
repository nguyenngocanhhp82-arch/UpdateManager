using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public partial class HotfixService : IHotfixService
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogService _logService;
    private readonly IWindowsUpdateService _windowsUpdateService;

    [GeneratedRegex(@"^\d{6,8}$", RegexOptions.Compiled)]
    private static partial Regex KbNumberRegex();

    [GeneratedRegex(@"^[A-Za-z0-9._\-~]+$", RegexOptions.Compiled)]
    private static partial Regex DismPackageNameRegex();

    public HotfixService(
        IProcessRunner processRunner,
        ILogService logService,
        IWindowsUpdateService windowsUpdateService)
    {
        _processRunner = processRunner;
        _logService = logService;
        _windowsUpdateService = windowsUpdateService;
    }

    public async Task<IReadOnlyList<InstalledHotfix>> GetInstalledHotfixesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<InstalledHotfix>();

            if (!OperatingSystem.IsWindows())
            {
                _logService.LogInfo("WMI Win32_QuickFixEngineering chỉ hoạt động trên hệ điều hành Windows.");
                return list;
            }

            try
            {
                _logService.LogInfo("Đang truy vấn danh sách bản vá KB đã cài đặt từ WMI (Win32_QuickFixEngineering)...");

                using var searcher = new ManagementObjectSearcher("SELECT HotFixID, Description, InstalledOn, InstalledBy FROM Win32_QuickFixEngineering");
                using var collection = searcher.Get();

                foreach (ManagementObject obj in collection)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    string id = obj["HotFixID"]?.ToString()?.Trim() ?? string.Empty;
                    string desc = obj["Description"]?.ToString()?.Trim() ?? string.Empty;
                    string installedOn = obj["InstalledOn"]?.ToString()?.Trim() ?? string.Empty;
                    string installedBy = obj["InstalledBy"]?.ToString()?.Trim() ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(id) && !string.Equals(id, "File 1", StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(new InstalledHotfix
                        {
                            HotFixId = id,
                            Description = desc,
                            InstalledOn = installedOn,
                            InstalledBy = installedBy
                        });
                    }
                }

                // Order by InstalledOn descending when possible
                list = list.OrderByDescending(h => h.InstalledOn).ToList();
                _logService.LogSuccess($"Tìm thấy {list.Count} bản cập nhật KB đã cài đặt.");
            }
            catch (Exception ex)
            {
                _logService.LogError("Lỗi khi truy vấn WMI Win32_QuickFixEngineering: " + ex.Message, ex);
            }

            return list;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProcessResult> UninstallHotfixAsync(string kbNumber, bool createRestorePoint = true, bool blockReinstall = true, CancellationToken cancellationToken = default)
    {
        string digits = ExtractKbDigits(kbNumber);
        if (!KbNumberRegex().IsMatch(digits))
        {
            throw new ArgumentException($"Số KB không hợp lệ: '{kbNumber}'. Yêu cầu từ 6 đến 8 chữ số.", nameof(kbNumber));
        }

        _logService.LogWarning($"--- Bắt đầu quy trình gỡ bỏ bản cập nhật KB{digits} ---");

        // 1. Kiểm tra trạng thái gói trong Registry CBS trước tiên
        if (OperatingSystem.IsWindows())
        {
            var (cbsPackageName, currentState) = FindCbsPackageByKb(digits);
            if (currentState == 128) // 0x80 = Permanent
            {
                string msg = $"Bản cập nhật KB{digits} ({cbsPackageName}) là gói hệ thống vĩnh viễn (Permanent / Servicing Stack). Windows đã tích hợp gói này vào nhân hệ điều hành và không cho phép gỡ bỏ độc lập.";
                _logService.LogError(msg);
                _logService.LogInfo("💡 Giải pháp nếu bản cập nhật này gây lỗi trên máy:");
                _logService.LogInfo("  1. Dùng tính năng 'Quay về điểm phục hồi hệ thống' (System Restore Point).");
                _logService.LogInfo("  2. Khởi động vào môi trường cứu hộ Windows (WinRE) và chọn 'Uninstall latest quality update'.");
                return new ProcessResult(87, string.Empty, msg);
            }
            else if (currentState == 80) // 0x50 = Superseded
            {
                string msg = $"Bản cập nhật KB{digits} ({cbsPackageName}) đã bị thay thế (Superseded) bởi một bản cập nhật tích lũy mới hơn trên máy tính. Bạn cần gỡ bản cập nhật tích lũy mới nhất hiện tại.";
                _logService.LogError(msg);
                return new ProcessResult(87, string.Empty, msg);
            }
        }

        // 2. Tạo Restore Point nếu được yêu cầu
        if (createRestorePoint)
        {
            await CreateRestorePointAsync($"Trước khi gỡ KB{digits}", cancellationToken).ConfigureAwait(false);
        }

        // 3. Tiến hành gỡ bỏ đa tầng:
        ProcessResult? result = null;

        // TẦNG 1: DISM với tên gói chính xác từ CBS Registry hoặc danh sách DISM
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var (cbsPackageName, _) = FindCbsPackageByKb(digits);
                string? targetPackage = cbsPackageName;

                if (string.IsNullOrWhiteSpace(targetPackage))
                {
                    var dismPackages = await GetDismPackagesAsync(cancellationToken).ConfigureAwait(false);
                    var matchingPkg = dismPackages.FirstOrDefault(p =>
                        p.PackageName.Contains(digits, StringComparison.OrdinalIgnoreCase));
                    targetPackage = matchingPkg?.PackageName;
                }

                if (!string.IsNullOrWhiteSpace(targetPackage))
                {
                    _logService.LogInfo($"Tìm thấy gói hệ thống DISM tương ứng: {targetPackage}. Đang tiến hành gỡ bỏ qua DISM...");
                    result = await RemoveDismPackageAsync(targetPackage, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logService.LogWarning($"Không thể gỡ trực tiếp qua DISM: {ex.Message}. Sẽ thử qua PowerShell...");
            }
        }

        // TẦNG 2: Gỡ qua PowerShell Remove-WindowsPackage (nếu DISM chưa gỡ thành công)
        if (OperatingSystem.IsWindows() && (result == null || (!result.Succeeded && result.ExitCode != 3010)))
        {
            try
            {
                _logService.LogInfo($"Đang thử gỡ bỏ bản cập nhật KB{digits} bằng PowerShell (Remove-WindowsPackage)...");
                string psCmd = $"$pkg = Get-WindowsPackage -Online | Where-Object {{ $_.PackageName -like '*KB{digits}*' -or $_.PackageName -like '*{digits}*' }} | Select-Object -First 1; if ($pkg) {{ Remove-WindowsPackage -Online -PackageName $pkg.PackageName -NoRestart }} else {{ exit 404 }}";

                var psArgs = new[]
                {
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy", "Bypass",
                    "-Command", psCmd
                };

                var psResult = await _processRunner.RunAsync("powershell.exe", psArgs, cancellationToken).ConfigureAwait(false);
                if (psResult.Succeeded || psResult.ExitCode == 3010)
                {
                    result = psResult;
                }
            }
            catch { }
        }

        // TẦNG 3: Fallback qua WUSA (hộp thoại chuẩn Windows)
        if (result == null || (!result.Succeeded && result.ExitCode != 3010))
        {
            _logService.LogInfo($"Đang mở hộp thoại gỡ cài đặt của Windows: wusa.exe /uninstall /kb:{digits}...");
            var args = new[]
            {
                "/uninstall",
                $"/kb:{digits}"
            };

            result = await _processRunner.RunAsync("wusa.exe", args, cancellationToken).ConfigureAwait(false);
        }

        if (result.ExitCode == 0)
        {
            _logService.LogSuccess($"Gỡ bỏ bản cập nhật KB{digits} thành công.");
        }
        else if (result.ExitCode == 3010)
        {
            _logService.LogSuccess($"Gỡ bỏ bản cập nhật KB{digits} thành công. Hệ thống cần KHỞI ĐỘNG LẠI.");
        }
        else
        {
            string errorExplanation = ExplainWusaErrorCode(result.ExitCode);
            _logService.LogError($"Gỡ bỏ KB{digits} thất bại (ExitCode {result.ExitCode}): {errorExplanation}");
            _logService.LogInfo("💡 Mẹo: Bạn có thể bấm nút '⚙ Mở Gỡ cài đặt của Windows' ở thanh công cụ phía trên để xem các bản vá được phép gỡ qua giao diện hệ thống.");
            return result;
        }

        // 4. Block reinstall if requested
        if (blockReinstall)
        {
            _logService.LogInfo($"Đang tiến hành chặn Windows tự cài đặt lại KB{digits}...");
            await _windowsUpdateService.HideKbArticleAsync(digits, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<bool> CreateRestorePointAsync(string description, CancellationToken cancellationToken = default)
    {
        _logService.LogInfo($"Đang tạo điểm phục hồi hệ thống (Restore Point): '{description}'...");

        try
        {
            string safeDesc = description.Replace("'", "''");
            string cmd = $"Checkpoint-Computer -Description '{safeDesc}' -RestorePointType 'MODIFY_SETTINGS'";

            var args = new[]
            {
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy", "Bypass",
                "-Command", cmd
            };

            var result = await _processRunner.RunAsync("powershell.exe", args, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                _logService.LogSuccess("Đã tạo điểm phục hồi hệ thống thành công.");
                return true;
            }
            else
            {
                _logService.LogWarning("Không thể tạo điểm phục hồi (có thể tính năng System Restore đang bị tắt bởi chính sách hệ thống). Tiến trình vẫn tiếp tục.");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logService.LogWarning($"Bỏ qua bước tạo Restore Point do ngoại lệ: {ex.Message}");
            return false;
        }
    }

    public async Task<IReadOnlyList<DismPackage>> GetDismPackagesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<DismPackage>();
        if (!OperatingSystem.IsWindows())
        {
            _logService.LogInfo("DISM chỉ hỗ trợ trên môi trường Windows.");
            return list;
        }

        _logService.LogInfo("Đang quét danh sách các gói hệ thống qua DISM (dism.exe /Online /Get-Packages)...");

        var args = new[] { "/Online", "/Get-Packages", "/Format:Table" };
        var result = await _processRunner.RunAsync("dism.exe", args, cancellationToken).ConfigureAwait(false);

        list = ParseDismPackagesTable(result.StandardOutput);
        _logService.LogInfo($"Tìm thấy {list.Count} gói hệ thống DISM.");
        return list;
    }

    public async Task<ProcessResult> RemoveDismPackageAsync(string packageName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageName) || !DismPackageNameRegex().IsMatch(packageName))
        {
            throw new ArgumentException($"Tên gói DISM không hợp lệ: '{packageName}'", nameof(packageName));
        }

        _logService.LogWarning($"Đang thực thi gỡ bỏ gói hệ thống qua DISM: {packageName}...");

        var args = new[]
        {
            "/Online",
            "/Remove-Package",
            $"/PackageName:{packageName}",
            "/Quiet",
            "/NoRestart"
        };

        var result = await _processRunner.RunAsync("dism.exe", args, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
        {
            _logService.LogSuccess($"Gỡ bỏ gói DISM '{packageName}' thành công.");
        }
        else
        {
            _logService.LogError($"Gỡ bỏ gói DISM '{packageName}' thất bại (ExitCode {result.ExitCode}): {result.StandardError}");
        }

        return result;
    }

    public static (string? packageName, int currentState) FindCbsPackageByKb(string digits)
    {
        if (!OperatingSystem.IsWindows()) return (null, 0);

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\Packages");
            if (key != null)
            {
                var names = key.GetSubKeyNames();
                // Tìm subkey chứa chuỗi số KB
                var match = names.FirstOrDefault(n =>
                    n.Contains(digits, StringComparison.OrdinalIgnoreCase) &&
                    (n.StartsWith("Package_for_", StringComparison.OrdinalIgnoreCase) || n.Contains("KB", StringComparison.OrdinalIgnoreCase)));

                match ??= names.FirstOrDefault(n => n.Contains(digits, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    using var subKey = key.OpenSubKey(match);
                    int currentState = 0;
                    if (subKey?.GetValue("CurrentState") is int stateVal)
                    {
                        currentState = stateVal;
                    }
                    return (match, currentState);
                }
            }
        }
        catch { }

        return (null, 0);
    }

    public async Task<ProcessResult> OpenInstalledUpdatesControlPanelAsync(CancellationToken cancellationToken = default)
    {
        _logService.LogInfo("Đang mở cửa sổ quản lý gỡ bản cập nhật của Windows...");
        if (!OperatingSystem.IsWindows())
        {
            return new ProcessResult(1, string.Empty, "Chỉ khả dụng trên Windows");
        }

        try
        {
            // Thử mở Settings Update History trước
            var res = await _processRunner.RunAsync("cmd.exe", new[] { "/c", "start", "ms-settings:windowsupdate-history" }, cancellationToken).ConfigureAwait(false);
            if (!res.Succeeded)
            {
                // Dự phòng mở Control Panel Installed Updates cổ điển
                res = await _processRunner.RunAsync("control.exe", new[] { "/name", "Microsoft.ProgramsAndFeatures", "/page", "pageInstalledUpdates" }, cancellationToken).ConfigureAwait(false);
            }
            return res;
        }
        catch (Exception ex)
        {
            return new ProcessResult(1, string.Empty, ex.Message);
        }
    }

    public static List<DismPackage> ParseDismPackagesTable(string rawOutput)
    {
        var result = new List<DismPackage>();
        if (string.IsNullOrWhiteSpace(rawOutput)) return result;

        var lines = rawOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        int separatorIdx = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("---") && lines[i].Count(c => c == '-') >= 10)
            {
                separatorIdx = i;
                break;
            }
        }

        if (separatorIdx <= 0) return result;

        for (int i = separatorIdx + 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("The operation completed", StringComparison.OrdinalIgnoreCase)) break;

            string[] parts = line.Contains('|')
                ? line.Split('|').Select(p => p.Trim()).ToArray()
                : Regex.Split(line, @"\s{2,}").Select(p => p.Trim()).ToArray();

            if (parts.Length >= 2)
            {
                string pkg = parts[0];
                string state = parts.Length > 1 ? parts[1] : "";
                string release = parts.Length > 2 ? parts[2] : "";
                string time = parts.Length > 3 ? parts[3] : "";

                if (!string.IsNullOrWhiteSpace(pkg) && !pkg.StartsWith("Package Identity", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new DismPackage
                    {
                        PackageName = pkg,
                        State = state,
                        ReleaseType = release,
                        InstallTime = time
                    });
                }
            }
        }

        return result;
    }

    private static string ExtractKbDigits(string kbNumber)
    {
        if (string.IsNullOrWhiteSpace(kbNumber)) return string.Empty;
        var match = Regex.Match(kbNumber, @"\d+");
        return match.Success ? match.Value : kbNumber.Trim();
    }

    private static string ExplainWusaErrorCode(int exitCode)
    {
        return exitCode switch
        {
            87 => "Tham số không hợp lệ (Mã lỗi 87 - 0x00000057 / ERROR_INVALID_PARAMETER). Bản cập nhật này thường là bản vá tích lũy cốt lõi (Cumulative Update) hoặc bản vá nền tảng (Servicing Stack) mà Windows đã khóa vĩnh viễn (Permanent) không cho phép gỡ bỏ qua dòng lệnh.",
            unchecked((int)0x80070057) => "Tham số không hợp lệ (Mã lỗi 87 - 0x00000057 / ERROR_INVALID_PARAMETER). Bản cập nhật này thường là bản vá tích lũy cốt lõi (Cumulative Update) hoặc bản vá nền tảng (Servicing Stack) mà Windows đã khóa vĩnh viễn (Permanent) không cho phép gỡ bỏ qua dòng lệnh.",
            unchecked((int)0x80240017) => "Bản cập nhật này không thể gỡ qua WUSA (thường là bản cập nhật tích lũy bắt buộc Cumulative Update). Bạn có thể thử gỡ gói RollupFix qua mục DISM.",
            unchecked((int)0x800f081f) => "Không tìm thấy nguồn hoặc gói cập nhật trên máy.",
            unchecked((int)0x800f0825) => "Gói cập nhật này đã được tích hợp vĩnh viễn (Permanent package) và không thể gỡ bỏ.",
            unchecked((int)0x80070005) => "Truy cập bị từ chối (cần quyền Administrator).",
            unchecked((int)0x80240024) => "Bản cập nhật không tồn tại hoặc đã được gỡ trước đó.",
            _ => $"Lỗi Windows Update Standalone Installer: mã lỗi {exitCode} (0x{exitCode:X8})."
        };
    }

    private static string SafeSubstring(string text, int startIndex, int? length)
    {
        if (startIndex >= text.Length) return string.Empty;
        if (!length.HasValue) return text.Substring(startIndex);

        int maxLen = Math.Min(length.Value, text.Length - startIndex);
        return maxLen > 0 ? text.Substring(startIndex, maxLen) : string.Empty;
    }
}
