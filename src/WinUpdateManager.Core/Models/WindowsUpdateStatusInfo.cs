namespace WinUpdateManager.Core.Models;

public class WindowsUpdateStatusInfo
{
    public bool IsDisabled { get; set; } = true;
    public string DisplayStatus { get; set; } = "Đang Bị Khóa (Disabled)";
    public string StatusDescription { get; set; } = "Dịch vụ cập nhật hệ thống đang bị vô hiệu hóa.";
    public string StatusColor { get; set; } = "#DC2626"; // Red
    public string ServiceStartMode { get; set; } = "Disabled";
    public bool IsProtectedByWub { get; set; }
}
