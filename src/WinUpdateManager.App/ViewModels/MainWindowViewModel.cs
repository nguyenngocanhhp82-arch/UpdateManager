using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinUpdateManager.Core.Models;
using WinUpdateManager.Core.Services;

namespace WinUpdateManager.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ILogService _logService;
    private readonly IProcessRunner _processRunner;
    private readonly IWingetService _wingetService;
    private readonly IConfigService _configService;
    private readonly IWindowsUpdateService _windowsUpdateService;
    private readonly IHotfixService _hotfixService;
    private readonly IPolicyService _policyService;
    private readonly IScheduleService _scheduleService;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Sẵn sàng";

    [ObservableProperty]
    private LogEntry? _selectedLogEntry;

    // --- Tab 1: Cập nhật ứng dụng ---
    [ObservableProperty]
    private bool _isWingetAvailable = true;

    [ObservableProperty]
    private bool _includeUnknown;

    [ObservableProperty]
    private string _searchUpgradeFilter = string.Empty;

    [ObservableProperty]
    private WhatsAppStatus? _whatsAppStatus;

    [ObservableProperty]
    private string _whatsAppInfoText = "Đang kiểm tra...";

    [ObservableProperty]
    private string _whatsAppCachedBadgeText = string.Empty;

    // --- Tab 5: Tùy chỉnh cập nhật & Lịch tự động ---
    [ObservableProperty]
    private UpdatePolicyModel _policyModel = new();

    // --- Windows Update Service / WUB Status ---
    [ObservableProperty]
    private WindowsUpdateStatusInfo _wuStatus = new();

    [ObservableProperty]
    private bool _isProtectServiceSettings = true;

    [ObservableProperty]
    private string _windowsUpdateBadgeText = "Windows Update: ĐÃ KHÓA (Disabled)";

    [ObservableProperty]
    private string _windowsUpdateBadgeIcon = "🛡️";

    [ObservableProperty]
    private string _windowsUpdateBadgeBackground = "#FEF2F2";

    [ObservableProperty]
    private string _windowsUpdateBadgeForeground = "#DC2626";

    [ObservableProperty]
    private string _windowsUpdateBadgeBorder = "#FCA5A5";

    [ObservableProperty]
    private ScheduleTaskModel _scheduleModel = new();

    [ObservableProperty]
    private string _scheduleLogText = string.Empty;

    [ObservableProperty]
    private bool _isScheduleLogModalOpen;

    [ObservableProperty]
    private string _scheduledAppsSummaryText = "Chưa tải danh sách ứng dụng.";

    public ObservableCollection<WingetPackage> ScheduledApps { get; } = new();

    public ObservableCollection<WingetPackage> UpgradableApps { get; } = new();
    private readonly List<WingetPackage> _allUpgradableApps = new();

    // --- Tab 2: Windows Update ---
    [ObservableProperty]
    private bool _showHiddenWindowsUpdates;

    [ObservableProperty]
    private bool _rebootRequired;

    [ObservableProperty]
    private string _windowsUpdateSummary = "Chưa quét bản cập nhật Windows.";

    public ObservableCollection<WindowsUpdateItem> WindowsUpdates { get; } = new();

    // --- Tab 3: Gỡ bản cập nhật Windows ---
    [ObservableProperty]
    private string _searchHotfixFilter = string.Empty;

    [ObservableProperty]
    private bool _autoCreateRestorePoint = true;

    [ObservableProperty]
    private bool _autoBlockReinstall = true;

    [ObservableProperty]
    private bool _isDismMode;

    public ObservableCollection<InstalledHotfix> InstalledHotfixes { get; } = new();
    private readonly List<InstalledHotfix> _allInstalledHotfixes = new();

    public ObservableCollection<DismPackage> DismPackages { get; } = new();

    // --- Tab 4: Ứng dụng đã cài / Rollback ---
    [ObservableProperty]
    private string _searchInstalledFilter = string.Empty;

    [ObservableProperty]
    private WingetPackage? _selectedInstalledApp;

    [ObservableProperty]
    private bool _isRollbackPanelOpen;

    [ObservableProperty]
    private string? _selectedRollbackVersion;

    [ObservableProperty]
    private bool _pinAfterRollback = true;

    public ObservableCollection<WingetPackage> InstalledApps { get; } = new();
    public ObservableCollection<string> AvailableVersions { get; } = new();
    private readonly List<WingetPackage> _allInstalledApps = new();

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public MainWindowViewModel(
        ILogService logService,
        IProcessRunner processRunner,
        IWingetService wingetService,
        IConfigService configService,
        IWindowsUpdateService windowsUpdateService,
        IHotfixService hotfixService,
        IPolicyService policyService,
        IScheduleService scheduleService)
    {
        _logService = logService;
        _processRunner = processRunner;
        _wingetService = wingetService;
        _configService = configService;
        _windowsUpdateService = windowsUpdateService;
        _hotfixService = hotfixService;
        _policyService = policyService;
        _scheduleService = scheduleService;

        // Initialize with existing log entries
        foreach (var entry in _logService.GetRecentEntries())
        {
            LogEntries.Add(entry);
        }

        _logService.EntryAdded += OnLogEntryAdded;
        _logService.Cleared += OnLogCleared;

        _logService.LogInfo("Lilbow Manager Update đã khởi động thành công. Phát triển bởi Lilbow Nguyễn.");

        // Initial background initialization
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await _configService.LoadConfigAsync().ConfigureAwait(false);

        bool hasWinget = await _wingetService.IsWingetInstalledAsync().ConfigureAwait(false);
        ExecuteOnUIThread(() => IsWingetAvailable = hasWinget);

        if (!hasWinget)
        {
            _logService.LogWarning("Không tìm thấy công cụ winget trên máy tính này. Vui lòng cài đặt 'App Installer' từ Microsoft Store hoặc GitHub.");
        }

        await RefreshWhatsAppStatusAsync().ConfigureAwait(false);
        await LoadPoliciesAsync().ConfigureAwait(false);
        RefreshWindowsUpdateStatus();
        await LoadScheduleStatusAsync().ConfigureAwait(false);
    }

    private void OnLogEntryAdded(LogEntry entry)
    {
        ExecuteOnUIThread(() =>
        {
            LogEntries.Add(entry);
            if (LogEntries.Count > 1000)
            {
                LogEntries.RemoveAt(0);
            }
        });
    }

    private void OnLogCleared()
    {
        ExecuteOnUIThread(() => LogEntries.Clear());
    }

    private void ExecuteOnUIThread(Action action)
    {
#if WINDOWS
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(action);
            return;
        }
#endif
        action();
    }

    private bool ConfirmAction(string title, string message)
    {
#if WINDOWS
        return System.Windows.MessageBox.Show(
            message,
            title,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
#else
        return true;
#endif
    }

    // ==========================================
    // TAB 1: WHATSAPP & APP UPDATES
    // ==========================================

    [RelayCommand]
    public async Task RefreshWhatsAppStatusAsync()
    {
        try
        {
            var status = await _wingetService.CheckWhatsAppStatusAsync().ConfigureAwait(false);
            ExecuteOnUIThread(() =>
            {
                WhatsAppStatus = status;
                if (!status.IsInstalled)
                {
                    WhatsAppInfoText = "Chưa cài đặt WhatsApp trên máy tính này.";
                }
                else
                {
                    WhatsAppInfoText = $"Phiên bản hiện tại: {status.CurrentVersion} ({status.PackageType})";
                }

                if (!string.IsNullOrWhiteSpace(status.CachedInstallerPath))
                {
                    WhatsAppCachedBadgeText = $"Đã có sẵn gói offline: {Path.GetFileName(status.CachedInstallerPath)}";
                }
                else
                {
                    WhatsAppCachedBadgeText = "Chưa có file gói trong bộ nhớ đệm (sẽ tự động tải khi bấm Cập nhật)";
                }
            });
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi kiểm tra trạng thái WhatsApp: " + ex.Message);
        }
    }

    [RelayCommand]
    public async Task FastUpdateWhatsAppAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Đang thực hiện cập nhật siêu tốc WhatsApp...";

        try
        {
            var result = await _wingetService.FastUpdateWhatsAppAsync().ConfigureAwait(false);
            if (result.Succeeded)
            {
                _logService.LogSuccess("WhatsApp đã được cập nhật thành công!");
                await RefreshWhatsAppStatusAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logService.LogError("Cập nhật WhatsApp siêu tốc thất bại: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task UpdateWhatsAppFromFileAsync()
    {
#if WINDOWS
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn file gói cài đặt WhatsApp (.msixbundle hoặc .exe)",
            Filter = "Gói ứng dụng Windows (*.msixbundle;*.msix;*.appxbundle;*.exe)|*.msixbundle;*.msix;*.appxbundle;*.exe|Tất cả tệp (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusText = $"Đang cài đặt từ tệp: {Path.GetFileName(dialog.FileName)}...";

            try
            {
                var result = await _wingetService.FastUpdateWhatsAppAsync(dialog.FileName).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    _logService.LogSuccess($"Cài đặt thành công WhatsApp từ tệp: {dialog.FileName}");
                    await RefreshWhatsAppStatusAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logService.LogError("Cài đặt WhatsApp từ tệp thất bại: " + ex.Message, ex);
            }
            finally
            {
                IsBusy = false;
                StatusText = "Sẵn sàng";
            }
        }
#else
        _logService.LogInfo("Chức năng chọn tệp chỉ khả dụng trên Windows.");
        await Task.CompletedTask;
#endif
    }

    [RelayCommand]
    public async Task DownloadWhatsAppPackageAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Đang tải gói WhatsApp mới nhất về máy...";

        try
        {
            var result = await _wingetService.DownloadWhatsAppPackageAsync().ConfigureAwait(false);
            if (result.Succeeded)
            {
                await RefreshWhatsAppStatusAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi tải gói WhatsApp: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task ScanUpgradesAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Đang quét danh sách bản cập nhật ứng dụng...";

        try
        {
            var packages = await _wingetService.GetUpgradesAsync(IncludeUnknown).ConfigureAwait(false);
            ExecuteOnUIThread(() =>
            {
                _allUpgradableApps.Clear();
                _allUpgradableApps.AddRange(packages);
                ApplyUpgradeFilter();
            });
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi quét cập nhật ứng dụng: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    partial void OnSearchUpgradeFilterChanged(string value) => ApplyUpgradeFilter();

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 4 && ScheduledApps.Count == 0)
        {
            _ = LoadScheduledAppsAsync();
        }
    }

    private void ApplyUpgradeFilter()
    {
        UpgradableApps.Clear();
        var filtered = string.IsNullOrWhiteSpace(SearchUpgradeFilter)
            ? _allUpgradableApps
            : _allUpgradableApps.Where(p =>
                p.Name.Contains(SearchUpgradeFilter, StringComparison.OrdinalIgnoreCase) ||
                p.Id.Contains(SearchUpgradeFilter, StringComparison.OrdinalIgnoreCase));

        foreach (var p in filtered)
        {
            UpgradableApps.Add(p);
        }
    }

    [RelayCommand]
    public void SelectAllUpgrades()
    {
        foreach (var p in UpgradableApps)
        {
            if (!p.IsExcluded) p.IsSelected = true;
        }
    }

    [RelayCommand]
    public void DeselectAllUpgrades()
    {
        foreach (var p in UpgradableApps) p.IsSelected = false;
    }

    [RelayCommand]
    public async Task UpgradeSingleAppAsync(WingetPackage package)
    {
        if (package == null || IsBusy) return;

        if (package.IsExcluded)
        {
            bool proceed = ConfirmAction(
                "Cảnh báo ứng dụng nhạy cảm",
                $"Ứng dụng [{package.Name}] ({package.Id}) thuộc danh mục nhạy cảm (Office/Zalo).\n\n" +
                "- Với Office: Cập nhật qua winget có thể làm hỏng bản quyền hoặc xung đột tệp Click-to-Run.\n" +
                "- Với Zalo: Ứng dụng tự động cập nhật khi mở app, nâng cấp qua winget khi app đang chạy ngầm có thể gây crash.\n\n" +
                "Bạn có chắc chắn muốn tiếp tục cập nhật ứng dụng này không?");
            if (!proceed) return;
        }

        IsBusy = true;
        StatusText = $"Đang cập nhật {package.Name}...";

        try
        {
            var result = await _wingetService.UpgradePackageAsync(package.Id).ConfigureAwait(false);
            if (result.Succeeded)
            {
                await ScanUpgradesAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logService.LogError($"Lỗi cập nhật {package.Name}: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task UpgradeSelectedAsync()
    {
        var selected = UpgradableApps.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            _logService.LogWarning("Vui lòng chọn ít nhất một ứng dụng để cập nhật.");
            return;
        }

        var excludedSelected = selected.Where(p => p.IsExcluded).ToList();
        if (excludedSelected.Count > 0)
        {
            string names = string.Join(", ", excludedSelected.Select(p => p.Name));
            bool proceed = ConfirmAction(
                "Cảnh báo ứng dụng nhạy cảm",
                $"Trong danh sách bạn chọn có các ứng dụng nhạy cảm: {names}.\n\n" +
                "Cập nhật các ứng dụng này qua winget có thể gây lỗi bản quyền (Office) hoặc crash (Zalo).\n\n" +
                "Bạn có muốn BỎ QUA các ứng dụng này và chỉ cập nhật các phần mềm an toàn khác không?\n(Bấm 'Yes' để bỏ qua và tiếp tục, 'No' để hủy thao tác)");
            if (proceed)
            {
                selected = selected.Where(p => !p.IsExcluded).ToList();
                if (selected.Count == 0)
                {
                    _logService.LogInfo("Đã bỏ qua các ứng dụng nhạy cảm, không còn ứng dụng nào khác cần cập nhật.");
                    return;
                }
            }
            else
            {
                return;
            }
        }

        if (IsBusy) return;
        IsBusy = true;

        int successCount = 0;
        int failCount = 0;

        try
        {
            for (int i = 0; i < selected.Count; i++)
            {
                var pkg = selected[i];
                StatusText = $"Đang cập nhật [{i + 1}/{selected.Count}]: {pkg.Name}...";
                _logService.LogInfo($"--- [{i + 1}/{selected.Count}] Bắt đầu cập nhật: {pkg.Name} [{pkg.Id}] ---");

                try
                {
                    var result = await _wingetService.UpgradePackageAsync(pkg.Id).ConfigureAwait(false);
                    if (result.Succeeded || result.ExitCode == 3010)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    _logService.LogError($"Lỗi cập nhật [{pkg.Name}]: {ex.Message}");
                }
            }

            if (failCount == 0)
            {
                _logService.LogSuccess($"Đã hoàn tất cập nhật tất cả {successCount} ứng dụng đã chọn.");
            }
            else
            {
                _logService.LogWarning($"Quá trình cập nhật hoàn tất: {successCount} thành công, {failCount} gặp sự cố. Xem chi tiết trong nhật ký.");
            }

            await ScanUpgradesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi trong quá trình cập nhật hàng loạt: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task UpgradeAllAsync()
    {
        SelectAllUpgrades();
        await UpgradeSelectedAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task UpgradePriorityGroupAsync()
    {
        var priorityList = UpgradableApps.Where(p => p.IsPriority).ToList();
        if (priorityList.Count == 0)
        {
            _logService.LogInfo("Không có ứng dụng nào thuộc nhóm ưu tiên (WhatsApp, Chrome) có bản cập nhật mới.");
            return;
        }

        if (IsBusy) return;
        IsBusy = true;

        int prioSuccess = 0;
        int prioFail = 0;

        try
        {
            for (int i = 0; i < priorityList.Count; i++)
            {
                var pkg = priorityList[i];
                StatusText = $"Đang cập nhật nhóm ưu tiên [{i + 1}/{priorityList.Count}]: {pkg.Name}...";
                _logService.LogInfo($"--- [{i + 1}/{priorityList.Count}] Cập nhật ưu tiên: {pkg.Name} [{pkg.Id}] ---");

                try
                {
                    var result = await _wingetService.UpgradePackageAsync(pkg.Id).ConfigureAwait(false);
                    if (result.Succeeded || result.ExitCode == 3010)
                    {
                        prioSuccess++;
                    }
                    else
                    {
                        prioFail++;
                    }
                }
                catch (Exception ex)
                {
                    prioFail++;
                    _logService.LogError($"Lỗi cập nhật [{pkg.Name}]: {ex.Message}");
                }
            }

            if (prioFail == 0)
            {
                _logService.LogSuccess($"Hoàn thành cập nhật {prioSuccess} ứng dụng ưu tiên.");
            }
            else
            {
                _logService.LogWarning($"Cập nhật nhóm ưu tiên hoàn tất: {prioSuccess} thành công, {prioFail} gặp sự cố.");
            }

            await ScanUpgradesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi cập nhật nhóm ưu tiên: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task TogglePinAsync(WingetPackage package)
    {
        if (package == null || IsBusy) return;
        IsBusy = true;

        try
        {
            if (package.IsPinned)
            {
                await _wingetService.UnpinPackageAsync(package.Id).ConfigureAwait(false);
                package.IsPinned = false;
            }
            else
            {
                await _wingetService.PinPackageAsync(package.Id).ConfigureAwait(false);
                package.IsPinned = true;
            }
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi thay đổi trạng thái ghim: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ==========================================
    // TAB 2: WINDOWS UPDATE (COM API)
    // ==========================================

    [RelayCommand]
    public async Task ScanWindowsUpdatesAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Đang quét các bản cập nhật Windows từ Microsoft.Update.Session...";

        try
        {
            var updates = await _windowsUpdateService.SearchUpdatesAsync(ShowHiddenWindowsUpdates).ConfigureAwait(false);
            ExecuteOnUIThread(() =>
            {
                WindowsUpdates.Clear();
                long totalBytes = 0;
                foreach (var u in updates)
                {
                    WindowsUpdates.Add(u);
                    totalBytes += u.SizeBytes;
                }

                string stateText = ShowHiddenWindowsUpdates ? "đã ẩn" : "chờ cài đặt";
                WindowsUpdateSummary = WindowsUpdates.Count > 0
                    ? $"Có {WindowsUpdates.Count} bản cập nhật Windows {stateText} (Tổng dung lượng: {WindowsUpdateItem.FormatSize(totalBytes)})."
                    : $"Hệ điều hành đã cập nhật bản mới nhất (không có bản cập nhật {stateText}).";
            });
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi quét Windows Update: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task InstallSelectedWindowsUpdatesAsync()
    {
        var selected = WindowsUpdates.Where(u => u.IsSelected).ToList();
        if (selected.Count == 0)
        {
            _logService.LogWarning("Vui lòng tick chọn ít nhất một bản cập nhật Windows để cài đặt.");
            return;
        }

        if (IsBusy) return;
        IsBusy = true;
        StatusText = $"Đang tải và cài đặt {selected.Count} bản cập nhật Windows...";

        try
        {
            var result = await _windowsUpdateService.InstallUpdatesAsync(selected).ConfigureAwait(false);
            if (result.RebootRequired)
            {
                RebootRequired = true;
            }
            await ScanWindowsUpdatesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi cài đặt Windows Update: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task InstallAllWindowsUpdatesAsync()
    {
        if (WindowsUpdates.Count == 0)
        {
            _logService.LogInfo("Không có bản cập nhật Windows nào để cài đặt.");
            return;
        }

        if (!ConfirmAction("Xác nhận cài đặt Windows Update", $"Bạn có chắc chắn muốn tải và cài đặt toàn bộ {WindowsUpdates.Count} bản cập nhật Windows?"))
        {
            return;
        }

        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Đang cài đặt toàn bộ bản cập nhật Windows...";

        try
        {
            var result = await _windowsUpdateService.InstallUpdatesAsync(WindowsUpdates).ConfigureAwait(false);
            if (result.RebootRequired)
            {
                RebootRequired = true;
            }
            await ScanWindowsUpdatesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi cài đặt Windows Update: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task ToggleHideWindowsUpdateAsync(WindowsUpdateItem update)
    {
        if (update == null || IsBusy) return;
        IsBusy = true;

        try
        {
            bool newHidden = !update.IsHidden;
            bool ok = await _windowsUpdateService.SetUpdateHiddenAsync(update, newHidden).ConfigureAwait(false);
            if (ok)
            {
                await ScanWindowsUpdatesAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi thay đổi trạng thái ẩn bản cập nhật: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RestartComputerAsync()
    {
        if (ConfirmAction("Khởi động lại máy tính", "Bạn có chắc chắn muốn khởi động lại máy tính ngay bây giờ để hoàn tất quá trình cập nhật?"))
        {
            await _windowsUpdateService.RestartComputerAsync().ConfigureAwait(false);
        }
    }

    // ==========================================
    // TAB 3: GỠ BẢN CẬP NHẬT WINDOWS (WMI / WUSA / DISM)
    // ==========================================

    [RelayCommand]
    public async Task LoadInstalledHotfixesAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Đang truy vấn danh sách bản vá KB đã cài đặt từ WMI...";

        try
        {
            var hotfixes = await _hotfixService.GetInstalledHotfixesAsync().ConfigureAwait(false);
            ExecuteOnUIThread(() =>
            {
                _allInstalledHotfixes.Clear();
                _allInstalledHotfixes.AddRange(hotfixes);
                ApplyHotfixFilter();
            });
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi tải danh sách bản vá KB: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    partial void OnSearchHotfixFilterChanged(string value) => ApplyHotfixFilter();

    private void ApplyHotfixFilter()
    {
        InstalledHotfixes.Clear();
        var filtered = string.IsNullOrWhiteSpace(SearchHotfixFilter)
            ? _allInstalledHotfixes
            : _allInstalledHotfixes.Where(h =>
                h.HotFixId.Contains(SearchHotfixFilter, StringComparison.OrdinalIgnoreCase) ||
                h.Description.Contains(SearchHotfixFilter, StringComparison.OrdinalIgnoreCase));

        foreach (var h in filtered)
        {
            InstalledHotfixes.Add(h);
        }
    }

    [RelayCommand]
    public async Task UninstallHotfixAsync(InstalledHotfix hotfix)
    {
        if (hotfix == null) return;

        bool confirm = ConfirmAction(
            "Xác nhận gỡ bản vá KB",
            $"Bạn có chắc chắn muốn gỡ bản cập nhật {hotfix.HotFixId} ({hotfix.Description})?\n\n" +
            (AutoCreateRestorePoint ? "✓ Sẽ tự động tạo Điểm phục hồi hệ thống trước khi gỡ.\n" : "") +
            (AutoBlockReinstall ? "✓ Sẽ tự động Ẩn KB này để ngăn Windows tự cài lại." : ""));

        if (!confirm) return;

        if (IsBusy) return;
        IsBusy = true;
        StatusText = $"Đang gỡ bỏ bản cập nhật {hotfix.HotFixId}...";

        try
        {
            var result = await _hotfixService.UninstallHotfixAsync(
                hotfix.NumericKb,
                AutoCreateRestorePoint,
                AutoBlockReinstall).ConfigureAwait(false);

            if (result.ExitCode == 0 || result.ExitCode == 3010)
            {
                if (result.ExitCode == 3010)
                {
                    RebootRequired = true;
                }
                await LoadInstalledHotfixesAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logService.LogError($"Lỗi gỡ bỏ {hotfix.HotFixId}: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task LoadDismPackagesAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Đang quét danh sách các gói hệ thống qua DISM...";

        try
        {
            var packages = await _hotfixService.GetDismPackagesAsync().ConfigureAwait(false);
            ExecuteOnUIThread(() =>
            {
                DismPackages.Clear();
                foreach (var p in packages)
                {
                    DismPackages.Add(p);
                }
            });
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi quét gói DISM: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task RemoveDismPackageAsync(DismPackage package)
    {
        if (package == null) return;

        bool confirm = ConfirmAction(
            "CẢNH BÁO: Gỡ gói hệ thống DISM",
            $"Gỡ gói hệ thống qua DISM có thể ảnh hưởng đến tính ổn định của Windows.\n\n" +
            $"Bạn có chắc chắn muốn gỡ bỏ gói sau:\n{package.PackageName}?");

        if (!confirm) return;

        if (IsBusy) return;
        IsBusy = true;
        StatusText = $"Đang gỡ gói DISM: {package.PackageName}...";

        try
        {
            var result = await _hotfixService.RemoveDismPackageAsync(package.PackageName).ConfigureAwait(false);
            if (result.Succeeded)
            {
                await LoadDismPackagesAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi gỡ gói DISM: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task CreateRestorePointAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Đang tạo điểm phục hồi hệ thống...";

        try
        {
            await _hotfixService.CreateRestorePointAsync("WinUpdateManager Điểm phục hồi thủ công").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi tạo Restore Point: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task OpenInstalledUpdatesControlPanelAsync()
    {
        try
        {
            await _hotfixService.OpenInstalledUpdatesControlPanelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logService.LogError("Không thể mở cửa sổ Control Panel của Windows: " + ex.Message);
        }
    }

    // ==========================================
    // TAB 4: ỨNG DỤNG ĐÃ CÀI / ROLLBACK
    // ==========================================

    [RelayCommand]
    public async Task LoadInstalledAppsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Đang tải danh sách toàn bộ ứng dụng đã cài đặt...";

        try
        {
            var apps = await _wingetService.GetInstalledAppsAsync().ConfigureAwait(false);
            ExecuteOnUIThread(() =>
            {
                _allInstalledApps.Clear();
                _allInstalledApps.AddRange(apps);
                ApplyInstalledFilter();
            });
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi tải ứng dụng đã cài đặt: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    partial void OnSearchInstalledFilterChanged(string value) => ApplyInstalledFilter();

    private void ApplyInstalledFilter()
    {
        InstalledApps.Clear();
        var filtered = string.IsNullOrWhiteSpace(SearchInstalledFilter)
            ? _allInstalledApps
            : _allInstalledApps.Where(p =>
                p.Name.Contains(SearchInstalledFilter, StringComparison.OrdinalIgnoreCase) ||
                p.Id.Contains(SearchInstalledFilter, StringComparison.OrdinalIgnoreCase));

        foreach (var p in filtered)
        {
            InstalledApps.Add(p);
        }
    }

    [RelayCommand]
    public async Task UninstallAppAsync(WingetPackage package)
    {
        if (package == null) return;

        bool confirm = ConfirmAction(
            "Xác nhận gỡ cài đặt",
            $"Bạn có chắc chắn muốn gỡ cài đặt ứng dụng: {package.Name} [{package.Id}]?");

        if (!confirm) return;

        if (IsBusy) return;
        IsBusy = true;
        StatusText = $"Đang gỡ cài đặt {package.Name}...";

        try
        {
            var result = await _wingetService.UninstallPackageAsync(package.Id).ConfigureAwait(false);
            if (result.Succeeded)
            {
                await LoadInstalledAppsAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logService.LogError($"Lỗi khi gỡ {package.Name}: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task OpenRollbackDialogAsync(WingetPackage package)
    {
        if (package == null) return;
        SelectedInstalledApp = package;
        AvailableVersions.Clear();
        SelectedRollbackVersion = null;
        IsRollbackPanelOpen = true;

        if (string.Equals(package.Source, "msstore", StringComparison.OrdinalIgnoreCase))
        {
            _logService.LogWarning("Lưu ý: Ứng dụng từ Microsoft Store thường không cho phép chọn phiên bản cũ qua winget.");
        }

        StatusText = $"Đang tra cứu các phiên bản có sẵn của {package.Name}...";
        try
        {
            var versions = await _wingetService.GetAvailableVersionsAsync(package.Id).ConfigureAwait(false);
            ExecuteOnUIThread(() =>
            {
                foreach (var v in versions)
                {
                    AvailableVersions.Add(v);
                }
                if (AvailableVersions.Count > 0)
                {
                    SelectedRollbackVersion = AvailableVersions[0];
                }
            });
        }
        catch (Exception ex)
        {
            _logService.LogError("Không thể lấy danh sách phiên bản: " + ex.Message);
        }
        finally
        {
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public void CloseRollbackPanel()
    {
        IsRollbackPanelOpen = false;
        SelectedRollbackVersion = null;
    }

    [RelayCommand]
    public async Task ExecuteRollbackAsync()
    {
        if (SelectedInstalledApp == null || string.IsNullOrWhiteSpace(SelectedRollbackVersion))
        {
            _logService.LogWarning("Vui lòng chọn một phiên bản để rollback.");
            return;
        }

        bool confirm = ConfirmAction(
            "Xác nhận Rollback phiên bản",
            $"Bạn có chắc chắn muốn cài đặt phiên bản {SelectedRollbackVersion} cho {SelectedInstalledApp.Name}?");

        if (!confirm) return;

        if (IsBusy) return;
        IsBusy = true;
        StatusText = $"Đang cài đặt phiên bản {SelectedRollbackVersion} cho {SelectedInstalledApp.Name}...";

        try
        {
            var result = await _wingetService.InstallVersionAsync(SelectedInstalledApp.Id, SelectedRollbackVersion, force: true).ConfigureAwait(false);
            if (result.Succeeded)
            {
                if (PinAfterRollback)
                {
                    await _wingetService.PinPackageAsync(SelectedInstalledApp.Id).ConfigureAwait(false);
                }
                IsRollbackPanelOpen = false;
                await LoadInstalledAppsAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi rollback: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    // ==========================================
    // TAB 5: TÙY CHỈNH CẬP NHẬT & LỊCH TỰ ĐỘNG
    // ==========================================

    [RelayCommand]
    public async Task LoadPoliciesAsync()
    {
        try
        {
            var policies = await Task.Run(() => _policyService.GetCurrentPolicies());
            ExecuteOnUIThread(() =>
            {
                PolicyModel = policies;
                RefreshWindowsUpdateStatus();
            });
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi nạp chính sách Registry: " + ex.Message);
        }
    }

    [RelayCommand]
    public async Task ApplyPoliciesAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Đang lưu cấu hình chính sách cập nhật vào Windows Registry...";

        try
        {
            await Task.Run(() => _policyService.ApplyPolicies(PolicyModel));
            await LoadPoliciesAsync();
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi áp dụng chính sách: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task ResetPoliciesAsync()
    {
        if (!ConfirmAction(
            "Khôi phục mặc định",
            "Bạn có chắc chắn muốn xóa toàn bộ các chính sách đã can thiệp trong Registry về mặc định của Windows?"))
        {
            return;
        }

        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Đang khôi phục chính sách mặc định...";

        try
        {
            await Task.Run(() => _policyService.ResetToDefaults());
            await LoadPoliciesAsync();
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi khôi phục mặc định: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public void RefreshWindowsUpdateStatus()
    {
        try
        {
            var status = _policyService.GetWindowsUpdateStatus();
            ExecuteOnUIThread(() =>
            {
                WuStatus = status;
                if (status.IsDisabled)
                {
                    WindowsUpdateBadgeText = "Windows Update: ĐÃ KHÓA (Disabled)";
                    WindowsUpdateBadgeIcon = "🛡️";
                    WindowsUpdateBadgeBackground = "#FEF2F2";
                    WindowsUpdateBadgeForeground = "#DC2626";
                    WindowsUpdateBadgeBorder = "#FCA5A5";
                }
                else
                {
                    WindowsUpdateBadgeText = "Windows Update: ĐANG BẬT (Enabled)";
                    WindowsUpdateBadgeIcon = "🟢";
                    WindowsUpdateBadgeBackground = "#ECFDF5";
                    WindowsUpdateBadgeForeground = "#059669";
                    WindowsUpdateBadgeBorder = "#6EE7B7";
                }
            });
        }
        catch (Exception ex)
        {
            _logService.LogWarning("Không thể cập nhật trạng thái Windows Update: " + ex.Message);
        }
    }

    [RelayCommand]
    public async Task EnableWindowsUpdateAsync()
    {
        if (IsBusy) return;
        if (!ConfirmAction(
            "Bật Windows Update",
            "Bạn có chắc chắn muốn BẬT LẠI dịch vụ Windows Update?\n\nPhần mềm sẽ tự động phá khóa WUB, khắc phục lỗi 'Access is denied' và khôi phục toàn diện dịch vụ để bạn có thể cập nhật."))
        {
            return;
        }

        IsBusy = true;
        StatusText = "Đang phá khóa WUB và bật lại Windows Update...";

        try
        {
            await _policyService.UnlockAndEnableWindowsUpdateAsync().ConfigureAwait(false);
            await LoadPoliciesAsync().ConfigureAwait(false);
            RefreshWindowsUpdateStatus();
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi bật lại Windows Update: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task DisableWindowsUpdateAsync()
    {
        if (IsBusy) return;
        if (!ConfirmAction(
            "Khóa Windows Update (Chế độ WUB)",
            "Bạn có chắc chắn muốn KHÓA TRIỆT ĐỂ Windows Update?\n\nPhần mềm sẽ dừng và vô hiệu hóa dịch vụ wuauserv, WaaSMedicSvc, UsoSvc và kích hoạt chính sách cấm tự động cập nhật ngầm."))
        {
            return;
        }

        IsBusy = true;
        StatusText = "Đang khóa triệt để Windows Update (Chế độ WUB)...";

        try
        {
            await _policyService.SetWuauservDisabledAsync(true, IsProtectServiceSettings).ConfigureAwait(false);
            await LoadPoliciesAsync().ConfigureAwait(false);
            RefreshWindowsUpdateStatus();
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi khóa Windows Update: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public void NavigateToPoliciesTab()
    {
        SelectedTabIndex = 4; // Tab 5 (Index 4: Tùy chỉnh cập nhật & Lịch)
    }

    [RelayCommand]
    public async Task ToggleWuauservAsync()
    {
        if (WuStatus.IsDisabled)
        {
            await EnableWindowsUpdateAsync();
        }
        else
        {
            await DisableWindowsUpdateAsync();
        }
    }

    [RelayCommand]
    public async Task LoadScheduleStatusAsync()
    {
        try
        {
            var status = await _scheduleService.GetScheduleStatusAsync().ConfigureAwait(false);
            ExecuteOnUIThread(() => ScheduleModel = status);
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi đọc trạng thái lịch nâng cấp: " + ex.Message);
        }
    }

    [RelayCommand]
    public async Task SaveScheduleAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Đang tạo/cập nhật tác vụ tự động trong Task Scheduler...";

        try
        {
            // 1. Lưu danh sách ứng dụng được chọn vào cấu hình (AppConfig)
            var selectedIds = ScheduledApps.Where(p => p.IsSelected)
                                           .Select(p => p.Id)
                                           .Distinct(StringComparer.OrdinalIgnoreCase)
                                           .ToList();

            // Nếu người dùng chưa chọn ứng dụng nào, mặc định chỉ giữ WhatsApp
            if (selectedIds.Count == 0)
            {
                selectedIds.Add(WingetService.WhatsAppStoreId);
            }

            _configService.CurrentConfig.ScheduledAppIds = selectedIds;
            await _configService.SaveConfigAsync(_configService.CurrentConfig).ConfigureAwait(false);

            // 2. Tạo hoặc cập nhật tác vụ trong Windows Task Scheduler
            var result = await _scheduleService.CreateOrUpdateScheduleAsync(ScheduleModel).ConfigureAwait(false);
            await LoadScheduleStatusAsync().ConfigureAwait(false);

            _logService.LogSuccess($"Đã lưu lịch tự động thành công! Đã cấu hình {selectedIds.Count} ứng dụng được tự động nâng cấp.");
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi lưu lịch tự động: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task LoadScheduledAppsAsync()
    {
        try
        {
            StatusText = "Đang tải danh sách ứng dụng cho lịch tự động...";
            var config = await _configService.LoadConfigAsync().ConfigureAwait(false);
            var scheduledIds = new HashSet<string>(config.ScheduledAppIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            var installed = await _wingetService.GetInstalledAppsAsync().ConfigureAwait(false);

            ExecuteOnUIThread(() =>
            {
                ScheduledApps.Clear();

                // 1. Luôn đưa WhatsApp lên hàng đầu (mặc định chọn nếu danh sách trống)
                bool isWaSelected = scheduledIds.Contains(WingetService.WhatsAppStoreId) ||
                                    scheduledIds.Contains(WingetService.WhatsAppWin32Id) ||
                                    scheduledIds.Count == 0;

                var waStore = new WingetPackage
                {
                    Name = "WhatsApp Desktop",
                    Id = WingetService.WhatsAppStoreId,
                    CurrentVersion = WhatsAppStatus?.CurrentVersion ?? "Đã cài đặt",
                    Source = "msstore",
                    IsSelected = isWaSelected,
                    WarningNote = "⚡ WhatsApp - Cập nhật siêu tốc 3 giây (Hỗ trợ Offline/LAN)"
                };
                ScheduledApps.Add(waStore);

                // 2. Các ứng dụng khác trên máy
                foreach (var app in installed)
                {
                    if (app.Id.Equals(WingetService.WhatsAppStoreId, StringComparison.OrdinalIgnoreCase) ||
                        app.Id.Equals(WingetService.WhatsAppWin32Id, StringComparison.OrdinalIgnoreCase) ||
                        app.Name.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var item = new WingetPackage
                    {
                        Name = app.Name,
                        Id = app.Id,
                        CurrentVersion = app.CurrentVersion,
                        Source = app.Source,
                        IsSelected = scheduledIds.Contains(app.Id)
                    };

                    if (_wingetService.IsExcludedPackage(app.Id, app.Name))
                    {
                        item.IsExcluded = true;
                        item.WarningNote = "⚠️ Ứng dụng nhạy cảm (Office/Zalo) - Khuyên dùng cập nhật nội bộ";
                    }
                    else
                    {
                        item.WarningNote = "Cập nhật qua winget";
                    }

                    ScheduledApps.Add(item);
                }

                UpdateScheduledAppsSummary();
            });
        }
        catch (Exception ex)
        {
            _logService.LogWarning("Không thể tải danh sách ứng dụng cho lịch: " + ex.Message);
        }
        finally
        {
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public void SelectAllScheduledApps()
    {
        foreach (var app in ScheduledApps)
        {
            if (!app.IsExcluded) app.IsSelected = true;
        }
        UpdateScheduledAppsSummary();
    }

    [RelayCommand]
    public void DeselectAllScheduledApps()
    {
        foreach (var app in ScheduledApps)
        {
            app.IsSelected = false;
        }
        UpdateScheduledAppsSummary();
    }

    [RelayCommand]
    public void ResetScheduledAppsToDefault()
    {
        foreach (var app in ScheduledApps)
        {
            app.IsSelected = app.Id.Equals(WingetService.WhatsAppStoreId, StringComparison.OrdinalIgnoreCase) ||
                             app.Id.Equals(WingetService.WhatsAppWin32Id, StringComparison.OrdinalIgnoreCase) ||
                             app.Name.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase);
        }
        UpdateScheduledAppsSummary();
    }

    public void UpdateScheduledAppsSummary()
    {
        int selectedCount = ScheduledApps.Count(p => p.IsSelected);
        ScheduledAppsSummaryText = $"Đã tích chọn {selectedCount} / {ScheduledApps.Count} ứng dụng để tự động cập nhật theo lịch.";
    }

    [RelayCommand]
    public async Task DeleteScheduleAsync()
    {
        if (!ConfirmAction("Xác nhận xóa lịch", "Bạn có chắc chắn muốn xóa tác vụ tự động nâng cấp khỏi Windows Task Scheduler?"))
        {
            return;
        }

        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Đang xóa tác vụ tự động...";

        try
        {
            await _scheduleService.DeleteScheduleAsync().ConfigureAwait(false);
            await LoadScheduleStatusAsync();
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi xóa lịch: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task RunScheduleNowAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Đang kích hoạt tác vụ chạy ngầm ngay lập tức...";

        try
        {
            await _scheduleService.RunScheduleNowAsync().ConfigureAwait(false);
            await LoadScheduleStatusAsync();
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi kích hoạt chạy lịch: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task ViewScheduleLogAsync()
    {
        try
        {
            string content = await _scheduleService.GetScheduleLogAsync().ConfigureAwait(false);
            ExecuteOnUIThread(() =>
            {
                ScheduleLogText = content;
                IsScheduleLogModalOpen = true;
            });
        }
        catch (Exception ex)
        {
            _logService.LogError("Không thể đọc file auto-upgrade.log: " + ex.Message);
        }
    }

    [RelayCommand]
    public void CloseScheduleLogModal()
    {
        IsScheduleLogModalOpen = false;
    }

    // ==========================================
    // LOG & PROCESS CONTROLS
    // ==========================================

    [RelayCommand]
    private void ClearLogs() => _logService.Clear();

    [RelayCommand]
    private void CopyLogs()
    {
        string text = _logService.GetAllLogsAsText();
        if (string.IsNullOrWhiteSpace(text)) return;

#if WINDOWS
        try
        {
            System.Windows.Clipboard.SetText(text);
            _logService.LogInfo("Đã sao chép toàn bộ nhật ký vào clipboard.");
        }
        catch (Exception ex)
        {
            _logService.LogError("Không thể sao chép nhật ký: " + ex.Message);
        }
#else
        _logService.LogInfo("Đã sao chép toàn bộ nhật ký.");
#endif
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            string path = _logService.GetLogFilePath();
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            else
            {
                _logService.LogWarning("Thư mục nhật ký chưa tồn tại.");
            }
        }
        catch (Exception ex)
        {
            _logService.LogError("Không thể mở thư mục nhật ký: " + ex.Message);
        }
    }

    [RelayCommand]
    private void CancelProcess()
    {
        if (_processRunner.IsRunning)
        {
            StatusText = "Đang gửi yêu cầu hủy tiến trình...";
            _processRunner.CancelCurrent();
        }
    }
}
