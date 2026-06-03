using System;
using System.Globalization;
using Avalonia.Data.Converters;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.Converters;

public sealed class NowPlayingBackgroundSourceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is NowPlayingBackgroundSource source
            ? source switch
            {
                NowPlayingBackgroundSource.CustomImage => "自定义图片",
                _ => "歌曲封面"
            }
            : "歌曲封面";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}
