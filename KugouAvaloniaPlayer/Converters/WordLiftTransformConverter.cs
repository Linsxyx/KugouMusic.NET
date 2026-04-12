using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace KugouAvaloniaPlayer.Converters;

public class WordLiftTransformConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double lift) return "translateY(0px)";
        return $"translateY({lift:0.##}px)";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
