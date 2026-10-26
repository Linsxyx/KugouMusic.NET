using System;
using System.Globalization;
using Avalonia.Data.Converters;
using KuGou.Net.Abstractions;

namespace KugouAvaloniaPlayer.Converters;

public sealed class PlaybackQualityDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString()?.ToLowerInvariant() switch
        {
            AudioQuality.Standard => "标准",
            AudioQuality.High => "高品",
            AudioQuality.Lossless => "无损",
            AudioQuality.HiRes => "Hi-Res",
            null or "" => "标准",
            var other => other
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "标准" => AudioQuality.Standard,
            "高品" => AudioQuality.High,
            "无损" => AudioQuality.Lossless,
            "Hi-Res" => AudioQuality.HiRes,
            var other => other ?? AudioQuality.Default
        };
    }
}
