namespace WinUpdateManager.Core.Models;

public class AppConfig
{
    public List<string> PriorityAppIds { get; set; } = new()
    {
        "9NKSQGP7F2NH",       // WhatsApp Microsoft Store
        "WhatsApp.WhatsApp", // WhatsApp Desktop Win32
        "Google.Chrome"       // Google Chrome
    };

    // Danh sách ứng dụng được người dùng tích chọn để tự động nâng cấp theo lịch (Scheduled Task)
    public List<string> ScheduledAppIds { get; set; } = new()
    {
        "9NKSQGP7F2NH",       // WhatsApp Microsoft Store
        "WhatsApp.WhatsApp"  // WhatsApp Desktop Win32
    };

    // Danh sách ứng dụng nhạy cảm cần loại trừ khỏi cập nhật tự động
    // (Office dùng Click-to-Run riêng, Zalo cập nhật per-user khi khởi động)
    public List<string> ExcludedAppIds { get; set; } = new()
    {
        "Microsoft.Office",
        "Microsoft.365",
        "VNG.Zalo",
        "Zalo.Zalo"
    };

    public string? CacheDirectory { get; set; }
    public string? SharedLanDirectory { get; set; }
    public bool AutoCachePackages { get; set; } = true;
    public bool IncludeUnknown { get; set; } = false;
    public bool CreateRestorePointBeforeUninstall { get; set; } = true;
    public string PreferredWhatsAppPackageType { get; set; } = "Store"; // "Store" or "Win32"
}
