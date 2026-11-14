namespace WinUpdateManager.Core.Models;

public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool IsCancelled = false
)
{
    public bool Succeeded => !IsCancelled && ExitCode == 0;

    public string AllOutput => string.IsNullOrWhiteSpace(StandardError)
        ? StandardOutput
        : string.IsNullOrWhiteSpace(StandardOutput)
            ? StandardError
            : $"{StandardOutput}\n{StandardError}";
}
