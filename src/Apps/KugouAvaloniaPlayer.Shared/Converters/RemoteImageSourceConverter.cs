using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace KugouAvaloniaPlayer.Converters;

public sealed class RemoteImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var source = value as string;
        if (string.IsNullOrWhiteSpace(source))
            return null;

        return !LocalImageSourceHelper.TryGetEmbeddedCoverFilePath(source, out _) &&
               LocalImageSourceHelper.GetLocalFilePath(source) == null
            ? source
            : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
