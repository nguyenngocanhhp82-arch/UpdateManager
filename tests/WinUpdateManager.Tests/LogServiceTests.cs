using FluentAssertions;
using WinUpdateManager.Core.Models;
using WinUpdateManager.Core.Services;
using Xunit;

namespace WinUpdateManager.Tests;

public class LogServiceTests : IDisposable
{
    private readonly string _tempLogDir;
    private readonly string _tempLogFile;

    public LogServiceTests()
    {
        _tempLogDir = Path.Combine(Path.GetTempPath(), "WinUpdateManager_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempLogDir);
        _tempLogFile = Path.Combine(_tempLogDir, "test-log.txt");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempLogDir))
            {
                Directory.Delete(_tempLogDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void LogInfo_AppendsEntryWithCorrectLevelAndMessage()
    {
        var logService = new LogService(_tempLogFile);
        logService.LogInfo("Ứng dụng khởi động");

        var entries = logService.GetRecentEntries();
        entries.Should().HaveCount(1);
        entries[0].Level.Should().Be(LogLevel.Info);
        entries[0].Message.Should().Be("Ứng dụng khởi động");

        File.Exists(_tempLogFile).Should().BeTrue();
        string content = File.ReadAllText(_tempLogFile);
        content.Should().Contain("Ứng dụng khởi động");
    }

    [Fact]
    public void LogError_WithException_AppendsDetails()
    {
        var logService = new LogService(_tempLogFile);
        var ex = new InvalidOperationException("Lỗi kết nối winget");

        logService.LogError("Thất bại khi quét", ex);

        var entries = logService.GetRecentEntries();
        entries.Should().HaveCount(1);
        entries[0].Level.Should().Be(LogLevel.Error);
        entries[0].Message.Should().Contain("Thất bại khi quét");
        entries[0].Message.Should().Contain("InvalidOperationException");
        entries[0].Message.Should().Contain("Lỗi kết nối winget");
    }

    [Fact]
    public void Clear_RemovesAllInMemoryEntries()
    {
        var logService = new LogService(_tempLogFile);
        logService.LogInfo("Entry 1");
        logService.LogInfo("Entry 2");
        logService.GetRecentEntries().Should().HaveCount(2);

        bool clearedEventFired = false;
        logService.Cleared += () => clearedEventFired = true;

        logService.Clear();

        logService.GetRecentEntries().Should().BeEmpty();
        clearedEventFired.Should().BeTrue();
    }

    [Fact]
    public void EntryAdded_Event_FiredWhenLogged()
    {
        var logService = new LogService(_tempLogFile);
        LogEntry? captured = null;
        logService.EntryAdded += entry => captured = entry;

        logService.LogSuccess("Cập nhật hoàn tất");

        captured.Should().NotBeNull();
        captured!.Level.Should().Be(LogLevel.Success);
        captured.Message.Should().Be("Cập nhật hoàn tất");
    }

    [Fact]
    public void GetAllLogsAsText_ReturnsFormattedLines()
    {
        var logService = new LogService(_tempLogFile);
        logService.LogInfo("Line A");
        logService.LogWarning("Line B");

        string allText = logService.GetAllLogsAsText();
        allText.Should().Contain("[INFO] Line A");
        allText.Should().Contain("[WARNING] Line B");
    }
}
