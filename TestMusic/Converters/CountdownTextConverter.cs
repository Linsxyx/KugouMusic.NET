using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace TestMusic.Converters;

public class CountdownTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int countdown)
        {
            if (countdown > 0)
                return $"{countdown}s";
            return "发送验证码";
        }

        return "发送验证码";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value;
    }
}