namespace WinUpdateManager.Core.Services;

public interface IScheduleService
{
}

public class ScheduleService : IScheduleService
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogService _logService;

    public ScheduleService(IProcessRunner processRunner, ILogService logService)
    {
        _processRunner = processRunner;
        _logService = logService;
    }
}
