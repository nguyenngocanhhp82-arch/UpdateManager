namespace WinUpdateManager.Core.Models;

public enum AuOptionMode
{
    Default = 0,               // Xóa chính sách AU (để Windows tự quản lý)
    NotifyBeforeDownload = 2,  // Thông báo trước khi tải
    AutoDownloadNotifyInstall = 3, // Tự tải, hỏi trước khi cài
    AutoDownloadAndInstall = 4,    // Tự tải và cài đặt tự động
    CompletelyDisabled = 1         // Tắt hẳn tự động cập nhật (NoAutoUpdate = 1)
}

public class UpdatePolicyModel
{
    public AuOptionMode AuMode { get; set; } = AuOptionMode.CompletelyDisabled;

    // Hoãn cập nhật chất lượng (0 - 30 ngày)
    public bool DeferQualityUpdates { get; set; }
    public int DeferQualityDays { get; set; } = 7;

    // Hoãn cập nhật tính năng (0 - 365 ngày)
    public bool DeferFeatureUpdates { get; set; }
    public int DeferFeatureDays { get; set; } = 30;

    // Khóa phiên bản tính năng (ví dụ: 23H2)
    public bool TargetReleaseVersionEnabled { get; set; }
    public string ProductVersion { get; set; } = "Windows 11"; // "Windows 10" or "Windows 11"
    public string TargetReleaseVersionInfo { get; set; } = "23H2";

    // Không tự khởi động lại khi có người dùng đăng nhập
    public bool NoAutoRebootWithLoggedOnUsers { get; set; } = true;

    // Không cập nhật Driver qua Windows Update
    public bool ExcludeDriversInQualityUpdate { get; set; } = true;

    // Giờ hoạt động (Active Hours)
    public bool ActiveHoursEnabled { get; set; } = true;
    public int ActiveHoursStart { get; set; } = 8;  // 8:00
    public int ActiveHoursEnd { get; set; } = 18;  // 18:00

    // Tắt tự động cập nhật app Store
    public bool DisableStoreAutoDownload { get; set; }

    // Trạng thái dịch vụ wuauserv (true = đang bị tắt/khóa)
    public bool IsWuauservDisabled { get; set; }
}
