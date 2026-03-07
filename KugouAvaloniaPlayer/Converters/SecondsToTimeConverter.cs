using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace KugouAvaloniaPlayer.Converters;

public class SecondsToMinutesSecondsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                return "00:00";
            var totalSeconds = (int)Math.Floor(seconds);

            var minutes = totalSeconds / 60;
            var secs = totalSeconds % 60;

            return $"{minutes:D2}:{secs:D2}";
        }

        return "00:00";
    }


    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}