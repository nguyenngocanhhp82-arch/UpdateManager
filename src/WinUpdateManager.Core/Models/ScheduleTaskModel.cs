namespace WinUpdateManager.Core.Models;

public class ScheduleTaskModel
{
    public const string DefaultTaskName = "WinUpdateManager-AutoUpgradeApps";

    public bool IsTaskInstalled { get; set; }
    public string TaskName { get; set; } = DefaultTaskName;
    public string ScheduleType { get; set; } = "Daily"; // "Daily" or "Weekly"
    public string ExecutionTime { get; set; } = "12:00"; // HH:mm
    public List<string> SelectedDays { get; set; } = new() { "MON", "WED", "FRI" };

    public string LastRunTime { get; set; } = "Chưa chạy";
    public string LastResult { get; set; } = "Chưa có kết quả";
    public string NextRunTime { get; set; } = "Chưa xác định";
    public string Status { get; set; } = "Chưa cài đặt";
}
