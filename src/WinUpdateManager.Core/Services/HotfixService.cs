namespace WinUpdateManager.Core.Services;

public interface IHotfixService
{
}

public class HotfixService : IHotfixService
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogService _logService;

    public HotfixService(IProcessRunner processRunner, ILogService logService)
    {
        _processRunner = processRunner;
        _logService = logService;
    }
}
