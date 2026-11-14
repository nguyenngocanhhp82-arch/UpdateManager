using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public interface ILogService
{
    void Log(string message, LogLevel level = LogLevel.Info);
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message, Exception? ex = null);
    void LogSuccess(string message);
    void Clear();
    IReadOnlyList<LogEntry> GetRecentEntries();
    string GetLogFilePath();
    string GetAllLogsAsText();

    event Action<LogEntry>? EntryAdded;
    event Action? Cleared;
}
