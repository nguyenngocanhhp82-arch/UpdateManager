using FluentAssertions;
using WinUpdateManager.Core.Services;
using Xunit;

namespace WinUpdateManager.Tests;

public class ProcessRunnerTests
{
    [Theory]
    [InlineData("\x1B[31mError message\x1B[0m", "Error message")]
    [InlineData("\x1B[1;32mUpdate successful\x1B[0m", "Update successful")]
    [InlineData("\x1B[?25hNormal text", "Normal text")]
    public void CleanLine_RemovesAnsiEscapeSequences(string input, string expected)
    {
        string? cleaned = ProcessRunner.CleanLine(input);
        cleaned.Should().Be(expected);
    }

    [Fact]
    public void CleanLine_HandlesCarriageReturnAndTakesLastSegment()
    {
        string input = "Downloading...\rDownloading 50%\rDownloading 100% complete.";
        string? cleaned = ProcessRunner.CleanLine(input);
        cleaned.Should().Be("Downloading 100% complete.");
    }

    [Theory]
    [InlineData("-")]
    [InlineData("\\")]
    [InlineData("|")]
    [InlineData("/")]
    [InlineData("—")]
    [InlineData("   |   ")]
    public void CleanLine_FiltersStandaloneSpinners(string spinner)
    {
        string? cleaned = ProcessRunner.CleanLine(spinner);
        cleaned.Should().BeNull();
    }

    [Theory]
    [InlineData("████████████████████ 100%")]
    [InlineData("▒▒▒▒▒▒▒▒▒▒ 50%")]
    [InlineData("[████████░░░░░░░░░░] 40%")]
    public void CleanLine_FiltersProgressBarBlocks(string progressBar)
    {
        string? cleaned = ProcessRunner.CleanLine(progressBar);
        cleaned.Should().BeNull();
    }

    [Fact]
    public void CleanLine_PreservesNormalOutput()
    {
        string input = "Found WhatsApp [9NKSQGP7F2NH] Version 2.2405.3.0";
        string? cleaned = ProcessRunner.CleanLine(input);
        cleaned.Should().Be(input);
    }

    [Fact]
    public async Task RunAsync_CapturesStdoutAndExitCode()
    {
        var runner = new ProcessRunner();
        // Cross-platform command
        string cmd = OperatingSystem.IsWindows() ? "cmd.exe" : "echo";
        var args = OperatingSystem.IsWindows()
            ? new[] { "/c", "echo", "Hello", "WinUpdateManager" }
            : new[] { "Hello", "WinUpdateManager" };

        var result = await runner.RunAsync(cmd, args);

        result.ExitCode.Should().Be(0);
        result.Succeeded.Should().BeTrue();
        result.IsCancelled.Should().BeFalse();
        result.StandardOutput.Should().Contain("Hello WinUpdateManager");
    }

    [Fact]
    public async Task RunAsync_EnforcesSingleTaskConcurrency_ThrowsInvalidOperationException()
    {
        var runner = new ProcessRunner();
        string cmd = OperatingSystem.IsWindows() ? "cmd.exe" : "sleep";
        var args = OperatingSystem.IsWindows()
            ? new[] { "/c", "powershell", "-Command", "Start-Sleep -Seconds 2" }
            : new[] { "2" };

        var task1 = runner.RunAsync(cmd, args);

        // Second call while task1 is running should immediately fail
        var act = async () => await runner.RunAsync(cmd, args);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Một tác vụ khác đang được thực thi*");

        runner.CancelCurrent();
        await task1;
    }

    [Fact]
    public async Task RunAsync_WithCancellation_TerminatesProcess()
    {
        var runner = new ProcessRunner();
        string cmd = OperatingSystem.IsWindows() ? "cmd.exe" : "sleep";
        var args = OperatingSystem.IsWindows()
            ? new[] { "/c", "powershell", "-Command", "Start-Sleep -Seconds 10" }
            : new[] { "10" };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var result = await runner.RunAsync(cmd, args, cts.Token);

        result.IsCancelled.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
    }
}
