namespace WinUpdateManager.Core.Models;

public class InstalledHotfix
{
    public string HotFixId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string InstalledOn { get; set; } = string.Empty;
    public string InstalledBy { get; set; } = string.Empty;
    public bool IsSelected { get; set; }

    public string NumericKb => HotFixId.StartsWith("KB", StringComparison.OrdinalIgnoreCase)
        ? HotFixId.Substring(2)
        : HotFixId;

    public override string ToString() =>
        $"{HotFixId} - {Description} ({InstalledOn})";
}
