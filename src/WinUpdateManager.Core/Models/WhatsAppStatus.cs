namespace WinUpdateManager.Core.Models;

public class WhatsAppStatus
{
    public bool IsInstalled { get; set; }
    public string PackageId { get; set; } = "9NKSQGP7F2NH";
    public string PackageType { get; set; } = "Store"; // "Store" or "Win32"
    public string CurrentVersion { get; set; } = string.Empty;
    public string AvailableVersion { get; set; } = string.Empty;
    public bool HasUpdate => !string.IsNullOrWhiteSpace(AvailableVersion) &&
                             !string.Equals(CurrentVersion, AvailableVersion, StringComparison.OrdinalIgnoreCase);
    public string? CachedInstallerPath { get; set; }
    public bool IsWuauservDisabled { get; set; }
}
