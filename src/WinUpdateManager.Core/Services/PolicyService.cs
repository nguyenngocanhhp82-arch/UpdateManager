namespace WinUpdateManager.Core.Services;

public interface IPolicyService
{
}

public class PolicyService : IPolicyService
{
    private readonly ILogService _logService;

    public PolicyService(ILogService logService)
    {
        _logService = logService;
    }
}
