using System.Text;
using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public class LogService : ILogService
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private const int MaxBackupFiles = 5;
    private const int MaxInMemoryEntries = 2000;

    private readonly object _lock = new();
    private readonly List<LogEntry> _entries = new();
    private readonly string _logFilePath;

    public event Action<LogEntry>? EntryAdded;
    public event Action? Cleared;

    public LogService(string? customLogPath = null)
    {
        _logFilePath = !string.IsNullOrWhiteSpace(customLogPath)
            ? customLogPath
            : DetermineLogFilePath();

        EnsureDirectoryExists(_logFilePath);
    }

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var entry = new LogEntry(DateTime.Now, level, message.TrimEnd());

        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxInMemoryEntries)
            {
                _entries.RemoveAt(0);
            }

            WriteToFile(entry);
        }

        try
        {
            EntryAdded?.Invoke(entry);
        }
        catch
        {
            // Do not allow subscriber exceptions to break logging
        }
    }

    public void LogInfo(string message) => Log(message, LogLevel.Info);

    public void LogWarning(string message) => Log(message, LogLevel.Warning);

    public void LogError(string message, Exception? ex = null)
    {
        string fullMessage = ex != null
            ? $"{message}\n[Chi tiết lỗi]: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"
            : message;
        Log(fullMessage, LogLevel.Error);
    }

    public void LogSuccess(string message) => Log(message, LogLevel.Success);

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }

        try
        {
            Cleared?.Invoke();
        }
        catch
        {
            // Do not allow subscriber exceptions to break clearing
        }
    }

    public IReadOnlyList<LogEntry> GetRecentEntries()
    {
        lock (_lock)
        {
            return _entries.ToList();
        }
    }

    public string GetLogFilePath() => _logFilePath;

    public string GetAllLogsAsText()
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            foreach (var entry in _entries)
            {
                sb.AppendLine(entry.ToString());
            }
            return sb.ToString();
        }
    }

    private void WriteToFile(LogEntry entry)
    {
        try
        {
            RotateLogFilesIfNeeded();
            File.AppendAllText(_logFilePath, entry + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Fail silently on file I/O to avoid app crashes
        }
    }

    private void RotateLogFilesIfNeeded()
    {
        try
        {
            if (!File.Exists(_logFilePath)) return;

            var fileInfo = new FileInfo(_logFilePath);
            if (fileInfo.Length < MaxFileSizeBytes) return;

            string dir = Path.GetDirectoryName(_logFilePath) ?? string.Empty;
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(_logFilePath);
            string ext = Path.GetExtension(_logFilePath);

            // Delete oldest backup if it exists
            string oldestBackup = Path.Combine(dir, $"{fileNameWithoutExt}.{MaxBackupFiles}{ext}");
            if (File.Exists(oldestBackup))
            {
                File.Delete(oldestBackup);
            }

            // Shift older backups: e.g. log.4.txt -> log.5.txt
            for (int i = MaxBackupFiles - 1; i >= 1; i--)
            {
                string source = Path.Combine(dir, $"{fileNameWithoutExt}.{i}{ext}");
                string target = Path.Combine(dir, $"{fileNameWithoutExt}.{i + 1}{ext}");
                if (File.Exists(source))
                {
                    File.Move(source, target, true);
                }
            }

            // Move current log.txt to log.1.txt
            string firstBackup = Path.Combine(dir, $"{fileNameWithoutExt}.1{ext}");
            File.Move(_logFilePath, firstBackup, true);
        }
        catch
        {
            // Ignore rotation errors
        }
    }

    private static string DetermineLogFilePath()
    {
        // 1. Try ProgramData (Windows standard)
        try
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrWhiteSpace(programData))
            {
                string targetDir = Path.Combine(programData, "WinUpdateManager");
                if (DirectoryIsWritable(targetDir))
                {
                    return Path.Combine(targetDir, "log.txt");
                }
            }
        }
        catch { }

        // 2. Try LocalAppData
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                string targetDir = Path.Combine(localAppData, "WinUpdateManager");
                if (DirectoryIsWritable(targetDir))
                {
                    return Path.Combine(targetDir, "log.txt");
                }
            }
        }
        catch { }

        // 3. Fallback to Temp path
        string tempDir = Path.Combine(Path.GetTempPath(), "WinUpdateManager");
        return Path.Combine(tempDir, "log.txt");
    }

    private static bool DirectoryIsWritable(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            string testFile = Path.Combine(path, Path.GetRandomFileName());
            using (var fs = File.Create(testFile, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureDirectoryExists(string filePath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
        catch { }
    }
}
