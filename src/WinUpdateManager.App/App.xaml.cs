#if WINDOWS || NET8_0_WINDOWS || WINDOWS7_0_OR_GREATER
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace WinUpdateManager.App;

public partial class App : Application
{
    private readonly IServiceProvider? _serviceProvider;

    public App()
    {
        DispatcherUnhandledException += (sender, args) =>
        {
            Program.LogAndShowStartupError(args.Exception, "DispatcherUnhandledException");
            args.Handled = false;
        };
        InitializeComponent();
    }

    public App(IServiceProvider serviceProvider) : this()
    {
        _serviceProvider = serviceProvider;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            if (_serviceProvider != null)
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                MainWindow = mainWindow;
                mainWindow.Show();
            }
        }
        catch (Exception ex)
        {
            Program.LogAndShowStartupError(ex, "App.OnStartup");
            Shutdown(1);
        }
    }
}
#else
namespace WinUpdateManager.App;

public class App
{
}
#endif
