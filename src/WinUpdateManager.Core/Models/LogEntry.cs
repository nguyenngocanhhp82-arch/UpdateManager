namespace WinUpdateManager.Core.Models;

public enum LogLevel
{
    Info,
    Warning,
    Error,
    Success
}

public sealed record LogEntry(
    DateTime Timestamp,
    LogLevel Level,
    string Message
)
{
    public override string ToString() =>
        $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level.ToString().ToUpperInvariant()}] {Message}";
}
