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

            if (isSelfTest)
            {
                // Self-test: Verify resolution of all services and ViewModels
                Console.WriteLine("Self-test: Kiểm tra giải quyết phụ thuộc (Dependency Injection)...");
                _ = host.Services.GetRequiredService<ILogService>();
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
                Console.WriteLine("Self-test: Kiểm tra nạp XAML MainWindow...");
                // Dựng thử MainWindow để phát hiện lỗi parse XAML hoặc ResourceDictionary
                var mainWindow = new MainWindow(vm);
                if (mainWindow == null)
                {
                    throw new InvalidOperationException("Không thể khởi tạo MainWindow XAML!");
                }
#endif

                Console.WriteLine("[SELFTEST] Smoke test khởi động thành công (ExitCode: 0)!");
                return 0;
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

    public static void ConfigureServices(IServiceCollection services)
    {
        // Core Services
        services.AddSingleton<ILogService, LogService>();
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
