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

    [GeneratedRegex(@"^[A-Za-z0-9._\\{}\-+()#@/:\s~]+$", RegexOptions.Compiled)]
    private static partial Regex PackageIdValidationRegex();

    [GeneratedRegex(@"^[A-Za-z0-9._\-+()#@/:\s~]+$", RegexOptions.Compiled)]
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
            p.IsSelected = false; // Mặc định KHÔNG tự động tích chọn bất kỳ app nào (để người dùng chủ động tích chọn)

            if (pinnedIds.Contains(p.Id))
            {
                p.IsPinned = true;
            }
            if (priorityIds.Contains(p.Id))
            {
                p.IsPriority = true;
            }

            if (IsExcludedPackage(p.Id, p.Name))
            {
                p.IsExcluded = true;
                p.WarningNote = "⚠️ Ứng dụng nhạy cảm (Office/Zalo) - Khuyên dùng cập nhật nội bộ của app!";
            }

            // Fix empty or <unknown> current versions
            if (string.IsNullOrWhiteSpace(p.CurrentVersion) ||
                p.CurrentVersion.Equals("<unknown>", StringComparison.OrdinalIgnoreCase))
            {
                if (p.Id.Equals(WhatsAppStoreId, StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase))
                {
                    string? ver = await GetInstalledAppxVersionAsync("*WhatsApp*", cancellationToken).ConfigureAwait(false);
                    p.CurrentVersion = !string.IsNullOrWhiteSpace(ver) ? ver : "Đã cài đặt (Store)";
                }
                else
                {
                    p.CurrentVersion = "Chưa rõ (Store)";
                }
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
            p.IsSelected = false; // Mặc định KHÔNG tự động tích chọn bất kỳ app nào (để người dùng chủ động tích chọn)

            if (pinnedIds.Contains(p.Id))
            {
                p.IsPinned = true;
            }
            if (priorityIds.Contains(p.Id))
            {
                p.IsPriority = true;
            }

            if (IsExcludedPackage(p.Id, p.Name))
            {
                p.IsExcluded = true;
                p.WarningNote = "⚠️ Ứng dụng nhạy cảm (Office/Zalo) - Khuyên dùng cập nhật nội bộ của app!";
            }

            // Fix empty or <unknown> current versions
            if (string.IsNullOrWhiteSpace(p.CurrentVersion) ||
                p.CurrentVersion.Equals("<unknown>", StringComparison.OrdinalIgnoreCase))
            {
                if (p.Id.Equals(WhatsAppStoreId, StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase))
                {
                    string? ver = await GetInstalledAppxVersionAsync("*WhatsApp*", cancellationToken).ConfigureAwait(false);
                    p.CurrentVersion = !string.IsNullOrWhiteSpace(ver) ? ver : "Đã cài đặt (Store)";
                }
                else
                {
                    p.CurrentVersion = "Chưa rõ (Store)";
                }
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

        // Special handling for WhatsApp: Bypass Store account requirement and route through fast update
        if (packageId.Equals(WhatsAppStoreId, StringComparison.OrdinalIgnoreCase) ||
            packageId.Equals(WhatsAppWin32Id, StringComparison.OrdinalIgnoreCase))
        {
            _logService.LogInfo("Phát hiện yêu cầu cập nhật WhatsApp: Kích hoạt quy trình cập nhật tối ưu không yêu cầu tài khoản...");
            return await FastUpdateWhatsAppAsync(null, cancellationToken).ConfigureAwait(false);
        }

        _logService.LogInfo($"Đang cập nhật ứng dụng [{packageId}]...");

        var args = new[]
        {
            "upgrade",
            "--id", packageId,
            "--exact",
            "--silent",
            "--disable-interactivity",
            "--accept-package-agreements",
            "--accept-source-agreements"
        };

        var result = await _processRunner.RunAsync("winget", args, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode == unchecked((int)0x80190194))
        {
            _logService.LogWarning($"Gói [{packageId}] trả về mã lỗi HTTP 404 (Không tìm thấy tệp cài trên server). " +
                                  "Nếu là gói cũ như Microsoft Teams cổ điển, Microsoft đã ngừng phát hành link tải này. Vui lòng chuyển sang bản 'Microsoft Teams (Work or School)'.");
        }

        LogProcessResult(result, $"Cập nhật ứng dụng [{packageId}]");
        return result;
    }

    public bool IsExcludedPackage(string packageId, string? packageName = null)
    {
        var excludedIds = _configService.CurrentConfig.ExcludedAppIds;
        if (excludedIds != null)
        {
            foreach (var exc in excludedIds)
            {
                if (string.Equals(packageId, exc, StringComparison.OrdinalIgnoreCase)) return true;
                if (packageId.StartsWith(exc + ".", StringComparison.OrdinalIgnoreCase)) return true;
            }
        }

        if (packageId.StartsWith("Microsoft.Office", StringComparison.OrdinalIgnoreCase)) return true;
        if (packageId.StartsWith("Microsoft.365", StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(packageName))
        {
            if (packageName.Contains("Microsoft Office", StringComparison.OrdinalIgnoreCase)) return true;
            if (packageName.Contains("Zalo", StringComparison.OrdinalIgnoreCase) && packageId.Contains("Zalo", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    public async Task<ProcessResult> UpgradeAllPackagesAsync(CancellationToken cancellationToken = default)
    {
        _logService.LogInfo("Đang bảo vệ các ứng dụng nhạy cảm (Office, Zalo)...");

        // Pin all excluded packages so that winget upgrade --all will skip them
        var excludedIds = _configService.CurrentConfig.ExcludedAppIds ?? new List<string>();
        foreach (var exc in excludedIds)
        {
            try
            {
                await _processRunner.RunAsync("winget", new[] { "pin", "add", "--id", exc, "--exact" }, cancellationToken).ConfigureAwait(false);
            }
            catch { }
        }

        _logService.LogInfo("Đang tiến hành cập nhật TẤT CẢ các ứng dụng (đã loại trừ và bảo vệ Office, Zalo)...");

        var args = new[]
        {
            "upgrade",
            "--all",
            "--silent",
            "--disable-interactivity",
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
            "--silent",
            "--disable-interactivity"
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
            "--disable-interactivity",
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

        // 1. Check Store package via direct Appx query (Most reliable for Windows Store MSIX apps)
        string? appxVersion = await GetInstalledAppxVersionAsync("*WhatsApp*", cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(appxVersion))
        {
            status.IsInstalled = true;
            status.PackageId = WhatsAppStoreId;
            status.PackageType = "Store";
            status.CurrentVersion = appxVersion;
            return status;
        }

        // 2. Check Win32 package via Registry
        string? win32Version = GetInstalledWin32AppVersion("WhatsApp");
        if (!string.IsNullOrWhiteSpace(win32Version))
        {
            status.IsInstalled = true;
            status.PackageId = WhatsAppWin32Id;
            status.PackageType = "Win32";
            status.CurrentVersion = win32Version;
            return status;
        }

        // 3. Fallback to winget list query
        try
        {
            var apps = await GetInstalledAppsAsync(WhatsAppStoreId, cancellationToken).ConfigureAwait(false);
            var storePkg = apps.FirstOrDefault(p => string.Equals(p.Id, WhatsAppStoreId, StringComparison.OrdinalIgnoreCase));
            if (storePkg != null)
            {
                status.IsInstalled = true;
                status.PackageId = WhatsAppStoreId;
                status.PackageType = "Store";
                status.CurrentVersion = CleanUnknownVersion(storePkg.CurrentVersion);
                return status;
            }
        }
        catch { }

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
                status.CurrentVersion = CleanUnknownVersion(win32Pkg.CurrentVersion);
                return status;
            }
        }
        catch { }

        return status;
    }

    public async Task<ProcessResult> FastUpdateWhatsAppAsync(string? customPackagePath = null, CancellationToken cancellationToken = default)
    {
        string? installerPath = customPackagePath;

        // If a custom installer was supplied, copy it to the local cache and sync to LAN share
        if (!string.IsNullOrWhiteSpace(installerPath) && File.Exists(installerPath))
        {
            try
            {
                string cacheDir = _configService.GetCacheDirectory();
                Directory.CreateDirectory(cacheDir);
                string destFile = Path.Combine(cacheDir, Path.GetFileName(installerPath));
                if (!string.Equals(Path.GetFullPath(installerPath), Path.GetFullPath(destFile), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(installerPath, destFile, overwrite: true);
                    installerPath = destFile;
                    _logService.LogInfo($"Đã lưu gói cài vào bộ nhớ đệm: {destFile}");
                }
                CopyCachedPackageToLan(cacheDir);
            }
            catch (Exception ex)
            {
                _logService.LogWarning("Không thể lưu gói vào bộ nhớ đệm: " + ex.Message);
            }
        }

        // 1. Check if an offline installer was supplied or is present in Cache/LAN
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            installerPath = FindCachedWhatsAppInstaller();
        }

        // 2. If package found in Cache or LAN -> Install directly offline in 3-5 seconds!
        if (!string.IsNullOrWhiteSpace(installerPath) && File.Exists(installerPath))
        {
            _logService.LogInfo($"Tìm thấy gói cài đặt offline: {Path.GetFileName(installerPath)}. Bắt đầu cập nhật siêu tốc (3-5 giây)...");

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

        // 3. If NO offline package in Cache or LAN:
        // Perform fast direct update via winget install (with temporary wuauserv unblock if blocked by WUB)
        _logService.LogInfo("Chưa có sẵn gói offline trong bộ nhớ đệm. Đang tiến hành cập nhật WhatsApp trực tiếp từ Store...");

        bool wasDisabled = IsWuauservDisabled();
        if (wasDisabled)
        {
            _logService.LogWarning("Phát hiện Windows Update (wuauserv) đang bị khóa bởi WUB. Tạm thời mở khóa để cập nhật WhatsApp...");
            await SetWuauservStartModeAsync(3, cancellationToken).ConfigureAwait(false); // 3 = Demand/Manual
        }

        try
        {
            var args = new[]
            {
                "install",
                "--id", WhatsAppStoreId,
                "--source", "msstore",
                "--accept-package-agreements",
                "--accept-source-agreements",
                "--disable-interactivity",
                "--silent"
            };

            var result = await _processRunner.RunAsync("winget", args, cancellationToken).ConfigureAwait(false);
            LogProcessResult(result, "Cập nhật trực tiếp WhatsApp từ Store");
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

        _logService.LogInfo($"Bắt đầu tải gói cài đặt WhatsApp vào thư mục đệm: {cacheDir}...");

        bool wasDisabled = IsWuauservDisabled();
        if (wasDisabled)
        {
            _logService.LogWarning("Phát hiện dịch vụ Windows Update (wuauserv) đang bị khóa bởi WUB. Tạm thời kích hoạt để tải gói Store...");
            await SetWuauservStartModeAsync(3, cancellationToken).ConfigureAwait(false); // 3 = Demand/Manual
        }

        try
        {
            var args = new[]
            {
                "download",
                "--id", WhatsAppStoreId,
                "--source", "msstore",
                "--download-directory", cacheDir,
                "--accept-package-agreements",
                "--accept-source-agreements",
                "--disable-interactivity"
            };

            var result = await _processRunner.RunAsync("winget", args, cancellationToken).ConfigureAwait(false);

            if (result.Succeeded)
            {
                _logService.LogSuccess("Đã tải xong gói cài đặt WhatsApp vào bộ nhớ đệm!");
                CopyCachedPackageToLan(cacheDir);
            }
            else
            {
                string desc = WingetExitCode.GetDescription(result.ExitCode);
                _logService.LogWarning($"Lệnh winget download trả về mã lỗi {result.ExitCode}: {desc}");
                _logService.LogInfo("💡 Hướng dẫn cập nhật offline tối ưu:");
                _logService.LogInfo("  1. Bấm '⚡ Cập nhật siêu tốc' để ứng dụng tự động cài đặt phiên bản mới nhất từ Store mà KHÔNG cần đăng nhập tài khoản.");
                _logService.LogInfo("  2. Hoặc tải file gói WhatsApp (.msixbundle) một lần, bấm '📁 Cài từ tệp Offline...' hoặc lưu vào thư mục chia sẻ LAN để phân phối tức thì cho toàn bộ máy tính.");
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

    public async Task<string?> GetInstalledAppxVersionAsync(string packageNamePattern, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            string safePattern = packageNamePattern.Replace("'", "''");
            var args = new[]
            {
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy", "Bypass",
                "-Command", $"(Get-AppxPackage -Name '{safePattern}' | Select-Object -ExpandProperty Version -First 1)"
            };

            var result = await _processRunner.RunAsync("powershell.exe", args, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                string ver = result.StandardOutput.Trim();
                if (!string.IsNullOrWhiteSpace(ver) && !ver.Contains("Error", StringComparison.OrdinalIgnoreCase))
                {
                    return ver;
                }
            }
        }
        catch { }

        return null;
    }

    private static string? GetInstalledWin32AppVersion(string appName)
    {
        if (!OperatingSystem.IsWindows()) return null;

        string[] registryRoots =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (var root in registryRoots)
        {
            try
            {
                using var hklmKey = Registry.LocalMachine.OpenSubKey(root);
                if (hklmKey != null)
                {
                    foreach (var subkeyName in hklmKey.GetSubKeyNames())
                    {
                        using var subkey = hklmKey.OpenSubKey(subkeyName);
                        var dispName = subkey?.GetValue("DisplayName")?.ToString();
                        if (dispName != null && dispName.Contains(appName, StringComparison.OrdinalIgnoreCase))
                        {
                            var ver = subkey?.GetValue("DisplayVersion")?.ToString();
                            if (!string.IsNullOrWhiteSpace(ver)) return ver;
                        }
                    }
                }

                using var hkcuKey = Registry.CurrentUser.OpenSubKey(root);
                if (hkcuKey != null)
                {
                    foreach (var subkeyName in hkcuKey.GetSubKeyNames())
                    {
                        using var subkey = hkcuKey.OpenSubKey(subkeyName);
                        var dispName = subkey?.GetValue("DisplayName")?.ToString();
                        if (dispName != null && dispName.Contains(appName, StringComparison.OrdinalIgnoreCase))
                        {
                            var ver = subkey?.GetValue("DisplayVersion")?.ToString();
                            if (!string.IsNullOrWhiteSpace(ver)) return ver;
                        }
                    }
                }
            }
            catch { }
        }

        return null;
    }

    private static string CleanUnknownVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Equals("<unknown>", StringComparison.OrdinalIgnoreCase))
        {
            return "Chưa rõ";
        }
        return version.Trim();
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
            // Phá khóa WUB / phân quyền Administrator trên khóa Registry để tránh lỗi 'Access is denied'
            WindowsSecurityHelper.UnlockServiceRegistryKey("wuauserv");

            if (startMode == 3)
            {
                // Khôi phục quyền SDDL chuẩn để Service Control Manager cho phép thao tác
                string defaultSddl = "D:(A;;CCLCSWRPLORC;;;AU)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;SY)S:(AU;FA;CCDCLCSWRPWPDTLOSDRCWDWO;;;WD)";
                await _processRunner.RunAsync("sc.exe", new[] { "sdset", "wuauserv", defaultSddl }, ct).ConfigureAwait(false);
            }

            string mode = startMode == 4 ? "disabled" : "demand";
            await _processRunner.RunAsync("sc.exe", new[] { "config", "wuauserv", $"start={mode}" }, ct).ConfigureAwait(false);

            // Ghi trực tiếp Start vào Registry làm phương án dự phòng chắc chắn
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv", writable: true);
                key?.SetValue("Start", startMode, RegistryValueKind.DWord);
            }
            catch { }

            if (startMode == 4)
            {
                await _processRunner.RunAsync("net.exe", new[] { "stop", "wuauserv" }, ct).ConfigureAwait(false);
            }
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
