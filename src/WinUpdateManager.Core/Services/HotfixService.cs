using System.Management;
using System.Text.RegularExpressions;
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

        // 1. Create Restore Point if requested
        if (createRestorePoint)
        {
            await CreateRestorePointAsync($"Trước khi gỡ KB{digits}", cancellationToken).ConfigureAwait(false);
        }

        // 2. Execute WUSA
        _logService.LogInfo($"Đang thực thi lệnh: wusa.exe /uninstall /kb:{digits} /quiet /norestart...");
        var args = new[]
        {
            "/uninstall",
            $"/kb:{digits}",
            "/quiet",
            "/norestart"
        };

        var result = await _processRunner.RunAsync("wusa.exe", args, cancellationToken).ConfigureAwait(false);

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
            return result;
        }

        // 3. Block reinstall if requested
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

            if (line.Contains('|'))
            {
                var parts = line.Split('|');
                if (parts.Length >= 2)
                {
                    string pkg = parts[0].Trim();
                    string state = parts.Length > 1 ? parts[1].Trim() : "";
                    string release = parts.Length > 2 ? parts[2].Trim() : "";
                    string time = parts.Length > 3 ? parts[3].Trim() : "";

                    if (!string.IsNullOrWhiteSpace(pkg))
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
            unchecked((int)0x80240017) => "Bản cập nhật này không thể gỡ qua WUSA (thường là bản cập nhật tích lũy bắt buộc Cumulative Update). Bạn có thể thử gỡ gói RollupFix qua mục DISM.",
            unchecked((int)0x800f081f) => "Không tìm thấy nguồn hoặc gói cập nhật trên máy.",
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
