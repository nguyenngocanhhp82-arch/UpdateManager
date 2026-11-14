using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public interface IProcessRunner
{
    bool IsRunning { get; }
    Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null);

    Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null);

    void CancelCurrent();
}
