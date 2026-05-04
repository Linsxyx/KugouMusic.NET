using System;
using System.Globalization;
using Avalonia.Data.Converters;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.Converters;

public class CloseBehaviorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is CloseBehavior behavior)
            return behavior switch
            {
                CloseBehavior.Exit => "退出程序",
                CloseBehavior.MinimizeToTray => "最小化到托盘",
                _ => behavior.ToString()
            };
        return "未知设置";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}