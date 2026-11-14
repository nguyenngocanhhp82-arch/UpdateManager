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

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Sẵn sàng";

    [ObservableProperty]
    private LogEntry? _selectedLogEntry;

    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public MainWindowViewModel(ILogService logService, IProcessRunner processRunner)
    {
        _logService = logService;
        _processRunner = processRunner;

        // Initialize with existing log entries
        foreach (var entry in _logService.GetRecentEntries())
        {
            LogEntries.Add(entry);
        }

        _logService.EntryAdded += OnLogEntryAdded;
        _logService.Cleared += OnLogCleared;

        _logService.LogInfo("Win Update Manager đã khởi động thành công.");
    }

    private void OnLogEntryAdded(LogEntry entry)
    {
        void Add()
        {
            LogEntries.Add(entry);
            if (LogEntries.Count > 1000)
            {
                LogEntries.RemoveAt(0);
            }
        }

#if WINDOWS
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(Add);
            return;
        }
#endif
        Add();
    }

    private void OnLogCleared()
    {
        void Clear() => LogEntries.Clear();

#if WINDOWS
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(Clear);
            return;
        }
#endif
        Clear();
    }

    [RelayCommand]
    private void ClearLogs()
    {
        _logService.Clear();
    }

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
