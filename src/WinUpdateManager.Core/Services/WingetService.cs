namespace WinUpdateManager.Core.Services;

public interface IWingetService
{
    Task<bool> IsWingetInstalledAsync(CancellationToken cancellationToken = default);
}

public class WingetService : IWingetService
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogService _logService;

    public WingetService(IProcessRunner processRunner, ILogService logService)
    {
        _processRunner = processRunner;
        _logService = logService;
    }

    public async Task<bool> IsWingetInstalledAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _processRunner.RunAsync("winget", "--version", cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
