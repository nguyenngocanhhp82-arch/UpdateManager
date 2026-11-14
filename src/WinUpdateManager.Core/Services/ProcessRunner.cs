using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public partial class ProcessRunner : IProcessRunner
{
    private readonly ILogService? _logService;
    private readonly SemaphoreSlim _concurrencySemaphore = new(1, 1);

    private Process? _currentProcess;
    private CancellationTokenSource? _activeCts;
    private int _isRunningFlag;

    // ANSI escape sequences (e.g. colors, cursor controls, DEC private modes)
    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled)]
    private static partial Regex AnsiEscapeRegex();

    // Progress bar line pattern (e.g. winget "[████████░░░░] 45%")
    [GeneratedRegex(@"^[█▒░■□\s\-\\\|\/\.\%0-9\[\]\(\)]+$", RegexOptions.Compiled)]
    private static partial Regex ProgressBarRegex();

    public bool IsRunning => Volatile.Read(ref _isRunningFlag) == 1;

    public ProcessRunner(ILogService? logService = null)
    {
        _logService = logService;
    }

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null)
    {
        return await ExecuteProcessAsync(
            fileName,
            startInfo =>
            {
                foreach (var arg in arguments)
                {
                    startInfo.ArgumentList.Add(arg);
                }
            },
            cancellationToken,
            onOutputLine,
            onErrorLine);
    }

    public async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null)
    {
        return await ExecuteProcessAsync(
            fileName,
            startInfo =>
            {
                startInfo.Arguments = arguments;
            },
            cancellationToken,
            onOutputLine,
            onErrorLine);
    }

    public void CancelCurrent()
    {
        try
        {
            _activeCts?.Cancel();
        }
        catch { }

        try
        {
            var proc = _currentProcess;
            if (proc != null && !proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
            }
        }
        catch { }
    }

    private async Task<ProcessResult> ExecuteProcessAsync(
        string fileName,
        Action<ProcessStartInfo> configureStartInfo,
        CancellationToken cancellationToken,
        Action<string>? onOutputLine,
        Action<string>? onErrorLine)
    {
        // Enforce concurrency limit: only one heavy process at a time
        if (!_concurrencySemaphore.Wait(0))
        {
            throw new InvalidOperationException("Một tác vụ khác đang được thực thi. Vui lòng đợi tác vụ hoàn tất hoặc bấm Hủy.");
        }

        Volatile.Write(ref _isRunningFlag, 1);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeCts = linkedCts;

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        configureStartInfo(startInfo);

        Process? process = null;
        bool isCancelled = false;

        try
        {
            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _currentProcess = process;

            var stdoutTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stderrTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null)
                {
                    stdoutTcs.TrySetResult(true);
                    return;
                }

                string? cleaned = CleanLine(e.Data);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    lock (stdoutBuilder)
                    {
                        stdoutBuilder.AppendLine(cleaned);
                    }
                    _logService?.LogInfo(cleaned);
                    onOutputLine?.Invoke(cleaned);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                {
                    stderrTcs.TrySetResult(true);
                    return;
                }

                string? cleaned = CleanLine(e.Data);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    lock (stderrBuilder)
                    {
                        stderrBuilder.AppendLine(cleaned);
                    }
                    _logService?.LogError(cleaned);
                    onErrorLine?.Invoke(cleaned);
                }
            };

            if (!process.Start())
            {
                throw new InvalidOperationException($"Không thể khởi động tiến trình: {fileName}");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using (linkedCts.Token.Register(() =>
            {
                isCancelled = true;
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch { }
            }))
            {
                await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
                // Ensure all stream data has been received
                await Task.WhenAll(stdoutTcs.Task, stderrTcs.Task).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }

            return new ProcessResult(
                ExitCode: process.ExitCode,
                StandardOutput: stdoutBuilder.ToString().Trim(),
                StandardError: stderrBuilder.ToString().Trim(),
                IsCancelled: isCancelled
            );
        }
        catch (OperationCanceledException)
        {
            isCancelled = true;
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch { }

            _logService?.LogWarning("Tiến trình đã bị người dùng hủy.");
            return new ProcessResult(-1, stdoutBuilder.ToString().Trim(), stderrBuilder.ToString().Trim(), IsCancelled: true);
        }
        finally
        {
            _currentProcess = null;
            _activeCts = null;
            Volatile.Write(ref _isRunningFlag, 0);
            _concurrencySemaphore.Release();
            process?.Dispose();
        }
    }

    /// <summary>
    /// Cleans noise from tool output (winget spinner characters, carriage returns, progress bar blocks, ANSI escape codes).
    /// Returns null or empty if the line should be suppressed from the log.
    /// </summary>
    public static string? CleanLine(string? rawLine)
    {
        if (string.IsNullOrEmpty(rawLine)) return null;

        // 1. Strip ANSI escape sequences
        string line = AnsiEscapeRegex().Replace(rawLine, string.Empty);

        // 2. Handle carriage returns: winget repeatedly updates the same terminal line using '\r'
        if (line.Contains('\r'))
        {
            string[] segments = line.Split('\r', StringSplitOptions.RemoveEmptyEntries);
            line = segments.Length > 0 ? segments[^1] : string.Empty;
        }

        line = line.Trim();
        if (string.IsNullOrWhiteSpace(line)) return null;

        // 3. Filter standalone winget spinners
        if (line is "-" or "\\" or "|" or "/" or "—")
        {
            return null;
        }

        // 4. Filter progress bar blocks (e.g. █ ▒ ░ ■ □)
        if (line.Contains('█') || line.Contains('▒') || line.Contains('░'))
        {
            // If the line consists only of progress blocks, percentage, and formatting
            if (ProgressBarRegex().IsMatch(line))
            {
                return null;
            }

            // Strip progress characters embedded in text
            line = line.Replace("█", "").Replace("▒", "").Replace("░", "").Trim();
            if (string.IsNullOrWhiteSpace(line)) return null;
        }

        return line;
    }
}
