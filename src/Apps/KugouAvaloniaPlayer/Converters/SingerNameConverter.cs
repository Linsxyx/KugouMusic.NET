using System;
using System.Globalization;
using Avalonia.Data.Converters;
using KuGou.Net.Abstractions.Models;

namespace KugouAvaloniaPlayer.Converters;

public class SingerNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SingerLite singer) return string.IsNullOrEmpty(singer.Name) ? "未知歌手" : singer.Name;

        return "数据错误";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}