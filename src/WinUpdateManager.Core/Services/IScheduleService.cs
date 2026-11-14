using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public interface IScheduleService
{
    Task<ScheduleTaskModel> GetScheduleStatusAsync(CancellationToken cancellationToken = default);
    Task<ProcessResult> CreateOrUpdateScheduleAsync(ScheduleTaskModel model, CancellationToken cancellationToken = default);
    Task<ProcessResult> DeleteScheduleAsync(CancellationToken cancellationToken = default);
    Task<ProcessResult> RunScheduleNowAsync(CancellationToken cancellationToken = default);
    Task<string> GetScheduleLogAsync(CancellationToken cancellationToken = default);
    string GetScheduleLogPath();
}
