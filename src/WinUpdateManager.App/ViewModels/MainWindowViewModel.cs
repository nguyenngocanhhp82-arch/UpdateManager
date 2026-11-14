using System.Collections.ObjectModel;
using System.Diagnostics;
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

    [ObservableProperty]
    private ScheduleTaskModel _scheduleModel = new();

    [ObservableProperty]
    private string _scheduleLogText = string.Empty;

    [ObservableProperty]
    private bool _isScheduleLogModalOpen;

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

        _logService.LogInfo("Win Update Manager đã khởi động thành công.");

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
    public async Task UpdateEverythingAsync()
    {
        if (IsBusy) return;

        bool confirm = ConfirmAction(
            "Cập nhật toàn bộ hệ thống",
            "Bạn có muốn thực hiện chu trình cập nhật toàn diện:\n\n" +
            "1. Cập nhật nhanh ứng dụng WhatsApp (kể cả khi đã tắt Windows Update)\n" +
            "2. Quét và cập nhật toàn bộ ứng dụng winget (winget upgrade --all)\n" +
            "3. Kiểm tra các bản cập nhật Windows Update mới\n\n" +
            "Quá trình này sẽ chạy tự động và hiển thị tiến trình chi tiết bên dưới. Bạn có muốn tiếp tục?");

        if (!confirm) return;

        IsBusy = true;
        StatusText = "Đang thực hiện cập nhật toàn bộ hệ thống...";
        _logService.LogInfo("=== BẮT ĐẦU CHU TRÌNH CẬP NHẬT TOÀN BỘ HỆ THỐNG ===");

        try
        {
            // Bước 1: WhatsApp
            StatusText = "1/3: Đang cập nhật nhanh WhatsApp...";
            _logService.LogInfo(">>> [1/3] Cập nhật siêu tốc WhatsApp...");
            await _wingetService.FastUpdateWhatsAppAsync().ConfigureAwait(false);
            await RefreshWhatsAppStatusAsync().ConfigureAwait(false);

            // Bước 2: Winget All
            StatusText = "2/3: Đang cập nhật toàn bộ phần mềm (winget)...";
            _logService.LogInfo(">>> [2/3] Nâng cấp toàn bộ ứng dụng hệ thống qua winget...");
            await _wingetService.UpgradeAllPackagesAsync().ConfigureAwait(false);
            await ScanUpgradesAsync().ConfigureAwait(false);

            // Bước 3: Scan Windows Update
            StatusText = "3/3: Đang quét kiểm tra Windows Update...";
            _logService.LogInfo(">>> [3/3] Quét các bản vá Windows Update chờ cài đặt...");
            var wuItems = await _windowsUpdateService.SearchUpdatesAsync(includeHidden: false).ConfigureAwait(false);
            ExecuteOnUIThread(() =>
            {
                WindowsUpdates.Clear();
                foreach (var item in wuItems)
                {
                    item.IsSelected = true;
                    WindowsUpdates.Add(item);
                }
                WindowsUpdateSummary = $"Tìm thấy {WindowsUpdates.Count} bản cập nhật Windows sẵn sàng cài đặt.";
            });

            _logService.LogSuccess("=== HOÀN TẤT CHU TRÌNH CẬP NHẬT TOÀN BỘ HỆ THỐNG ===");
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi trong chu trình cập nhật toàn bộ: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
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
        foreach (var p in UpgradableApps) p.IsSelected = true;
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

        if (IsBusy) return;
        IsBusy = true;

        try
        {
            for (int i = 0; i < selected.Count; i++)
            {
                var pkg = selected[i];
                StatusText = $"Đang cập nhật [{i + 1}/{selected.Count}]: {pkg.Name}...";
                _logService.LogInfo($"--- [{i + 1}/{selected.Count}] Bắt đầu cập nhật: {pkg.Name} [{pkg.Id}] ---");

                await _wingetService.UpgradePackageAsync(pkg.Id).ConfigureAwait(false);
            }

            _logService.LogSuccess("Đã hoàn tất quá trình cập nhật các ứng dụng đã chọn.");
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
        if (!ConfirmAction("Xác nhận cập nhật tất cả", "Bạn có chắc chắn muốn cập nhật toàn bộ các ứng dụng có bản mới?"))
        {
            return;
        }

        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Đang cập nhật tất cả ứng dụng...";

        try
        {
            await _wingetService.UpgradeAllPackagesAsync().ConfigureAwait(false);
            await ScanUpgradesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi cập nhật tất cả ứng dụng: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
        }
    }

    [RelayCommand]
    public async Task UpgradePriorityGroupAsync()
    {
        var priorityList = UpgradableApps.Where(p => p.IsPriority).ToList();
        if (priorityList.Count == 0)
        {
            _logService.LogInfo("Không có ứng dụng nào thuộc nhóm ưu tiên (WhatsApp, Chrome, Zalo) có bản cập nhật mới.");
            return;
        }

        if (IsBusy) return;
        IsBusy = true;

        try
        {
            for (int i = 0; i < priorityList.Count; i++)
            {
                var pkg = priorityList[i];
                StatusText = $"Đang cập nhật nhóm ưu tiên [{i + 1}/{priorityList.Count}]: {pkg.Name}...";
                await _wingetService.UpgradePackageAsync(pkg.Id).ConfigureAwait(false);
            }

            _logService.LogSuccess("Hoàn thành cập nhật nhóm ứng dụng ưu tiên.");
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
            ExecuteOnUIThread(() => PolicyModel = policies);
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
    public async Task ToggleWuauservAsync()
    {
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            bool willDisable = !PolicyModel.IsWuauservDisabled;
            string msg = willDisable
                ? "Bạn có chắc chắn muốn Khóa/Tắt dịch vụ Windows Update (wuauserv)?"
                : "Bạn có chắc chắn muốn Bật lại dịch vụ Windows Update (wuauserv - Manual)?";

            if (!ConfirmAction("Cấu hình dịch vụ wuauserv", msg))
            {
                return;
            }

            StatusText = willDisable ? "Đang khóa dịch vụ wuauserv..." : "Đang bật dịch vụ wuauserv...";
            await _policyService.SetWuauservDisabledAsync(willDisable).ConfigureAwait(false);
            await LoadPoliciesAsync();
        }
        catch (Exception ex)
        {
            _logService.LogError("Lỗi khi thay đổi trạng thái dịch vụ wuauserv: " + ex.Message, ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Sẵn sàng";
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
            var result = await _scheduleService.CreateOrUpdateScheduleAsync(ScheduleModel).ConfigureAwait(false);
            await LoadScheduleStatusAsync();
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
