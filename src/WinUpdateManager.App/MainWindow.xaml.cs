#if WINDOWS || NET8_0_WINDOWS || WINDOWS7_0_OR_GREATER
using System.Windows;
using WinUpdateManager.App.ViewModels;

namespace WinUpdateManager.App;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
#else
namespace WinUpdateManager.App;

public class MainWindow
{
}
#endif
