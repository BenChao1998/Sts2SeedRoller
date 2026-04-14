using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SeedUi.Converters;

internal sealed class LogLevelToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value?.ToString() ?? string.Empty;
        return level switch
        {
            "错误" => new SolidColorBrush(Color.FromRgb(239, 68, 68)),   // Danger
            "警告" => new SolidColorBrush(Color.FromRgb(245, 158, 11)),  // Warning
            "信息" => new SolidColorBrush(Color.FromRgb(59, 130, 246)),  // Primary
            _ => new SolidColorBrush(Color.FromRgb(100, 116, 139))       // TextMuted
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
