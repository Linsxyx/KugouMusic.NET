using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace KugouAvaloniaPlayer.Converters;

public sealed class PlaybackQualityDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString()?.ToLowerInvariant() switch
        {
            "128" => "标准",
            "320" => "高品",
            "flac" => "无损",
            "high" => "Hi-Res",
            null or "" => "标准",
            var other => other
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "标准" => "128",
            "高品" => "320",
            "无损" => "flac",
            "Hi-Res" => "high",
            var other => other ?? "128"
        };
    }
}