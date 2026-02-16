using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TestMusic.Converters;

public class CountdownEnabledConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int countdown) return countdown <= 0;
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value;
    }
}