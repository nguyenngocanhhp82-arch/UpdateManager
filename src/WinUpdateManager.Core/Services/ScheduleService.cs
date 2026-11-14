using System.Diagnostics;
using System.Text.RegularExpressions;
using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public partial class ScheduleService : IScheduleService
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogService _logService;

    [GeneratedRegex(@"^(0[0-9]|1[0-9]|2[0-3]):[0-5][0-9]$", RegexOptions.Compiled)]
    private static partial Regex TimeFormatRegex();

    public ScheduleService(IProcessRunner processRunner, ILogService logService)
    {
        _processRunner = processRunner;
        _logService = logService;
    }

    public async Task<ScheduleTaskModel> GetScheduleStatusAsync(CancellationToken cancellationToken = default)
    {
        var model = new ScheduleTaskModel();

        if (!OperatingSystem.IsWindows())
        {
            model.Status = "Không hỗ trợ trên nền tảng này (yêu cầu Windows).";
            return model;
        }

        try
        {
            var args = new[] { "/Query", "/TN", ScheduleTaskModel.DefaultTaskName, "/FO", "LIST", "/V" };
            var result = await _processRunner.RunAsync("schtasks.exe", args, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                model.IsTaskInstalled = true;
                ParseSchtasksOutput(result.StandardOutput, model);
            }
            else
            {
                model.IsTaskInstalled = false;
                model.Status = "Chưa được thiết lập";
            }
        }
        catch (Exception ex)
        {
            _logService.LogWarning("Không thể truy vấn trạng thái Scheduled Task: " + ex.Message);
        }

        return model;
    }

    public async Task<ProcessResult> CreateOrUpdateScheduleAsync(ScheduleTaskModel model, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.ExecutionTime) || !TimeFormatRegex().IsMatch(model.ExecutionTime))
        {
            throw new ArgumentException("Thời gian chạy không hợp lệ. Vui lòng nhập theo định dạng HH:mm (ví dụ: 12:00).", nameof(model.ExecutionTime));
        }

        string exePath = Environment.ProcessPath ?? "WinUpdateManager.exe";
        string commandRun = $"\"{exePath}\" --auto-upgrade";

        _logService.LogInfo($"Bắt đầu tạo/cập nhật Scheduled Task: '{model.TaskName}' lúc {model.ExecutionTime}...");

        var args = new List<string>
        {
            "/Create",
            "/TN", model.TaskName,
            "/TR", commandRun,
            "/RL", "HIGHEST",
            "/F"
        };

        if (string.Equals(model.ScheduleType, "Weekly", StringComparison.OrdinalIgnoreCase))
        {
            string days = (model.SelectedDays != null && model.SelectedDays.Count > 0)
                ? string.Join(",", model.SelectedDays)
                : "MON";

            args.AddRange(new[] { "/SC", "WEEKLY", "/D", days, "/ST", model.ExecutionTime });
        }
        else
        {
            args.AddRange(new[] { "/SC", "DAILY", "/ST", model.ExecutionTime });
        }

        var result = await _processRunner.RunAsync("schtasks.exe", args, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
        {
            _logService.LogSuccess($"Tạo lịch tự động '{model.TaskName}' thành công!");
        }
        else
        {
            _logService.LogError($"Tạo lịch tự động thất bại: {result.StandardError}");
        }

        return result;
    }

    public async Task<ProcessResult> DeleteScheduleAsync(CancellationToken cancellationToken = default)
    {
        _logService.LogWarning($"Đang xóa Scheduled Task '{ScheduleTaskModel.DefaultTaskName}'...");

        var args = new[] { "/Delete", "/TN", ScheduleTaskModel.DefaultTaskName, "/F" };
        var result = await _processRunner.RunAsync("schtasks.exe", args, cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            _logService.LogSuccess("Đã xóa lịch tự động cập nhật khỏi hệ thống.");
        }
        else
        {
            _logService.LogWarning($"Không thể xóa lịch: {result.StandardError}");
        }

        return result;
    }

    public async Task<ProcessResult> RunScheduleNowAsync(CancellationToken cancellationToken = default)
    {
        _logService.LogInfo($"Đang gửi lệnh kích hoạt chạy ngay tác vụ '{ScheduleTaskModel.DefaultTaskName}'...");

        var args = new[] { "/Run", "/TN", ScheduleTaskModel.DefaultTaskName };
        var result = await _processRunner.RunAsync("schtasks.exe", args, cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            _logService.LogSuccess("Đã kích hoạt chạy tác vụ ngầm thành công. Xem tiến trình trong auto-upgrade.log.");
        }
        else
        {
            _logService.LogError($"Không thể kích hoạt tác vụ: {result.StandardError}");
        }

        return result;
    }

    public async Task<string> GetScheduleLogAsync(CancellationToken cancellationToken = default)
    {
        string logPath = GetScheduleLogPath();
        if (File.Exists(logPath))
        {
            return await File.ReadAllTextAsync(logPath, cancellationToken).ConfigureAwait(false);
        }
        return "Chưa có file nhật ký tự động (tác vụ chưa từng được chạy).";
    }

    public string GetScheduleLogPath()
    {
        try
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrWhiteSpace(programData))
            {
                return Path.Combine(programData, "WinUpdateManager", "auto-upgrade.log");
            }
        }
        catch { }

        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                return Path.Combine(localAppData, "WinUpdateManager", "auto-upgrade.log");
            }
        }
        catch { }

        return Path.Combine(Path.GetTempPath(), "WinUpdateManager", "auto-upgrade.log");
    }

    internal static void ParseSchtasksOutput(string output, ScheduleTaskModel model)
    {
        var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.Contains(':'))
            {
                int colonIdx = line.IndexOf(':');
                string key = line.Substring(0, colonIdx).Trim();
                string val = line.Substring(colonIdx + 1).Trim();

                if (key.Contains("Status", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("Trạng thái", StringComparison.OrdinalIgnoreCase))
                {
                    model.Status = val;
                }
                else if (key.Contains("Last Run Time", StringComparison.OrdinalIgnoreCase) ||
                         key.Contains("Thời gian chạy lần cuối", StringComparison.OrdinalIgnoreCase))
                {
                    model.LastRunTime = val;
                }
                else if (key.Contains("Last Result", StringComparison.OrdinalIgnoreCase) ||
                         key.Contains("Kết quả lần cuối", StringComparison.OrdinalIgnoreCase))
                {
                    model.LastResult = ExplainSchtasksResult(val);
                }
                else if (key.Contains("Next Run Time", StringComparison.OrdinalIgnoreCase) ||
                         key.Contains("Thời gian chạy tiếp theo", StringComparison.OrdinalIgnoreCase))
                {
                    model.NextRunTime = val;
                }
            }
        }
    }

    internal static string ExplainSchtasksResult(string rawResult)
    {
        if (rawResult == "0" || rawResult.Contains("0x0")) return "Thành công (0)";
        if (rawResult == "267011") return "Chưa từng chạy";
        if (rawResult == "267009") return "Đang chạy";
        if (rawResult == "267014") return "Bị người dùng dừng";
        return rawResult;
    }
}
