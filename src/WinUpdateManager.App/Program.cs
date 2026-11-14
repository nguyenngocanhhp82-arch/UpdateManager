using System.IO;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WinUpdateManager.App.ViewModels;
using WinUpdateManager.Core.Services;

namespace WinUpdateManager.App;

public static class Program
{
    public static bool SuppressErrorDialogForTesting { get; set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // 1. Global unhandled exception handlers
        AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
        {
            var ex = eventArgs.ExceptionObject as Exception ?? new Exception($"UnhandledException: {eventArgs.ExceptionObject}");
            LogAndShowStartupError(ex, "AppDomain.UnhandledException");
        };

        TaskScheduler.UnobservedTaskException += (sender, eventArgs) =>
        {
            LogAndShowStartupError(eventArgs.Exception, "TaskScheduler.UnobservedTaskException");
            eventArgs.SetObserved();
        };

        bool isSelfTest = args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(a, "-selftest", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(a, "/selftest", StringComparison.OrdinalIgnoreCase));

        bool isAutoUpgrade = args.Any(a => string.Equals(a, "--auto-upgrade", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(a, "-auto-upgrade", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(a, "/auto-upgrade", StringComparison.OrdinalIgnoreCase));

        try
        {
            if (isSelfTest)
            {
                Console.WriteLine("[WinUpdateManager] Chế độ kiểm tra khởi động tự động (--selftest)...");
            }

            IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    ConfigureServices(services);
                })
                .Build();

            // Self-test execution (CI/CD smoke test)
            if (isSelfTest)
            {
                Console.WriteLine("Self-test: Kiểm tra giải quyết phụ thuộc (Dependency Injection)...");
                _ = host.Services.GetRequiredService<ILogService>();
                _ = host.Services.GetRequiredService<IConfigService>();
                _ = host.Services.GetRequiredService<IProcessRunner>();
                _ = host.Services.GetRequiredService<IWingetService>();
                _ = host.Services.GetRequiredService<IWindowsUpdateService>();
                _ = host.Services.GetRequiredService<IHotfixService>();
                _ = host.Services.GetRequiredService<IPolicyService>();
                _ = host.Services.GetRequiredService<IScheduleService>();

                var vm = host.Services.GetRequiredService<MainWindowViewModel>();
                if (vm == null)
                {
                    throw new InvalidOperationException("Không thể khởi tạo MainWindowViewModel!");
                }

#if WINDOWS || NET8_0_WINDOWS || WINDOWS7_0_OR_GREATER
                Console.WriteLine("Self-test: Khởi tạo App và nạp tài nguyên XAML...");
                if (System.Windows.Application.Current == null)
                {
                    _ = new App(host.Services);
                }
                Console.WriteLine("Self-test: Kiểm tra nạp XAML MainWindow...");
                var mainWindow = new MainWindow(vm);
                if (mainWindow == null)
                {
                    throw new InvalidOperationException("Không thể khởi tạo MainWindow XAML!");
                }
#endif

                Console.WriteLine("[SELFTEST] Smoke test khởi động thành công (ExitCode: 0)!");
                return 0;
            }

            // Headless Scheduled Task runner (--auto-upgrade)
            if (isAutoUpgrade)
            {
                return RunHeadlessAutoUpgrade(host);
            }

#if WINDOWS || NET8_0_WINDOWS || WINDOWS7_0_OR_GREATER
            var app = new App(host.Services);
            return app.Run();
#else
            Console.WriteLine("WinUpdateManager.App hiện chỉ hỗ trợ giao diện trên Windows.");
            return 0;
#endif
        }
        catch (Exception ex)
        {
            LogAndShowStartupError(ex, "Program.Main");
            return 1;
        }
    }

    private static int RunHeadlessAutoUpgrade(IHost host)
    {
        var scheduleService = host.Services.GetRequiredService<IScheduleService>();
        var wingetService = host.Services.GetRequiredService<IWingetService>();
        var policyService = host.Services.GetRequiredService<IPolicyService>();
        var logPath = scheduleService.GetScheduleLogPath();

        void AppendAutoUpgradeLog(string text)
        {
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}";
                Console.WriteLine(line);
                File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        AppendAutoUpgradeLog("=================================================");
        AppendAutoUpgradeLog("BẮT ĐẦU CHẠY LỊCH NÂNG CẤP TỰ ĐỘNG (SCHEDULED TASK)");

        bool wasWuauservDisabled = policyService.IsWuauservDisabled();
        if (wasWuauservDisabled)
        {
            AppendAutoUpgradeLog("Trạng thái hiện tại: Windows Update đang bị khóa (Disabled).");
        }

        try
        {
            // 1. WhatsApp Fast Update
            AppendAutoUpgradeLog("Đang kiểm tra và cập nhật siêu tốc WhatsApp...");
            var whatsAppTask = wingetService.FastUpdateWhatsAppAsync().GetAwaiter().GetResult();
            AppendAutoUpgradeLog($"Kết quả cập nhật WhatsApp: ExitCode {whatsAppTask.ExitCode} (Thành công: {whatsAppTask.Succeeded})");

            // 2. Scheduled auto-upgrade is configured strictly for WhatsApp to prevent unwanted updates on other apps
            AppendAutoUpgradeLog("Hoàn tất tác vụ theo lịch: Chỉ cập nhật WhatsApp, bảo vệ an toàn cho các ứng dụng hệ thống khác.");

            // 3. Guarantee wuauserv remains locked if it was locked initially
            if (wasWuauservDisabled)
            {
                policyService.SetWuauservDisabledAsync(true).GetAwaiter().GetResult();
                AppendAutoUpgradeLog("Bảo đảm an toàn: Đã tái khóa dịch vụ Windows Update về trạng thái Disabled.");
            }

            AppendAutoUpgradeLog("HOÀN TẤT TIẾN TRÌNH LỊCH NÂNG CẤP TỰ ĐỘNG THÀNH CÔNG.");
            AppendAutoUpgradeLog("=================================================");
            return 0;
        }
        catch (Exception ex)
        {
            AppendAutoUpgradeLog($"LỖI TRONG TIẾN TRÌNH NÂNG CẤP TỰ ĐỘNG: {ex.Message}");
            if (wasWuauservDisabled)
            {
                try
                {
                    policyService.SetWuauservDisabledAsync(true).GetAwaiter().GetResult();
                }
                catch { }
            }
            return 1;
        }
    }

    public static void ConfigureServices(IServiceCollection services)
    {
        // Core Services
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IWingetService, WingetService>();
        services.AddSingleton<IWindowsUpdateService, WindowsUpdateService>();
        services.AddSingleton<IHotfixService, HotfixService>();
        services.AddSingleton<IPolicyService, PolicyService>();
        services.AddSingleton<IScheduleService, ScheduleService>();

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();

#if WINDOWS || NET8_0_WINDOWS || WINDOWS7_0_OR_GREATER
        // Views
        services.AddSingleton<App>();
        services.AddSingleton<MainWindow>();
#endif
    }

    public static void LogAndShowStartupError(Exception ex, string context, bool isSelfTest = false)
    {
        try
        {
            string logDir = GetStartupLogDirectory();
            Directory.CreateDirectory(logDir);
            string logFile = Path.Combine(logDir, "startup-error.log");

            var sb = new StringBuilder();
            sb.AppendLine("=========================================================================");
            sb.AppendLine($"[CRITICAL STARTUP ERROR] - {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Context: {context}");
            sb.AppendLine($"OS Version: {Environment.OSVersion}");
            sb.AppendLine($"Machine: {Environment.MachineName}");
            sb.AppendLine($"User: {Environment.UserName}");
            sb.AppendLine($"Exception Type: {ex.GetType().FullName}");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine($"StackTrace:\n{ex.StackTrace}");
            if (ex.InnerException != null)
            {
                sb.AppendLine($"--- Inner Exception: {ex.InnerException.GetType().FullName} ---");
                sb.AppendLine($"Inner Message: {ex.InnerException.Message}");
                sb.AppendLine($"Inner StackTrace:\n{ex.InnerException.StackTrace}");
            }
            sb.AppendLine("=========================================================================");
            sb.AppendLine();

            File.AppendAllText(logFile, sb.ToString(), Encoding.UTF8);

            Console.Error.WriteLine($"\n[WIN UPDATE MANAGER CRITICAL STARTUP ERROR - {context}]");
            Console.Error.WriteLine(ex.ToString());
            Console.Error.WriteLine($"Chi tiết đã được lưu tại: {logFile}\n");

            ShowErrorDialog(ex, logFile, isSelfTest);
        }
        catch (Exception fallbackEx)
        {
            try
            {
                Console.Error.WriteLine($"Không thể ghi startup-error.log: {fallbackEx.Message}");
                Console.Error.WriteLine($"Lỗi ban đầu: {ex}");
            }
            catch { }
        }
    }

    private static void ShowErrorDialog(Exception ex, string logFilePath, bool isSelfTest)
    {
        try
        {
            bool isNonInteractive = isSelfTest ||
                                    SuppressErrorDialogForTesting ||
                                    !Environment.UserInteractive ||
                                    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
                                    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

            if (isNonInteractive)
            {
                return;
            }

#if WINDOWS || NET8_0_WINDOWS || WINDOWS7_0_OR_GREATER
            string message =
                $"Ứng dụng Win Update Manager gặp sự cố nghiêm trọng khi khởi động và không thể tiếp tục.\n\n" +
                $"Chi tiết lỗi:\n{ex.Message}\n\n" +
                $"Tệp nhật ký ghi lỗi chi tiết đã được lưu tại:\n{logFilePath}\n\n" +
                $"Vui lòng kiểm tra tệp nhật ký trên để khắc phục.";

            System.Windows.MessageBox.Show(
                message,
                "Win Update Manager - Lỗi khởi động nghiêm trọng",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
#endif
        }
        catch { }
    }

    private static string GetStartupLogDirectory()
    {
        try
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrWhiteSpace(programData))
            {
                string path = Path.Combine(programData, "WinUpdateManager");
                Directory.CreateDirectory(path);
                return path;
            }
        }
        catch { }

        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                string path = Path.Combine(localAppData, "WinUpdateManager");
                Directory.CreateDirectory(path);
                return path;
            }
        }
        catch { }

        string temp = Path.Combine(Path.GetTempPath(), "WinUpdateManager");
        Directory.CreateDirectory(temp);
        return temp;
    }
}
