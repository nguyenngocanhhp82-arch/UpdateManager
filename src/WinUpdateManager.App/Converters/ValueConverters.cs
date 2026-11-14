#if WINDOWS
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WinUpdateManager.Core.Models;

namespace WinUpdateManager.App.Converters;

public class LogLevelToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush InfoBrush = new(Color.FromRgb(0x4B, 0x55, 0x63));     // Gray-600
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(0xD9, 0x77, 0x06));  // Amber-600
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xDC, 0x26, 0x26));    // Red-600
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x16, 0xA3, 0x4A));  // Green-600
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(0x1F, 0x29, 0x37));  // Dark Gray

    static LogLevelToBrushConverter()
    {
        InfoBrush.Freeze();
        WarningBrush.Freeze();
        ErrorBrush.Freeze();
        SuccessBrush.Freeze();
        DefaultBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LogLevel level)
        {
            return level switch
            {
                LogLevel.Info => InfoBrush,
                LogLevel.Warning => WarningBrush,
                LogLevel.Error => ErrorBrush,
                LogLevel.Success => SuccessBrush,
                _ => DefaultBrush
            };
        }
        return DefaultBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}

public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Visible;
        }
        return false;
    }
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility != Visibility.Visible;
        }
        return false;
    }
}
#else
namespace WinUpdateManager.App.Converters;

public class LogLevelToBrushConverter { }
public class InverseBooleanConverter { }
public class BooleanToVisibilityConverter { }
public class InverseBooleanToVisibilityConverter { }
#endif
