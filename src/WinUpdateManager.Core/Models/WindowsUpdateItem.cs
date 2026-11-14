namespace WinUpdateManager.Core.Models;

public class WindowsUpdateItem
{
    public string Title { get; set; } = string.Empty;
    public string KbArticleId { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Category { get; set; } = "Phần mềm"; // Software, Driver, Security, Quality
    public bool IsHidden { get; set; }
    public bool IsDownloaded { get; set; }
    public bool IsSelected { get; set; }

    public string SizeDisplay => FormatSize(SizeBytes);

    public object? NativeUpdateObject { get; set; }

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "Không xác định";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    public override string ToString() =>
        $"[{KbArticleId}] {Title} ({Category}, {SizeDisplay})";
}
