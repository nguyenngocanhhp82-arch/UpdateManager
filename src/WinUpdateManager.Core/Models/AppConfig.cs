namespace WinUpdateManager.Core.Models;

public class AppConfig
{
    public List<string> PriorityAppIds { get; set; } = new()
    {
        "9NKSQGP7F2NH",       // WhatsApp Microsoft Store
        "WhatsApp.WhatsApp", // WhatsApp Desktop Win32
        "Google.Chrome",      // Google Chrome
        "VNG.Zalo"           // Zalo Desktop
    };

    public string? CacheDirectory { get; set; }
    public string? SharedLanDirectory { get; set; }
    public bool AutoCachePackages { get; set; } = true;
    public bool IncludeUnknown { get; set; } = false;
    public bool CreateRestorePointBeforeUninstall { get; set; } = true;
    public string PreferredWhatsAppPackageType { get; set; } = "Store"; // "Store" or "Win32"
}
