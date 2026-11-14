namespace WinUpdateManager.Core.Services;

public interface IWindowsUpdateService
{
}

public class WindowsUpdateService : IWindowsUpdateService
{
    private readonly ILogService _logService;

    public WindowsUpdateService(ILogService logService)
    {
        _logService = logService;
    }
}
