using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace KugouAvaloniaPlayer.Converters;

public class EnumEqualityConverter : IValueConverter
{
    public static readonly EnumEqualityConverter Equal = new(false);
    public static readonly EnumEqualityConverter NotEqual = new(true);

    private readonly bool _invert;

    public EnumEqualityConverter(bool invert)
    {
        _invert = invert;
    }

    public EnumEqualityConverter()
    {
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return _invert;
        var result = value.ToString() == parameter.ToString();
        return _invert ? !result : result;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}