using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace KugouAvaloniaPlayer.Converters;

public sealed class SecondsToMinutesSecondsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var seconds = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            _ => 0
        };

        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            return "00:00";

        var totalSeconds = (int)seconds;

        return $"{totalSeconds / 60:D2}:{totalSeconds % 60:D2}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}