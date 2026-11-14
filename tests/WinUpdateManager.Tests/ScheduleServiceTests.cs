using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WinUpdateManager.Core.Models;
using WinUpdateManager.Core.Services;
using Xunit;

namespace WinUpdateManager.Tests;

public class ScheduleServiceTests
{
    private class DummyProcessRunner : IProcessRunner
    {
        public bool IsRunning => false;

        public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, System.Threading.CancellationToken cancellationToken = default, Action<string>? onOutputLine = null, Action<string>? onErrorLine = null)
        {
            return Task.FromResult(new ProcessResult(0, "SUCCESS", string.Empty));
        }

        public Task<ProcessResult> RunAsync(string fileName, string arguments, System.Threading.CancellationToken cancellationToken = default, Action<string>? onOutputLine = null, Action<string>? onErrorLine = null)
        {
            return Task.FromResult(new ProcessResult(0, "SUCCESS", string.Empty));
        }

        public void CancelCurrent() { }
    }

    [Fact]
    public void ParseSchtasksOutput_ParsesEnglishOutputCorrectly()
    {
        string sampleOutput = @"
Folder: \
HostName:                             DESKTOP-ABC
TaskName:                             \WinUpdateManager_AutoUpgrade
Next Run Time:                        11/15/2026 12:00:00 PM
Status:                               Ready
Logon Mode:                           Interactive/Background
Last Run Time:                        11/14/2026 12:00:00 PM
Last Result:                          0
Author:                               IT Administrator
Task To Run:                          ""C:\Program Files\WinUpdateManager\WinUpdateManager.exe"" --auto-upgrade
";

        var model = new ScheduleTaskModel();
        ScheduleService.ParseSchtasksOutput(sampleOutput, model);

        Assert.Equal("Ready", model.Status);
        Assert.Equal("11/15/2026 12:00:00 PM", model.NextRunTime);
        Assert.Equal("11/14/2026 12:00:00 PM", model.LastRunTime);
        Assert.Equal("Thành công (0)", model.LastResult);
    }

    [Fact]
    public void ParseSchtasksOutput_ParsesVietnameseOutputCorrectly()
    {
        string sampleOutput = @"
Tên tác vụ:                           \WinUpdateManager_AutoUpgrade
Thời gian chạy tiếp theo:             15/11/2026 12:00:00
Trạng thái:                           Sẵn sàng
Thời gian chạy lần cuối:              14/11/2026 12:00:00
Kết quả lần cuối:                     267011
";

        var model = new ScheduleTaskModel();
        ScheduleService.ParseSchtasksOutput(sampleOutput, model);

        Assert.Equal("Sẵn sàng", model.Status);
        Assert.Equal("15/11/2026 12:00:00", model.NextRunTime);
        Assert.Equal("14/11/2026 12:00:00", model.LastRunTime);
        Assert.Equal("Chưa từng chạy", model.LastResult);
    }

    [Theory]
    [InlineData("0", "Thành công (0)")]
    [InlineData("0x0", "Thành công (0)")]
    [InlineData("267011", "Chưa từng chạy")]
    [InlineData("267009", "Đang chạy")]
    [InlineData("267014", "Bị người dùng dừng")]
    [InlineData("1", "1")]
    public void ExplainSchtasksResult_TranslatesCodesCorrectly(string inputCode, string expected)
    {
        string result = ScheduleService.ExplainSchtasksResult(inputCode);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("12:60")]
    [InlineData("invalid")]
    [InlineData("")]
    public async Task CreateOrUpdateScheduleAsync_ThrowsArgumentException_OnInvalidTime(string invalidTime)
    {
        var log = new LogService();
        var runner = new DummyProcessRunner();
        var scheduleService = new ScheduleService(runner, log);

        var model = new ScheduleTaskModel
        {
            ExecutionTime = invalidTime
        };

        await Assert.ThrowsAsync<ArgumentException>(() => scheduleService.CreateOrUpdateScheduleAsync(model));
    }
}
