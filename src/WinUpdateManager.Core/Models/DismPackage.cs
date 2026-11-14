namespace WinUpdateManager.Core.Models;

public class DismPackage
{
    public string PackageName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string ReleaseType { get; set; } = string.Empty;
    public string InstallTime { get; set; } = string.Empty;
    public bool IsSelected { get; set; }

    public bool IsRollupFix => PackageName.Contains("RollupFix", StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        $"{PackageName} [{State}] ({ReleaseType})";
}
