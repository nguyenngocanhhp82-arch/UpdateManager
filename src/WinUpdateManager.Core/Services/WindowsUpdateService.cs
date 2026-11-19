using System.Diagnostics;
using System.Text.RegularExpressions;
using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public partial class WindowsUpdateService : IWindowsUpdateService
{
    private readonly ILogService _logService;
    private readonly IProcessRunner _processRunner;

    [GeneratedRegex(@"KB\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex KbPatternRegex();

    public WindowsUpdateService(ILogService logService, IProcessRunner processRunner)
    {
        _logService = logService;
        _processRunner = processRunner;
    }

    public async Task<IReadOnlyList<WindowsUpdateItem>> SearchUpdatesAsync(bool includeHidden = false, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var result = new List<WindowsUpdateItem>();

            if (!OperatingSystem.IsWindows())
            {
                _logService.LogInfo("Windows Update Agent COM API chỉ hoạt động trên môi trường Windows.");
                return result;
            }

            try
            {
                string criteria = includeHidden
                    ? "IsInstalled=0 and IsHidden=1"
                    : "IsInstalled=0 and IsHidden=0";

                _logService.LogInfo($"Bắt đầu quét bản cập nhật Windows (Tiêu chí: '{criteria}')...");

                var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
                if (sessionType == null)
                {
                    _logService.LogError("Không tìm thấy COM ProgID 'Microsoft.Update.Session'. Hệ thống có thể bị thiếu thành phần Windows Update.");
                    return result;
                }

                dynamic session = Activator.CreateInstance(sessionType)!;
                dynamic searcher = session.CreateUpdateSearcher();
                searcher.ServerSelection = 0; // Default server (Windows Update / WSUS)

                dynamic searchResult = searcher.Search(criteria);
                dynamic updates = searchResult.Updates;
                int count = updates.Count;

                _logService.LogInfo($"Tìm thấy {count} bản cập nhật từ Windows Update Agent.");

                for (int i = 0; i < count; i++)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    dynamic update = updates.Item(i);
                    string title = (string)update.Title;
                    string kb = ExtractKbArticleId(update, title);
                    long size = 0;
                    try
                    {
                        size = (long)update.MaxDownloadSize;
                    }
                    catch { }

                    string category = DetermineCategory(update);
                    bool isHidden = (bool)update.IsHidden;
                    bool isDownloaded = (bool)update.IsDownloaded;

                    result.Add(new WindowsUpdateItem
                    {
                        Title = title,
                        KbArticleId = kb,
                        SizeBytes = size,
                        Category = category,
                        IsHidden = isHidden,
                        IsDownloaded = isDownloaded,
                        NativeUpdateObject = update
                    });
                }
            }
            catch (Exception ex)
            {
                _logService.LogError("Lỗi trong quá trình quét Windows Update: " + ex.Message, ex);
            }

            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WindowsUpdateInstallResult> InstallUpdatesAsync(IEnumerable<WindowsUpdateItem> updates, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return new WindowsUpdateInstallResult(-1, false, 0, 0, "Chỉ khả dụng trên Windows.");
            }

            var updateList = updates.ToList();
            if (updateList.Count == 0)
            {
                return new WindowsUpdateInstallResult(0, false, 0, 0, "Không có bản cập nhật nào được chọn.");
            }

            try
            {
                var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
                var collType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl");

                if (sessionType == null || collType == null)
                {
                    return new WindowsUpdateInstallResult(-1, false, 0, 0, "Không thể khởi tạo COM Windows Update.");
                }

                dynamic session = Activator.CreateInstance(sessionType)!;
                dynamic downloadColl = Activator.CreateInstance(collType)!;
                dynamic installColl = Activator.CreateInstance(collType)!;

                // 1. Accept EULA and collect updates
                foreach (var item in updateList)
                {
                    if (item.NativeUpdateObject != null)
                    {
                        dynamic update = item.NativeUpdateObject;
                        try
                        {
                            if (!(bool)update.EulaAccepted)
                            {
                                update.AcceptEula();
                            }
                        }
                        catch { }

                        downloadColl.Add(update);
                        installColl.Add(update);
                    }
                }

                // 2. Download phase
                _logService.LogInfo($"Bắt đầu tải về {downloadColl.Count} bản cập nhật Windows...");
                dynamic downloader = session.CreateUpdateDownloader();
                downloader.Updates = downloadColl;
                dynamic downloadResult = downloader.Download();
                _logService.LogInfo($"Hoàn thành tải về (Mã kết quả tải: {downloadResult.ResultCode}).");

                if (cancellationToken.IsCancellationRequested)
                {
                    return new WindowsUpdateInstallResult(-1, false, 0, 0, "Tiến trình đã bị người dùng hủy.");
                }

                // 3. Install phase
                _logService.LogInfo($"Bắt đầu cài đặt {installColl.Count} bản cập nhật Windows...");
                dynamic installer = session.CreateUpdateInstaller();
                installer.Updates = installColl;
                dynamic installResult = installer.Install();

                int resultCode = (int)installResult.ResultCode;
                bool rebootRequired = (bool)installResult.RebootRequired;

                string statusDesc = resultCode switch
                {
                    2 => "Cài đặt thành công toàn bộ các bản cập nhật.",
                    3 => "Cài đặt thành công một phần (có bản cập nhật gặp lỗi).",
                    4 => "Cài đặt thất bại.",
                    5 => "Cài đặt bị hủy ngang.",
                    _ => $"Mã kết quả cài đặt: {resultCode}"
                };

                _logService.LogSuccess($"Quá trình cài đặt hoàn tất. {statusDesc}");
                if (rebootRequired)
                {
                    _logService.LogWarning("Hệ thống yêu cầu KHỞI ĐỘNG LẠI MÁY để hoàn tất việc cập nhật!");
                }

                return new WindowsUpdateInstallResult(resultCode, rebootRequired, updateList.Count, 0, statusDesc);
            }
            catch (Exception ex)
            {
                _logService.LogError("Lỗi trong quá trình tải/cài đặt Windows Update: " + ex.Message, ex);
                return new WindowsUpdateInstallResult(-1, false, 0, updateList.Count, ex.Message);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SetUpdateHiddenAsync(WindowsUpdateItem update, bool hide, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            if (!OperatingSystem.IsWindows() || update.NativeUpdateObject == null) return false;

            try
            {
                dynamic native = update.NativeUpdateObject;
                native.IsHidden = hide;
                update.IsHidden = hide;

                string action = hide ? "Đã ẩn" : "Đã bỏ ẩn";
                _logService.LogSuccess($"{action} bản cập nhật [{update.KbArticleId}] {update.Title}.");
                return true;
            }
            catch (Exception ex)
            {
                _logService.LogError($"Không thể thay đổi trạng thái ẩn cho {update.Title}: " + ex.Message);
                return false;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HideKbArticleAsync(string kbNumber, CancellationToken cancellationToken = default)
    {
        string formattedKb = kbNumber.StartsWith("KB", StringComparison.OrdinalIgnoreCase)
            ? kbNumber.ToUpperInvariant()
            : "KB" + kbNumber;

        _logService.LogInfo($"Đang tìm kiếm bản cập nhật {formattedKb} trên Windows Update để ẩn đi...");

        var updates = await SearchUpdatesAsync(includeHidden: false, cancellationToken).ConfigureAwait(false);
        var target = updates.FirstOrDefault(u =>
            string.Equals(u.KbArticleId, formattedKb, StringComparison.OrdinalIgnoreCase) ||
            u.Title.Contains(formattedKb, StringComparison.OrdinalIgnoreCase));

        if (target != null)
        {
            return await SetUpdateHiddenAsync(target, true, cancellationToken).ConfigureAwait(false);
        }

        _logService.LogWarning($"Không tìm thấy {formattedKb} trong danh sách chờ cài đặt của Windows Update hiện tại (có thể máy cần khởi động lại trước khi bản vá xuất hiện lại).");
        return false;
    }

    public async Task RestartComputerAsync()
    {
        _logService.LogWarning("Đang gửi lệnh khởi động lại hệ thống trong 10 giây...");
        if (OperatingSystem.IsWindows())
        {
            await _processRunner.RunAsync("shutdown.exe", new[] { "/r", "/t", "10", "/c", "Lilbow Manager Update: Khoi dong lai de hoan tat cap nhat." });
        }
    }

    private static string ExtractKbArticleId(dynamic update, string title)
    {
        try
        {
            object? kbObj = update.KBArticleIDs;
            if (kbObj != null)
            {
                dynamic kbArticles = kbObj;
                int count = (int)kbArticles.Count;
                if (count > 0)
                {
                    string id = (string)kbArticles.Item(0);
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        return id.StartsWith("KB", StringComparison.OrdinalIgnoreCase) ? id : "KB" + id;
                    }
                }
            }
        }
        catch { }

        // Fallback: search regex in title
        var match = KbPatternRegex().Match(title);
        return match.Success ? match.Value.ToUpperInvariant() : "KB-Chung";
    }

    private static string DetermineCategory(dynamic update)
    {
        try
        {
            object? catObj = update.Categories;
            if (catObj != null)
            {
                dynamic categories = catObj;
                int count = (int)categories.Count;
                if (count > 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        string name = (string)categories.Item(i).Name;
                        if (name.Contains("Driver", StringComparison.OrdinalIgnoreCase)) return "Driver";
                        if (name.Contains("Security", StringComparison.OrdinalIgnoreCase)) return "Bảo mật";
                        if (name.Contains("Quality", StringComparison.OrdinalIgnoreCase)) return "Chất lượng";
                        if (name.Contains("Definition", StringComparison.OrdinalIgnoreCase)) return "Định nghĩa";
                    }
                }
            }
        }
        catch { }

        return "Phần mềm";
    }
}
