namespace WinUpdateManager.Core.Models;

public class WingetPackage
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
    public string AvailableVersion { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public bool IsSelected { get; set; }
    public bool IsPriority { get; set; }
    public bool IsExcluded { get; set; }
    public string WarningNote { get; set; } = string.Empty;

    public bool HasUpdate => !string.IsNullOrWhiteSpace(AvailableVersion) &&
                             !string.Equals(CurrentVersion, AvailableVersion, StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        $"{Name} [{Id}] {CurrentVersion} -> {AvailableVersion} ({Source})";
}
