using System.Text.RegularExpressions;
using Microsoft.Win32;
using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public partial class WingetService : IWingetService
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogService _logService;
    private readonly IConfigService _configService;

    public const string WhatsAppStoreId = "9NKSQGP7F2NH";
    public const string WhatsAppWin32Id = "WhatsApp.WhatsApp";

    [GeneratedRegex(@"^[A-Za-z0-9._\\{}\-]+$", RegexOptions.Compiled)]
    private static partial Regex PackageIdValidationRegex();

    [GeneratedRegex(@"^[A-Za-z0-9._\-+]+$", RegexOptions.Compiled)]
    private static partial Regex VersionValidationRegex();

    public WingetService(
        IProcessRunner processRunner,
        ILogService logService,
        IConfigService configService)
    {
        _processRunner = processRunner;
        _logService = logService;
        _configService = configService;
    }

    public async Task<bool> IsWingetInstalledAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _processRunner.RunAsync("winget", new[] { "--version" }, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<WingetPackage>> GetUpgradesAsync(bool includeUnknown = false, CancellationToken cancellationToken = default)
    {
        _logService.LogInfo("Bắt đầu quét các bản cập nhật ứng dụng qua winget...");

        var args = new List<string> { "upgrade", "--accept-source-agreements" };
        if (includeUnknown)
        {
            args.Add("--include-unknown");
        }

        var result = await _processRunner.RunAsync("winget", args, cancellationToken).ConfigureAwait(false);
        var packages = WingetParser.ParseTable(result.StandardOutput, hasAvailableColumn: true);

        // Retrieve pinned IDs to mark packages
        var pinnedIds = await GetPinnedPackageIdsAsync(cancellationToken).ConfigureAwait(false);
        var priorityIds = new HashSet<string>(_configService.CurrentConfig.PriorityAppIds, StringComparer.OrdinalIgnoreCase);

        foreach (var p in packages)
        {
            if (pinnedIds.Contains(p.Id))
            {
                p.IsPinned = true;
            }
            if (priorityIds.Contains(p.Id))
            {
                p.IsPriority = true;
            }
        }

        _logService.LogSuccess($"Quét hoàn tất: Phát hiện {packages.Count} ứng dụng có bản cập nhật mới.");
        return packages;
    }

    public async Task<IReadOnlyList<WingetPackage>> GetInstalledAppsAsync(string? filter = null, CancellationToken cancellationToken = default)
    {
        _logService.LogInfo("Đang tải danh sách toàn bộ ứng dụng đã cài đặt...");

        var args = new List<string> { "list", "--accept-source-agreements" };
        if (!string.IsNullOrWhiteSpace(filter))
        {
            ValidatePackageId(filter);
            args.AddRange(new[] { "--query", filter });
        }

        var result = await _processRunner.RunAsync("winget", args, cancellationToken).ConfigureAwait(false);
        var packages = WingetParser.ParseTable(result.StandardOutput, hasAvailableColumn: false);

        var pinnedIds = await GetPinnedPackageIdsAsync(cancellationToken).ConfigureAwait(false);
        var priorityIds = new HashSet<string>(_configService.CurrentConfig.PriorityAppIds, StringComparer.OrdinalIgnoreCase);

        foreach (var p in packages)
        {
            if (pinnedIds.Contains(p.Id))
            {
                p.IsPinned = true;
            }
            if (priorityIds.Contains(p.Id))
            {
                p.IsPriority = true;
            }
        }

        _logService.LogInfo($"Tìm thấy {packages.Count} ứng dụng đã cài đặt trên hệ thống.");
        return packages;
    }

    public async Task<IReadOnlyList<string>> GetAvailableVersionsAsync(string packageId, CancellationToken cancellationToken = default)
    {
        ValidatePackageId(packageId);
        _logService.LogInfo($"Đang tra cứu các phiên bản có sẵn của gói: {packageId}...");

        var args = new[] { "show", "--id", packageId, "--exact", "--versions", "--accept-source-agreements" };
        var result = await _processRunner.RunAsync("winget", args, cancellationToken).ConfigureAwait(false);

        var versions = WingetParser.ParseVersions(result.StandardOutput);
        return versions;
    }

    public async Task<IReadOnlyList<string>> GetPinnedPackageIdsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var args = new[] { "pin", "list" };
            var result = await _processRunner.RunAsync("winget", args, cancellationToken).ConfigureAwait(false);
            var pinned = WingetParser.ParsePinnedIds(result.StandardOutput);
            return pinned.ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public async Task<ProcessResult> UpgradePackageAsync(string packageId, CancellationToken cancellationToken = default)
    {
        ValidatePackageId(packageId);
        _logService.LogInfo($"Đang cập nhật ứng dụng [{packageId}]...");

        var args = new[]
        {
            "upgrade",
            "--id", packageId,
            "--exact",
            "--silent",
            "--accept-package-agreements",
            "--accept-source-agreements"
        };

        var result = await _processRunner.RunAsync("winget", args, cancellationToken).ConfigureAwait(false);
        LogProcessResult(result, $"Cập nhật ứng dụng [{packageId}]");
        return result;
    }

    public async Task<ProcessResult> UpgradeAllPackagesAsync(CancellationToken cancellationToken = default)
    {
        _logService.LogInfo("Đang tiến hành cập nhật TẤT CẢ các ứng dụng có bản mới...");

        var args = new[]
        {
            "upgrade",
            "--all",
            "--silent",
            "--accept-package-agreements",
            "--accept-source-agreements"
        };

        var result = await _processRunner.RunAsync("winget", args, cancellationToken).ConfigureAwait(false);
        LogProcessResult(result, "Cập nhật tất cả ứng dụng");
        return result;
    }

    public async Task<ProcessResult> UninstallPackageAsync(string packageId, CancellationToken cancellationToken = default)
    {
        ValidatePackageId(packageId);
        _logService.LogWarning($"Bắt đầu gỡ cài đặt ứng dụng: [{packageId}]...");

        var args = new[]
        {
            "uninstall",
            "--id", packageId,
            "--exact",
            "--silent"
        };

        var result = await _processRunner.RunAsync("winget", args, cancellationToken).ConfigureAwait(false);
        LogProcessResult(result, $"Gỡ ứng dụng [{packageId}]");
        return result;
    }

    public async Task<ProcessResult> InstallVersionAsync(string packageId, string version, bool force = true, CancellationToken cancellationToken = default)
    {
        ValidatePackageId(packageId);
        ValidateVersion(version);

        _logService.LogInfo($"Bắt đầu cài đặt/rollback ứng dụng [{packageId}] về phiên bản {version}...");

        var args = new List<string>
        {
            "install",
            "--id", packageId,
            "--exact",
            "--version", version,
            "--silent",
            "--accept-package-agreements",
            "--accept-source-agreements"
        };

        if (force)
        {
            args.Add("--force");
        }

        var result = await _processRunner.RunAsync("winget", args, cancellationToken).ConfigureAwait(false);
        LogProcessResult(result, $"Cài đặt phiên bản {version} cho [{packageId}]");
        return result;
    }

    public async Task<ProcessResult> PinPackageAsync(string packageId, CancellationToken cancellationToken = default)
    {
        ValidatePackageId(packageId);
        _logService.LogInfo($"Đang ghim phiên bản cho gói: [{packageId}]...");

        var args = new[] { "pin", "add", "--id", packageId, "--exact" };
        var result = await _processRunner.RunAsync("winget", args, cancellationToken).ConfigureAwait(false);
        LogProcessResult(result, $"Ghim gói [{packageId}]");
        return result;
    }

    public async Task<ProcessResult> UnpinPackageAsync(string packageId, CancellationToken cancellationToken = default)
    {
        ValidatePackageId(packageId);
        _logService.LogInfo($"Đang bỏ ghim phiên bản cho gói: [{packageId}]...");

        var args = new[] { "pin", "remove", "--id", packageId, "--exact" };
        var result = await _processRunner.RunAsync("winget", args, cancellationToken).ConfigureAwait(false);
        LogProcessResult(result, $"Bỏ ghim gói [{packageId}]");
        return result;
    }

    // ==========================================
    // WHATSAPP FAST / OFFLINE UPDATE FEATURES
    // ==========================================

    public async Task<WhatsAppStatus> CheckWhatsAppStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = new WhatsAppStatus();
        status.IsWuauservDisabled = IsWuauservDisabled();

        // Check if there is an offline cached package
        status.CachedInstallerPath = FindCachedWhatsAppInstaller();

        // 1. Check Store package first
        try
        {
            var apps = await GetInstalledAppsAsync(WhatsAppStoreId, cancellationToken).ConfigureAwait(false);
            var storePkg = apps.FirstOrDefault(p => string.Equals(p.Id, WhatsAppStoreId, StringComparison.OrdinalIgnoreCase));
            if (storePkg != null)
            {
                status.IsInstalled = true;
                status.PackageId = WhatsAppStoreId;
                status.PackageType = "Store";
                status.CurrentVersion = storePkg.CurrentVersion;
                return status;
            }
        }
        catch { }

        // 2. Check Win32 package
        try
        {
            var apps = await GetInstalledAppsAsync(WhatsAppWin32Id, cancellationToken).ConfigureAwait(false);
            var win32Pkg = apps.FirstOrDefault(p => string.Equals(p.Id, WhatsAppWin32Id, StringComparison.OrdinalIgnoreCase) ||
                                                    p.Name.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase));
            if (win32Pkg != null)
            {
                status.IsInstalled = true;
                status.PackageId = WhatsAppWin32Id;
                status.PackageType = "Win32";
                status.CurrentVersion = win32Pkg.CurrentVersion;
                return status;
            }
        }
        catch { }

        return status;
    }

    public async Task<ProcessResult> FastUpdateWhatsAppAsync(string? customPackagePath = null, CancellationToken cancellationToken = default)
    {
        string? installerPath = customPackagePath;

        // 1. Check if an offline installer was supplied or is present in Cache/LAN
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            installerPath = FindCachedWhatsAppInstaller();
        }

        // 2. If NO package found in Cache or LAN: AUTOMATICALLY DOWNLOAD IT!
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            _logService.LogInfo("Chưa có gói cài đặt WhatsApp trong bộ nhớ đệm hoặc mạng LAN. Đang tự động tải về...");
            var dlResult = await DownloadWhatsAppPackageAsync(cancellationToken).ConfigureAwait(false);
            if (!dlResult.Succeeded)
            {
                _logService.LogError("Không thể tự động tải gói WhatsApp: " + WingetExitCode.GetDescription(dlResult.ExitCode));
                return dlResult;
            }

            installerPath = FindCachedWhatsAppInstaller();
            if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
            {
                return new ProcessResult(-1, "", "Không tìm thấy file gói cài sau khi tải hoàn tất.", false);
            }
        }

        // 3. Install the package directly (Offline speed 3-5 seconds, bypassing Store & wuauserv)
        _logService.LogInfo($"Tìm thấy gói cài đặt: {Path.GetFileName(installerPath)}. Bắt đầu cập nhật siêu tốc...");

        if (installerPath.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) ||
            installerPath.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) ||
            installerPath.EndsWith(".appxbundle", StringComparison.OrdinalIgnoreCase))
        {
            return await InstallAppxPackageDirectAsync(installerPath, cancellationToken).ConfigureAwait(false);
        }

        // Win32 executable installer
        var procResult = await _processRunner.RunAsync(installerPath, new[] { "/silent", "/quiet", "/norestart" }, cancellationToken).ConfigureAwait(false);
        LogProcessResult(procResult, "Cài đặt WhatsApp Win32");
        return procResult;
    }

    public async Task<ProcessResult> InstallAppxPackageDirectAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        _logService.LogInfo($"Đang cài đặt gói MSIX trực tiếp qua PowerShell: {Path.GetFileName(packagePath)}...");

        // Safety: Path is verified and passed via ArgumentList
        string safePath = packagePath.Replace("'", "''");
        string command = $"Add-AppxPackage -Path '{safePath}' -ForceUpdateFromAnyVersion";

        var args = new[]
        {
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy", "Bypass",
            "-Command", command
        };

        var result = await _processRunner.RunAsync("powershell.exe", args, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
        {
            _logService.LogSuccess("Cập nhật WhatsApp siêu tốc thành công trong vài giây!");
        }
        else
        {
            _logService.LogError($"Lỗi cài đặt gói MSIX: {result.StandardError}");
        }
        return result;
    }

    public async Task<ProcessResult> DownloadWhatsAppPackageAsync(CancellationToken cancellationToken = default)
    {
        string cacheDir = _configService.GetCacheDirectory();
        Directory.CreateDirectory(cacheDir);

        _logService.LogInfo($"Bắt đầu tải gói cài đặt WhatsApp mới nhất vào thư mục: {cacheDir}...");

        bool wasDisabled = IsWuauservDisabled();
        if (wasDisabled)
        {
            _logService.LogWarning("Phát hiện dịch vụ Windows Update (wuauserv) đang bị khóa bởi WUB. Tạm thời kích hoạt để tải gói Store...");
            await SetWuauservStartModeAsync(3, cancellationToken).ConfigureAwait(false); // 3 = Demand/Manual
        }

        try
        {
            // Attempt downloading MS Store package first
            var args = new[]
            {
                "download",
                "--id", WhatsAppStoreId,
                "--source", "msstore",
                "--download-directory", cacheDir,
                "--accept-package-agreements",
                "--accept-source-agreements"
            };

            var result = await _processRunner.RunAsync("winget", args, cancellationToken).ConfigureAwait(false);

            // If store download fails, try winget win32 package as fallback
            if (!result.Succeeded)
            {
                _logService.LogWarning("Không tải được gói Store. Đang thử tải gói WhatsApp Win32...");
                var fbArgs = new[]
                {
                    "download",
                    "--id", WhatsAppWin32Id,
                    "--download-directory", cacheDir,
                    "--accept-package-agreements",
                    "--accept-source-agreements"
                };
                result = await _processRunner.RunAsync("winget", fbArgs, cancellationToken).ConfigureAwait(false);
            }

            if (result.Succeeded)
            {
                _logService.LogSuccess("Đã tải xong gói cài đặt WhatsApp vào bộ nhớ đệm!");

                // If Shared LAN directory configured and writable, copy package so all other PCs can use it without downloading!
                CopyCachedPackageToLan(cacheDir);
            }

            return result;
        }
        finally
        {
            if (wasDisabled)
            {
                _logService.LogInfo("Khôi phục lại trạng thái khóa Windows Update (Disabled) ban đầu...");
                await SetWuauservStartModeAsync(4, cancellationToken).ConfigureAwait(false); // 4 = Disabled
            }
        }
    }

    private string? FindCachedWhatsAppInstaller()
    {
        var searchDirs = new List<string>();

        // 1. Shared LAN directory (Highest priority for office PCs)
        if (!string.IsNullOrWhiteSpace(_configService.CurrentConfig.SharedLanDirectory) &&
            Directory.Exists(_configService.CurrentConfig.SharedLanDirectory))
        {
            searchDirs.Add(_configService.CurrentConfig.SharedLanDirectory);
        }

        // 2. Local cache directory
        string localCache = _configService.GetCacheDirectory();
        if (Directory.Exists(localCache))
        {
            searchDirs.Add(localCache);
        }

        foreach (var dir in searchDirs)
        {
            try
            {
                var files = Directory.GetFiles(dir, "*WhatsApp*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".appxbundle", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                if (files.Count > 0)
                {
                    return files[0].FullName;
                }
            }
            catch { }
        }

        return null;
    }

    private void CopyCachedPackageToLan(string localCacheDir)
    {
        string? lanDir = _configService.CurrentConfig.SharedLanDirectory;
        if (string.IsNullOrWhiteSpace(lanDir) || !Directory.Exists(lanDir)) return;

        try
        {
            var files = Directory.GetFiles(localCacheDir, "*WhatsApp*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            foreach (var file in files)
            {
                string dest = Path.Combine(lanDir, Path.GetFileName(file));
                if (!File.Exists(dest) || new FileInfo(file).Length != new FileInfo(dest).Length)
                {
                    File.Copy(file, dest, overwrite: true);
                    _logService.LogInfo($"Đã sao chép gói WhatsApp sang thư mục chia sẻ mạng LAN: {dest}");
                }
            }
        }
        catch (Exception ex)
        {
            _logService.LogWarning("Không thể sao chép gói sang mạng LAN: " + ex.Message);
        }
    }

    private static bool IsWuauservDisabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv");
            if (key != null)
            {
                var startVal = key.GetValue("Start");
                if (startVal is int val && val == 4) return true; // 4 = Disabled
            }
        }
        catch { }
        return false;
    }

    private async Task SetWuauservStartModeAsync(int startMode, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            string mode = startMode == 4 ? "disabled" : "demand";
            await _processRunner.RunAsync("sc.exe", new[] { "config", "wuauserv", $"start={mode}" }, ct).ConfigureAwait(false);
        }
        catch { }
    }

    private static void ValidatePackageId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !PackageIdValidationRegex().IsMatch(id))
        {
            throw new ArgumentException($"Mã định danh gói (Package ID) không hợp lệ: '{id}'", nameof(id));
        }
    }

    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || !VersionValidationRegex().IsMatch(version))
        {
            throw new ArgumentException($"Phiên bản không hợp lệ: '{version}'", nameof(version));
        }
    }

    private void LogProcessResult(ProcessResult result, string actionName)
    {
        if (result.Succeeded)
        {
            _logService.LogSuccess($"{actionName} thành công.");
        }
        else if (result.IsCancelled)
        {
            _logService.LogWarning($"{actionName} đã bị người dùng hủy.");
        }
        else
        {
            string desc = WingetExitCode.GetDescription(result.ExitCode);
            _logService.LogError($"{actionName} thất bại (ExitCode {result.ExitCode}): {desc}");
        }
    }
}
