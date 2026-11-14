namespace WinUpdateManager.Core.Models;

public sealed record WindowsUpdateInstallResult(
    int ResultCode,
    bool RebootRequired,
    int InstalledCount,
    int FailedCount,
    string Message
)
{
    public bool Succeeded => ResultCode == 2 || ResultCode == 0; // 2 = OperationSucceeded in WUA
}
