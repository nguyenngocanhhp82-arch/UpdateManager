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

    public ObservableCollection<WingetPackage> UpgradableApps { get; } = new();
    private readonly List<WingetPackage> _allUpgradableApps = new();

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
        IConfigService configService)
    {
        _logService = logService;
        _processRunner = processRunner;
        _wingetService = wingetService;
        _configService = configService;

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
    // WHATSAPP SPECIAL ACTIONS (GIAI ĐOẠN 2)
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

    /// <summary>
    /// Nút 1: Cập nhật siêu tốc WhatsApp
    /// Tự động kiểm tra gói trong Cache/LAN -> Nếu chưa có, tự động tải về -> Cài đặt tức thì chỉ trong 3-5 giây!
    /// Bỏ qua Windows Update Blocker và Store hang.
    /// </summary>
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

    /// <summary>
    /// Cho phép người dùng hoặc IT chọn file .msixbundle / .exe từ máy tính hoặc USB
    /// </summary>
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

    /// <summary>
    /// Tải gói cài đặt mới nhất về bộ nhớ đệm / thư mục chia sẻ LAN
    /// </summary>
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

    // ==========================================
    // TAB 1: CẬP NHẬT ỨNG DỤNG (WINGET UPGRADE)
    // ==========================================

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
